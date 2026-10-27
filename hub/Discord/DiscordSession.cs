using System.Text.Json.Nodes;
using VoicedeckEdge.Hub.Infrastructure;

namespace VoicedeckEdge.Hub.Discord;

internal enum DiscordStatus
{
    NotConfigured,
    InvalidClientId,
    DiscordNotRunning,
    Connecting,
    Unauthorized,
    Authorizing,
    Ready,
}

/// <summary>
/// Keeps one RPC connection to the Discord app alive: reconnects with backoff, signs in with the
/// stored token (refreshing it when needed) and runs the PKCE authorization when the user asks.
/// </summary>
internal sealed class DiscordSession(HubSettings settings, TokenStore tokenStore, DiscordOAuth oauth)
{
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _authorizeLock = new(1, 1);
    private TaskCompletionSource _wake = NewWake();
    private DiscordIpc? _ipc;
    private string? _clientId;
    private JsonObject? _pendingMe; // local user from READY, kept until authorization finishes

    public DiscordStatus Status { get; private set; } = DiscordStatus.NotConfigured;

    public event Action<DiscordStatus>? StatusChanged;

    /// <summary>Signed in; carries the local user from READY. Raised on a worker thread.</summary>
    public event Action<DiscordIpc, JsonObject>? Ready;

    /// <summary>The signed-in connection ended.</summary>
    public event Action? Lost;

    public event Action<string, JsonObject>? EventReceived;

    /// <summary>Drop the current connection and start over (e.g. after the client ID changed).</summary>
    public void Restart() => Interlocked.Exchange(ref _wake, NewWake()).TrySetResult();

