using HeronWin.Face.Models;
using HeronWin.Face.ViewModels;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;

namespace HeronWin.Face.Services;

public sealed class FacePipeClient : IAsyncDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly CancellationTokenSource _cancellationSource = new();
    private Task? _backgroundTask;
    private bool _hasEverConnected;

    public FacePipeClient(MainViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public void Start()
    {
        _backgroundTask ??= Task.Run(() => RunAsync(_cancellationSource.Token));
    }

    public async ValueTask DisposeAsync()
    {
        _cancellationSource.Cancel();
        if (_backgroundTask is not null)
        {
            await _backgroundTask;
        }

        _cancellationSource.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(
                    ".",
                    _viewModel.Settings.PipeName,
                    PipeDirection.In,
                    PipeOptions.Asynchronous);
                await pipe.ConnectAsync(1500, cancellationToken);
                _hasEverConnected = true;
                _viewModel.SetConnected(true);
                using var reader = new StreamReader(pipe);

                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (line is null)
                    {
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var payload = JsonSerializer.Deserialize<FaceStatusMessage>(line);
                    if (payload is not null)
                    {
                        _viewModel.ApplyMessage(payload);
                    }
                }

                _viewModel.SetConnected(false);
                if (_hasEverConnected && !cancellationToken.IsCancellationRequested)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown());
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                _viewModel.SetConnected(false);
                if (_hasEverConnected && !cancellationToken.IsCancellationRequested)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown());
                    break;
                }
            }

            try
            {
                await Task.Delay(1500, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}