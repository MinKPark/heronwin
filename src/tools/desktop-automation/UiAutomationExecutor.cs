using System.Collections.Concurrent;

namespace HeronWin.Body.DesktopAutomation;

public sealed class UiAutomationExecutor : IDisposable
{
    private readonly BlockingCollection<IWorkItem> _queue = new();
    private readonly Thread _thread;
    private bool _disposed;

    public UiAutomationExecutor()
    {
        _thread = new Thread(RunLoop)
        {
            Name = "body-windows-uia-sta",
            IsBackground = true,
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<T> RunAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(UiAutomationExecutor));
        }

        var item = new WorkItem<T>(action);
        CancellationTokenRegistration registration = default;

        if (cancellationToken.CanBeCanceled)
        {
            registration = cancellationToken.Register(() => item.TrySetCanceled(cancellationToken));
            _ = item.Task.ContinueWith(
                _ => registration.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        try
        {
            _queue.Add(item, cancellationToken);
        }
        catch
        {
            registration.Dispose();
            throw;
        }

        return item.Task;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(2));
        _queue.Dispose();
    }

    private void RunLoop()
    {
        foreach (var item in _queue.GetConsumingEnumerable())
        {
            item.Execute();
        }
    }

    private interface IWorkItem
    {
        void Execute();
    }

    private sealed class WorkItem<T>(Func<T> action) : IWorkItem
    {
        private readonly TaskCompletionSource<T> _taskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<T> Task => _taskCompletionSource.Task;

        public void Execute()
        {
            if (_taskCompletionSource.Task.IsCompleted)
            {
                return;
            }

            try
            {
                _taskCompletionSource.TrySetResult(action());
            }
            catch (Exception ex)
            {
                _taskCompletionSource.TrySetException(ex);
            }
        }

        public void TrySetCanceled(CancellationToken cancellationToken)
        {
            _taskCompletionSource.TrySetCanceled(cancellationToken);
        }
    }
}