    public async Task RunAsync(CancellationToken ct)
    {
        var backoff = MinBackoff;
        while (!ct.IsCancellationRequested)
        {
            var wake = _wake.Task;
            var clientId = settings.ClientId;
            if (!HubSettings.IsValidClientId(clientId))
            {
                SetStatus(DiscordStatus.NotConfigured);
                await WaitAsync(wake, Timeout.InfiniteTimeSpan, ct);
                continue;
            }

            var ipc = await DiscordIpc.ConnectAsync(ct);
            if (ipc == null)
            {
                SetStatus(DiscordStatus.DiscordNotRunning);
                await WaitAsync(wake, backoff, ct);
                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
                continue;
            }

            backoff = MinBackoff;
            var wasReady = false;
            try
            {
                SetStatus(DiscordStatus.Connecting);
                var readyData = await ipc.HandshakeAsync(clientId!);
                var me = readyData["user"] as JsonObject ?? new JsonObject();
                ipc.EventReceived += ForwardEvent;
                _ipc = ipc;
                _clientId = clientId;

                if (await TryAuthenticateWithStoredTokenAsync(ipc, clientId!, ct))
                {
                    wasReady = true;
                    SetReady(ipc, me);
                }
                else
                {
                    SetStatus(DiscordStatus.Unauthorized);
                    _pendingMe = me;
                }

                await Task.WhenAny(ipc.Closed, wake, Task.Delay(Timeout.Infinite, ct));
                if (ipc.Closed.IsCompleted) Log.Info($"Discord-Verbindung beendet: {ipc.Closed.Result}");
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or DiscordRpcException or HttpRequestException)
            {
                Log.Warn($"Discord-Verbindung fehlgeschlagen: {ex.Message}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            finally
            {
                _ipc = null;
                ipc.EventReceived -= ForwardEvent;
                wasReady |= Status == DiscordStatus.Ready;
                await ipc.DisposeAsync();
                if (wasReady) Lost?.Invoke();
            }

            // Close code 4000 = "Invalid Client ID": retrying will not help until the user fixes it.
            if (ipc.Closed.IsCompletedSuccessfully && ipc.Closed.Result.Contains("4000"))
            {
                SetStatus(DiscordStatus.InvalidClientId);
                await WaitAsync(_wake.Task, Timeout.InfiniteTimeSpan, ct);
            }
            else if (!ct.IsCancellationRequested && !wake.IsCompleted)
            {
                SetStatus(DiscordStatus.Connecting);
                await WaitAsync(wake, MinBackoff, ct);
            }
        }
    }

    /// <summary>
    /// Runs the one-time authorization: Discord shows its consent dialog, then the code is exchanged
    /// (PKCE, no secret) and the tokens are stored encrypted. Returns true when signed in.
    /// </summary>
    public async Task<bool> AuthorizeAsync(CancellationToken ct)
    {
        if (!await _authorizeLock.WaitAsync(0, ct)) return false;
        try
        {
            var ipc = _ipc;
            var clientId = _clientId;
            if (ipc == null || clientId == null || Status != DiscordStatus.Unauthorized) return false;

            SetStatus(DiscordStatus.Authorizing);
            var pkce = DiscordOAuth.CreatePkce();
            var scopes = new JsonArray(DiscordOAuth.Scopes.Select(s => (JsonNode?)s).ToArray());
            var data = await ipc.CommandAsync("AUTHORIZE", new JsonObject
            {
                ["client_id"] = clientId,
                ["scopes"] = scopes,
                ["code_challenge"] = pkce.Challenge,
                ["code_challenge_method"] = "S256",
            }, timeout: TimeSpan.FromMinutes(5));

            var code = data.Str("code") ?? throw new IOException("Discord hat keinen Autorisierungscode geliefert.");
            var tokens = await oauth.ExchangeCodeAsync(clientId, code, pkce.Verifier, ct);
            tokenStore.Save(tokens);
            await ipc.CommandAsync("AUTHENTICATE", new JsonObject { ["access_token"] = tokens.AccessToken });
            Log.Info("Autorisierung erfolgreich.");
            SetReady(ipc, _pendingMe ?? new JsonObject());
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or DiscordRpcException or HttpRequestException or OAuthException)
        {
            Log.Warn($"Autorisierung fehlgeschlagen: {ex.Message}");
            if (_ipc != null) SetStatus(DiscordStatus.Unauthorized);
            return false;
        }
        finally
        {
            _authorizeLock.Release();
        }
    }

    private async Task<bool> TryAuthenticateWithStoredTokenAsync(DiscordIpc ipc, string clientId, CancellationToken ct)
    {
        var tokens = tokenStore.Load();
        if (tokens == null) return false;
        if (tokens.ClientId != clientId)
        {
            Log.Info("Client-ID geändert – gespeicherte Anmeldung verworfen.");
            tokenStore.Clear();
            return false;
        }

        var refreshed = false;
        if (tokens.ExpiresAt < DateTimeOffset.UtcNow.AddMinutes(5))
        {
            tokens = await RefreshAsync(tokens, ct);
            if (tokens == null) return false;
            refreshed = true;
        }

        JsonNode? auth;
        try
        {
            auth = await ipc.CommandAsync("AUTHENTICATE", new JsonObject { ["access_token"] = tokens.AccessToken });
        }
        catch (DiscordRpcException ex) when (!refreshed)
        {
            Log.Info($"Gespeichertes Token abgelehnt ({ex.Code}), versuche Erneuerung.");
            tokens = await RefreshAsync(tokens, ct);
            if (tokens == null) return false;
            auth = await ipc.CommandAsync("AUTHENTICATE", new JsonObject { ["access_token"] = tokens.AccessToken });
        }

        // A sign-in from an older version may lack a newer scope: ask the user to connect once more.
        if (!DiscordOAuth.HasRequiredScopes(auth))
        {
            Log.Info("Neue Discord-Berechtigung nötig (Direktnachrichten) - bitte einmal neu verbinden.");
            return false;
        }
        return true;
    }

    private async Task<DiscordTokens?> RefreshAsync(DiscordTokens tokens, CancellationToken ct)
    {
        try
        {
            var fresh = await oauth.RefreshAsync(tokens, ct);
            tokenStore.Save(fresh);
            Log.Info("Anmeldung erneuert.");
            return fresh;
        }
        catch (OAuthException ex) when (ex.IsInvalidGrant)
        {
            Log.Info("Anmeldung abgelaufen oder widerrufen – neue Autorisierung nötig.");
            tokenStore.Clear();
            return null;
        }
        catch (HttpRequestException ex)
        {
            // Offline: keep the old token, AUTHENTICATE may still accept it.
            Log.Warn($"Erneuerung nicht möglich (offline?): {ex.Message}");
            return tokens;
        }
    }

    private void SetReady(DiscordIpc ipc, JsonObject me)
    {
        SetStatus(DiscordStatus.Ready);
        Ready?.Invoke(ipc, me);
    }

    private void ForwardEvent(string evt, JsonObject data) => EventReceived?.Invoke(evt, data);

    private void SetStatus(DiscordStatus status)
    {
        if (Status == status) return;
        Status = status;
        Log.Info($"Status: {status}");
        StatusChanged?.Invoke(status);
    }

    private static async Task WaitAsync(Task wake, TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.WhenAny(wake, Task.Delay(delay, ct));
        }
        catch (OperationCanceledException) { }
    }

    private static TaskCompletionSource NewWake() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
