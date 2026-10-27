using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using VoicedeckEdge.Hub.Infrastructure;
using VoicedeckEdge.Hub.State;

namespace VoicedeckEdge.Hub.Server;

/// <summary>Asks the user on the PC whether a new widget may connect.</summary>
internal interface IPairingPrompt
{
    Task<bool> AskAsync(string origin, CancellationToken ct);
}

/// <summary>
/// Local server for the widget: WebSocket on /ws, avatars on /img/..., reachable only via localhost.
/// The port is fixed so the widget's manifest permission always matches.
/// </summary>
internal sealed class HubServer
{
    public const int Port = 47820;
    public const int Protocol = 1;
    public static readonly string BaseUrl = $"http://localhost:{Port}";
    private static readonly TimeSpan DenyCooldown = TimeSpan.FromSeconds(5);

    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<Guid, WidgetConnection> _connections = new();
    private readonly SemaphoreSlim _promptLock = new(1, 1);
    private DateTime _lastDeny = DateTime.MinValue;
    private readonly ConcurrentDictionary<string, byte> _refusedOrigins = new();

    public StateStore State { get; }
    public HubController Controller { get; }
    public PairingStore Pairings { get; }
    public ImageCache Images { get; }
    public IPairingPrompt Prompt { get; }

    /// <summary>Started with --dev: also accept http://localhost pages (browser tests).</summary>
    public bool AllowLocalDevPages { get; init; }

    /// <summary>Random per hub run; required on /img so other local pages cannot use the hub as an image proxy.</summary>
    public string ImageKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public static string HubVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public HubServer(StateStore state, HubController controller, PairingStore pairings, ImageCache images, IPairingPrompt prompt)
    {
        State = state;
        Controller = controller;
        Pairings = pairings;
        Images = images;
        Prompt = prompt;
        _listener.Prefixes.Add($"{BaseUrl}/");

        state.SectionChanged += (section, data) =>
            Broadcast(new JsonObject { ["t"] = "state", ["section"] = section, ["data"] = data?.DeepClone() });
        state.SpeakingChanged += (userId, speaking) =>
            Broadcast(new JsonObject { ["t"] = "speaking", ["userId"] = userId, ["speaking"] = speaking });
        pairings.Revoked += pairingId =>
        {
            foreach (var c in _connections.Values.Where(c => c.PairingId == pairingId)) c.Abort("unpaired");
        };
    }

    /// <summary>Starts listening; throws <see cref="HttpListenerException"/> when the port is taken.</summary>
    public void Start() => _listener.Start();

    public async Task RunAsync(CancellationToken ct)
    {
        await using var stopOnCancel = ct.Register(() => _listener.Stop());
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                break;
            }
            _ = Task.Run(() => HandleAsync(ctx, ct), ct);
        }
        foreach (var c in _connections.Values) c.Abort("shutdown");
    }

    private async Task HandleAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? "/";
        try
        {
            // http.sys listens on all interfaces and only filters by the Host header, which a device on the
            // network could fake. Only this PC may talk to the hub.
            if (!IsLocalRequest(ctx.Request.RemoteEndPoint))
            {
                ctx.Response.StatusCode = 403;
                ctx.Response.Close();
                return;
            }

            if (path == "/ws" && ctx.Request.IsWebSocketRequest)
            {
                var rawOrigin = ctx.Request.Headers["Origin"];
                if (!OriginPolicy.IsAllowed(rawOrigin, AllowLocalDevPages))
                {
                    // Once per origin and run: a page retrying in a loop must not be able to flood the log.
                    var shown = CleanOrigin(rawOrigin);
                    if (_refusedOrigins.Count < 20 && _refusedOrigins.TryAdd(shown, 0))
                        Log.Warn($"Verbindung von Webseite abgewiesen (Herkunft: {shown}).");
                    await WriteAsync(ctx, 403, "text/plain", "forbidden origin");
                    return;
                }
                var origin = CleanOrigin(rawOrigin);
                var ws = await ctx.AcceptWebSocketAsync(subProtocol: null);
                var connection = new WidgetConnection(this, ws.WebSocket, origin);
                _connections[connection.Id] = connection;
                Log.Info($"Widget verbunden (Herkunft: {origin}).");
                try
                {
                    await connection.RunAsync(ct);
                }
                finally
                {
                    _connections.TryRemove(connection.Id, out _);
                    Log.Info("Widget getrennt.");
                }
            }
            else if (path.StartsWith("/img/", StringComparison.Ordinal))
            {
                await ServeImageAsync(ctx, path, ct);
            }
            else if (path == "/health")
            {
                await WriteAsync(ctx, 200, "application/json", $"{{\"app\":\"voicedeck-edge-hub\",\"proto\":{Protocol}}}");
            }
            else
            {
                await WriteAsync(ctx, 404, "text/plain", "not found");
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException)
        {
            // client went away
        }
        catch (Exception ex)
        {
            Log.Error("Fehler bei Anfrage", ex);
            try { ctx.Response.Abort(); } catch { }
        }
    }

    private async Task ServeImageAsync(HttpListenerContext ctx, string path, CancellationToken ct)
    {
        var key = ctx.Request.QueryString["k"];
        if (key == null || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(key), Encoding.ASCII.GetBytes(ImageKey)))
        {
            await WriteAsync(ctx, 403, "text/plain", "forbidden");
            return;
        }
        var image = await Images.GetAsync(path, ct);
        if (image == null)
        {
            await WriteAsync(ctx, 404, "text/plain", "not found");
            return;
        }
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = image.ContentType;
        ctx.Response.Headers["Cache-Control"] = "private, max-age=3600";
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.ContentLength64 = image.Data.Length;
        await ctx.Response.OutputStream.WriteAsync(image.Data, ct);
        ctx.Response.Close();
    }

    private static async Task WriteAsync(HttpListenerContext ctx, int status, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = contentType;
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    /// <summary>
    /// Shows the "allow widget?" prompt – one at a time, with a short cooldown after a denial so a
    /// misbehaving page cannot flood the user with dialogs.
    /// </summary>
    public async Task<bool> AskPairingAsync(string origin, CancellationToken ct)
    {
        await _promptLock.WaitAsync(ct);
        try
        {
            if (DateTime.UtcNow - _lastDeny < DenyCooldown) return false;
            var allowed = await Prompt.AskAsync(origin, ct);
            if (ct.IsCancellationRequested)
            {
                // The widget went away (e.g. reloaded) while asking – not a denial, so no cooldown.
                Log.Info("Kopplungsfrage abgebrochen (Widget getrennt).");
                return false;
            }
            if (!allowed) _lastDeny = DateTime.UtcNow;
            Log.Info(allowed ? "Kopplung erlaubt." : "Kopplung abgelehnt.");
            return allowed;
        }
        finally
        {
            _promptLock.Release();
        }
    }

    private void Broadcast(JsonObject message)
    {
        var text = message.ToJsonString();
        foreach (var c in _connections.Values)
            if (c.IsPaired) c.Send(text);
    }

    internal static bool IsLocalRequest(IPEndPoint? remote) => remote != null && IPAddress.IsLoopback(remote.Address);

    private static string CleanOrigin(string? origin)
    {
        if (origin == OriginPolicy.IcueOrigin) return Strings.IcueOrigin;
        if (string.IsNullOrWhiteSpace(origin)) return Strings.UnknownOrigin;
        var clean = new string(origin.Where(ch => !char.IsControl(ch)).Take(120).ToArray());
        return clean.Length == 0 ? Strings.UnknownOrigin : clean;
    }
}
