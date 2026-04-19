using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Channels;

namespace HeronWin.Brain;

internal static class FaceBridge
{
    private static readonly object Sync = new();
    private static readonly List<FaceClientConnection> Clients = [];
    private static readonly Channel<FaceStatusMessage> Outbound = Channel.CreateUnbounded<FaceStatusMessage>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static CancellationTokenSource? _cancellationSource;
    private static Task? _acceptLoopTask;
    private static Task? _dispatchLoopTask;
    private static FaceStatusMessage? _lastMessage;
    private static string _pipeName = "heronwin.face";
    private static bool _isEnabled;

    public static void Initialize(AppConfig config)
    {
        if (_isEnabled || !config.FacePipeEnabled)
        {
            DebugTrace.WriteEvent("face_bridge.init.skip",
                _isEnabled
                    ? "already initialized"
                    : "FACE_PIPE_ENABLED is false");
            return;
        }

        _pipeName = string.IsNullOrWhiteSpace(config.FacePipeName)
            ? "heronwin.face"
            : config.FacePipeName.Trim();
        _cancellationSource = new CancellationTokenSource();
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cancellationSource.Token));
        _dispatchLoopTask = Task.Run(() => DispatchLoopAsync(_cancellationSource.Token));
        _isEnabled = true;
        DebugTrace.WriteEvent("face_bridge.init", $"pipe=\\\\.\\pipe\\{_pipeName}");
    }

    public static async Task ShutdownAsync()
    {
        if (!_isEnabled || _cancellationSource is null)
        {
            return;
        }

        DebugTrace.WriteEvent("face_bridge.shutdown", "stopping pipe server");
        _cancellationSource.Cancel();
        Outbound.Writer.TryComplete();

        if (_acceptLoopTask is not null)
        {
            await AwaitQuietlyAsync(_acceptLoopTask);
        }

        if (_dispatchLoopTask is not null)
        {
            await AwaitQuietlyAsync(_dispatchLoopTask);
        }

        List<FaceClientConnection> clients;
        lock (Sync)
        {
            clients = [.. Clients];
            Clients.Clear();
        }

        foreach (var client in clients)
        {
            client.Dispose();
        }

        _cancellationSource.Dispose();
        _cancellationSource = null;
        _acceptLoopTask = null;
        _dispatchLoopTask = null;
        _isEnabled = false;
    }

    public static void PublishStatus(
        string state,
        string headline,
        string detail,
        string? transcript = null,
        string? toolName = null)
    {
        if (!_isEnabled)
        {
            return;
        }

        var message = new FaceStatusMessage(
            state.Trim(),
            headline.Trim(),
            detail.Trim(),
            transcript,
            toolName,
            DateTimeOffset.UtcNow.ToString("O"));
        _lastMessage = message;
        Outbound.Writer.TryWrite(message);
    }

    private static async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.Out,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(cancellationToken);

                var client = new FaceClientConnection(pipe, new StreamWriter(pipe) { AutoFlush = true });
                int clientCount;
                lock (Sync)
                {
                    Clients.Add(client);
                    clientCount = Clients.Count;
                }

                DebugTrace.WriteEvent("face_bridge.client_connected", $"clients={clientCount}");

                if (_lastMessage is not null)
                {
                    var payload = JsonSerializer.Serialize(_lastMessage, SerializerOptions);
                    await client.Writer.WriteLineAsync(payload);
                }

                pipe = null;
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                DebugTrace.WriteEvent("face_bridge.accept_error", ex.Message);
                pipe?.Dispose();
            }
        }
    }

    private static async Task DispatchLoopAsync(CancellationToken cancellationToken)
    {
        await foreach (var message in Outbound.Reader.ReadAllAsync(cancellationToken))
        {
            var payload = JsonSerializer.Serialize(message, SerializerOptions);
            List<FaceClientConnection> clients;
            lock (Sync)
            {
                clients = [.. Clients];
            }

            foreach (var client in clients)
            {
                try
                {
                    if (!client.Pipe.IsConnected)
                    {
                        DebugTrace.WriteEvent("face_bridge.client_disconnected", "pipe no longer connected");
                        RemoveClient(client);
                        continue;
                    }

                    await client.Writer.WriteLineAsync(payload);
                }
                catch (Exception ex)
                {
                    DebugTrace.WriteEvent("face_bridge.client_disconnected", ex.Message);
                    RemoveClient(client);
                }
            }
        }
    }

    private static void RemoveClient(FaceClientConnection client)
    {
        lock (Sync)
        {
            Clients.Remove(client);
        }

        client.Dispose();
    }

    private static async Task AwaitQuietlyAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class FaceClientConnection : IDisposable
    {
        public FaceClientConnection(NamedPipeServerStream pipe, StreamWriter writer)
        {
            Pipe = pipe;
            Writer = writer;
        }

        public NamedPipeServerStream Pipe { get; }

        public StreamWriter Writer { get; }

        public void Dispose()
        {
            Writer.Dispose();
            Pipe.Dispose();
        }
    }

    private sealed record FaceStatusMessage(
        string State,
        string Headline,
        string Detail,
        string? Transcript,
        string? ToolName,
        string TimestampUtc);
}