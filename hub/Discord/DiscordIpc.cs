using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;

namespace VoicedeckEdge.Hub.Discord;

/// <summary>Discord answered a command with evt=ERROR.</summary>
internal sealed class DiscordRpcException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}

/// <summary>
/// Client for the Discord desktop app's local RPC over the named pipe \\.\pipe\discord-ipc-N.
/// Frame = int32 LE opcode, int32 LE length, UTF-8 JSON.
/// </summary>
internal sealed class DiscordIpc : IAsyncDisposable
{
    private const int OpHandshake = 0;
    private const int OpFrame = 1;
    private const int OpClose = 2;
    private const int OpPing = 3;
    private const int OpPong = 4;
    private const int MaxFrameBytes = 16 * 1024 * 1024;

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly NamedPipeClientStream _pipe;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonObject>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource<string> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<JsonObject>? _ready;
    private Task? _readLoop;

    /// <summary>Raised on the reader thread for every DISPATCH that is not a command response.</summary>
    public event Action<string, JsonObject>? EventReceived;

    /// <summary>Completes (with a reason) when the pipe is closed by either side.</summary>
    public Task<string> Closed => _closed.Task;

    private DiscordIpc(NamedPipeClientStream pipe) => _pipe = pipe;

    /// <summary>Tries discord-ipc-0 .. discord-ipc-9 and returns the first that connects, or null.</summary>
    public static async Task<DiscordIpc?> ConnectAsync(CancellationToken ct)
    {
        for (var i = 0; i < 10; i++)
        {
            var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(300, ct);
                return new DiscordIpc(pipe);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                await pipe.DisposeAsync();
            }
        }
        return null;
    }

    /// <summary>Sends HANDSHAKE and returns the READY dispatch data (contains the local user).</summary>
    public async Task<JsonObject> HandshakeAsync(string clientId)
    {
        _ready = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _readLoop = Task.Run(ReadLoopAsync);
        await WriteFrameAsync(OpHandshake, new JsonObject { ["v"] = 1, ["client_id"] = clientId });
        var ready = await _ready.Task.WaitAsync(DefaultTimeout);
        return ready["data"] as JsonObject ?? new JsonObject();
    }

    /// <summary>Sends a command and returns its data, or throws <see cref="DiscordRpcException"/>.</summary>
    public async Task<JsonNode?> CommandAsync(string cmd, JsonObject? args = null, string? evt = null, TimeSpan? timeout = null)
    {
        var nonce = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[nonce] = tcs;
        var payload = new JsonObject { ["cmd"] = cmd, ["args"] = args ?? new JsonObject(), ["nonce"] = nonce };
        if (evt != null) payload["evt"] = evt;
        try
        {
            await WriteFrameAsync(OpFrame, payload);
            var response = await tcs.Task.WaitAsync(timeout ?? DefaultTimeout);
            if (response["evt"]?.GetValue<string>() == "ERROR")
            {
                var data = response["data"];
                throw new DiscordRpcException(
                    data?["code"]?.GetValue<int>() ?? 0,
                    data?["message"]?.GetValue<string>() ?? "Unknown RPC error");
            }
            return response["data"];
        }
        finally
        {
            _pending.TryRemove(nonce, out _);
        }
    }

    private async Task WriteFrameAsync(int op, JsonObject payload)
    {
        var body = Encoding.UTF8.GetBytes(payload.ToJsonString());
        var frame = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), op);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), body.Length);
        body.CopyTo(frame, 8);
        await _writeLock.WaitAsync(_cts.Token);
        try
        {
            await _pipe.WriteAsync(frame, _cts.Token);
            await _pipe.FlushAsync(_cts.Token);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Shutdown("write failed");
            throw new IOException("Verbindung zu Discord unterbrochen.", ex);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        var header = new byte[8];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await _pipe.ReadExactlyAsync(header, _cts.Token);
                var op = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
                var len = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
                if (len < 0 || len > MaxFrameBytes) throw new IOException($"Ungültige Framegröße {len}");
                var body = new byte[len];
                await _pipe.ReadExactlyAsync(body, _cts.Token);
                var json = JsonNode.Parse(body) as JsonObject ?? new JsonObject();

                switch (op)
                {
                    case OpPing:
                        await WriteFrameAsync(OpPong, json);
                        break;
                    case OpClose:
                        Shutdown($"Discord closed the connection: {json["code"]} {json["message"]}");
                        return;
                    case OpFrame:
                        Dispatch(json);
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or EndOfStreamException
                                       or ObjectDisposedException or System.Text.Json.JsonException)
        {
            Shutdown(ex.GetType().Name);
        }
    }

    private void Dispatch(JsonObject msg)
    {
        var nonce = msg["nonce"]?.GetValue<string>();
        if (nonce != null && _pending.TryGetValue(nonce, out var tcs))
        {
            tcs.TrySetResult(msg);
            return;
        }
        var evt = msg["evt"]?.GetValue<string>();
        if (evt == "READY")
        {
            _ready?.TrySetResult(msg);
            return;
        }
        if (evt != null)
        {
            try
            {
                EventReceived?.Invoke(evt, msg["data"] as JsonObject ?? new JsonObject());
            }
            catch (Exception ex)
            {
                Infrastructure.Log.Error($"Fehler beim Verarbeiten von {evt}", ex);
            }
        }
    }

    private void Shutdown(string reason)
    {
        if (!_closed.TrySetResult(reason)) return;
        _cts.Cancel();
        var ex = new IOException("Verbindung zu Discord geschlossen.");
        _ready?.TrySetException(ex);
        foreach (var tcs in _pending.Values) tcs.TrySetException(ex);
        _pending.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        Shutdown("disposed");
        await _pipe.DisposeAsync();
        if (_readLoop != null)
        {
            try { await _readLoop; } catch { /* reader ends with the pipe */ }
        }
    }
}
