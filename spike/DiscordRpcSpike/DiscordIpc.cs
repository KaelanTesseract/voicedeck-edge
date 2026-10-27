using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;

namespace VoicedeckEdge.Spike;

/// <summary>
/// Minimal client for the Discord desktop app's local RPC over the named pipe
/// \\.\pipe\discord-ipc-N. Frame = int32 LE opcode, int32 LE length, UTF-8 JSON.
/// </summary>
public sealed class DiscordIpc : IAsyncDisposable
{
    private const int OpHandshake = 0;
    private const int OpFrame = 1;
    private const int OpClose = 2;
    private const int OpPing = 3;
    private const int OpPong = 4;

    private readonly NamedPipeClientStream _pipe;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonObject>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private Task? _readLoop;

    public int PipeIndex { get; }
    public event Action<JsonObject>? EventReceived;

    private DiscordIpc(NamedPipeClientStream pipe, int index)
    {
        _pipe = pipe;
        PipeIndex = index;
    }

    /// <summary>Tries discord-ipc-0 .. discord-ipc-9 and returns the first that connects.</summary>
    public static async Task<DiscordIpc?> ConnectAsync()
    {
        for (var i = 0; i < 10; i++)
        {
            var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(500);
                return new DiscordIpc(pipe, i);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                await pipe.DisposeAsync();
            }
        }
        return null;
    }

    /// <summary>Sends HANDSHAKE and waits for the READY dispatch.</summary>
    public async Task<JsonObject> HandshakeAsync(string clientId, TimeSpan timeout)
    {
        var ready = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending["__ready"] = ready;
        _readLoop = Task.Run(ReadLoopAsync);
        await WriteFrameAsync(OpHandshake, new JsonObject { ["v"] = 1, ["client_id"] = clientId });
        return await ready.Task.WaitAsync(timeout);
    }

    /// <summary>Sends a command and waits for the response with the same nonce.</summary>
    public async Task<JsonObject> CommandAsync(string cmd, JsonObject? args, TimeSpan timeout, string? evt = null)
    {
        var nonce = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[nonce] = tcs;
        var payload = new JsonObject { ["cmd"] = cmd, ["args"] = args ?? new JsonObject(), ["nonce"] = nonce };
        if (evt != null) payload["evt"] = evt;
        await WriteFrameAsync(OpFrame, payload);
        try
        {
            return await tcs.Task.WaitAsync(timeout);
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
        await _writeLock.WaitAsync();
        try
        {
            await _pipe.WriteAsync(frame, _cts.Token);
            await _pipe.FlushAsync(_cts.Token);
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
                var body = new byte[len];
                await _pipe.ReadExactlyAsync(body, _cts.Token);
                var json = JsonNode.Parse(body) as JsonObject ?? new JsonObject();

                switch (op)
                {
                    case OpPing:
                        await WriteFrameAsync(OpPong, json);
                        break;
                    case OpClose:
                        FailAll(new IOException($"Discord hat die Verbindung geschlossen: {json["message"]}"));
                        return;
                    case OpFrame:
                        Dispatch(json);
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or EndOfStreamException)
        {
            FailAll(ex);
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
        if (msg["evt"]?.GetValue<string>() == "READY" && _pending.TryRemove("__ready", out var ready))
        {
            ready.TrySetResult(msg);
            return;
        }
        EventReceived?.Invoke(msg);
    }

    private void FailAll(Exception ex)
    {
        foreach (var tcs in _pending.Values) tcs.TrySetException(ex);
        _pending.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        await _pipe.DisposeAsync();
        if (_readLoop != null)
        {
            try { await _readLoop; } catch { /* reader ends with the pipe */ }
        }
        _cts.Dispose();
    }
}
