// Voicedeck Edge – Spike: checks what the Discord local RPC really offers.
// Privacy: the report only contains the *shape* of responses (field names, counts,
// error codes), never names, IDs, messages or tokens. The token is revoked at the end.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VoicedeckEdge.Spike;

var clientId = GetArg("--client-id") ?? Environment.GetEnvironmentVariable("VOICEDECK_SPIKE_CLIENT_ID");
var redirectUri = GetArg("--redirect") ?? "http://localhost";
var reportPath = Path.GetFullPath(GetArg("--report") ?? "spike-report.json");

if (string.IsNullOrWhiteSpace(clientId) || !clientId.All(char.IsDigit))
{
    Console.WriteLine("Aufruf: DiscordRpcSpike --client-id <Client-ID deiner Discord-App>");
    return 2;
}

var report = new JsonObject
{
    ["spikeVersion"] = 1,
    ["timestampUtc"] = DateTime.UtcNow.ToString("O"),
    ["redirectUri"] = redirectUri,
};
var steps = new JsonObject();
report["steps"] = steps;

var dmMode = args.Contains("--dm");
var scopes = new JsonArray("rpc", "rpc.voice.read", "rpc.voice.write", "messages.read");
if (dmMode) scopes.Add("rpc.notifications.read");
report["mode"] = dmMode ? "dm" : "default";
var http = new HttpClient();
string? accessToken = null;
string? usedSecret = null;

try
{
    Say("Verbinde mit Discord …");
    await using var ipc = await DiscordIpc.ConnectAsync();
    if (ipc == null)
    {
        steps["connect"] = new JsonObject { ["ok"] = false, ["error"] = "Kein discord-ipc-Pipe gefunden (Discord nicht geöffnet?)" };
        Say("Discord ist nicht geöffnet. Bitte Discord starten und erneut versuchen.");
        return 1;
    }
    steps["connect"] = new JsonObject { ["ok"] = true, ["pipe"] = ipc.PipeIndex };

    var ready = await ipc.HandshakeAsync(clientId, TimeSpan.FromSeconds(10));
    steps["ready"] = new JsonObject
    {
        ["ok"] = true,
        ["rpcVersion"] = ready["data"]?["v"]?.DeepClone(),
        ["dataShape"] = Shape(ready["data"]),
    };
    Say("Verbunden. Fordere Autorisierung an – bitte in Discord auf \"Autorisieren\" klicken.");

    // 1) AUTHORIZE with PKCE
    var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
    var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    var authArgs = new JsonObject
    {
        ["client_id"] = clientId,
        ["scopes"] = scopes.DeepClone(),
        ["code_challenge"] = challenge,
        ["code_challenge_method"] = "S256",
    };
    var auth = await ipc.CommandAsync("AUTHORIZE", authArgs, TimeSpan.FromMinutes(3));
    var pkceAccepted = !IsError(auth);
    steps["authorizePkce"] = Result(auth);

    if (!pkceAccepted)
    {
        Say("AUTHORIZE mit PKCE abgelehnt – versuche ohne PKCE. Bitte erneut in Discord autorisieren.");
        authArgs.Remove("code_challenge");
        authArgs.Remove("code_challenge_method");
        auth = await ipc.CommandAsync("AUTHORIZE", authArgs, TimeSpan.FromMinutes(3));
        steps["authorizePlain"] = Result(auth);
        if (IsError(auth)) return Finish(1, "Autorisierung fehlgeschlagen.");
    }
    var code = auth["data"]?["code"]?.GetValue<string>();
    if (code == null) return Finish(1, "Discord hat keinen Code geliefert.");

    // 2) Token exchange – first without secret
    var (tokenOk, tokenJson, status) = await ExchangeAsync(code, pkceAccepted ? verifier : null, secret: null);
    steps["tokenWithoutSecret"] = TokenResult(tokenOk, tokenJson, status);

    if (!tokenOk)
    {
        usedSecret = ReadSecret();
        if (usedSecret == null) return Finish(1, "Ohne Secret kein Token – Test endet hier (Ergebnis steht im Bericht).");
        // A code is single-use: authorize again for the second attempt.
        Say("Bitte noch einmal in Discord autorisieren (zweiter Versuch mit Secret).");
        auth = await ipc.CommandAsync("AUTHORIZE", authArgs, TimeSpan.FromMinutes(3));
        steps["authorizeForSecret"] = Result(auth);
        code = auth["data"]?["code"]?.GetValue<string>();
        if (code == null) return Finish(1, "Zweite Autorisierung fehlgeschlagen.");
        (tokenOk, tokenJson, status) = await ExchangeAsync(code, pkceAccepted ? verifier : null, usedSecret);
        steps["tokenWithSecret"] = TokenResult(tokenOk, tokenJson, status);
        if (!tokenOk) return Finish(1, "Token-Tausch auch mit Secret fehlgeschlagen.");
    }
    accessToken = tokenJson!["access_token"]!.GetValue<string>();

    // 3) AUTHENTICATE
    var authn = await ipc.CommandAsync("AUTHENTICATE", new JsonObject { ["access_token"] = accessToken }, TimeSpan.FromSeconds(15));
    steps["authenticate"] = Result(authn);
    if (IsError(authn)) return Finish(1, "AUTHENTICATE fehlgeschlagen.");
    report["grantedScopes"] = authn["data"]?["scopes"]?.DeepClone();
    Say("Angemeldet. Lese Struktur der Daten …");

    if (dmMode)
    {
        // Direct messages: RPC has no "list DMs" command. Probe whether notifications reveal DM channels
        // and whether GET_CHANNEL / SUBSCRIBE work for them.
        var noGuild = await ipc.CommandAsync("GET_CHANNELS", new JsonObject(), TimeSpan.FromSeconds(10));
        steps["getChannelsWithoutGuild"] = Result(noGuild, includeShape: true);
        var privateList = noGuild["data"]?["channels"] as JsonArray ?? new JsonArray();
        steps["getChannelsWithoutGuild_typeCounts"] = CountBy(privateList, "type");
        if (args.Contains("--list-only")) return Finish(0, "DM-Liste geprüft.");

        var notification = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        ipc.EventReceived += msg =>
        {
            if (msg["evt"]?.GetValue<string>() == "NOTIFICATION_CREATE" && msg["data"] is JsonObject d) notification.TrySetResult(d);
        };
        var sub = await ipc.CommandAsync("SUBSCRIBE", null, TimeSpan.FromSeconds(10), "NOTIFICATION_CREATE");
        steps["subscribe_NOTIFICATION_CREATE"] = Result(sub);
        if (IsError(sub)) return Finish(1, "NOTIFICATION_CREATE nicht erlaubt.");

        Say("Warte bis zu 4 Minuten auf eine eingehende Direktnachricht … (Discord darf dabei NICHT diesen Chat geöffnet haben)");
        JsonObject data;
        try
        {
            data = await notification.Task.WaitAsync(TimeSpan.FromMinutes(4));
        }
        catch (TimeoutException)
        {
            return Finish(1, "Keine Benachrichtigung erhalten.");
        }
        steps["notificationShape"] = Shape(data);
        var dmChannelId = data["channel_id"]?.GetValue<string>() ?? data["message"]?["channel_id"]?.GetValue<string>();
        steps["notificationHasChannelId"] = dmChannelId != null;
        if (dmChannelId == null) return Finish(1, "Benachrichtigung ohne Kanal-ID.");

        var dm = await ipc.CommandAsync("GET_CHANNEL", new JsonObject { ["channel_id"] = dmChannelId }, TimeSpan.FromSeconds(10));
        steps["getChannel_dm"] = Result(dm, includeShape: true);
        var subDm = await ipc.CommandAsync("SUBSCRIBE", new JsonObject { ["channel_id"] = dmChannelId }, TimeSpan.FromSeconds(10), "MESSAGE_CREATE");
        steps["subscribe_MESSAGE_CREATE_dm"] = Result(subDm);
        return Finish(0, "DM-Test abgeschlossen.");
    }

    // 4) Read-only probes
    var selected = await ipc.CommandAsync("GET_SELECTED_VOICE_CHANNEL", null, TimeSpan.FromSeconds(10));
    steps["getSelectedVoiceChannel"] = Result(selected, includeShape: true);
    var selectedGuildId = selected["data"]?["guild_id"]?.GetValue<string>();

    var voiceSettings = await ipc.CommandAsync("GET_VOICE_SETTINGS", null, TimeSpan.FromSeconds(10));
    steps["getVoiceSettings"] = Result(voiceSettings, includeShape: true);

    var guilds = await ipc.CommandAsync("GET_GUILDS", null, TimeSpan.FromSeconds(10));
    steps["getGuilds"] = Result(guilds, includeShape: true);

    var guildId = selectedGuildId ?? guilds["data"]?["guilds"]?.AsArray().FirstOrDefault()?["id"]?.GetValue<string>();
    if (guildId != null)
    {
        var guild = await ipc.CommandAsync("GET_GUILD", new JsonObject { ["guild_id"] = guildId }, TimeSpan.FromSeconds(10));
        steps["getGuild"] = Result(guild, includeShape: true);

        var channels = await ipc.CommandAsync("GET_CHANNELS", new JsonObject { ["guild_id"] = guildId }, TimeSpan.FromSeconds(10));
        steps["getChannels"] = Result(channels, includeShape: true);

        var list = channels["data"]?["channels"]?.AsArray() ?? new JsonArray();
        steps["channelTypeCounts"] = CountBy(list, "type");

        // GET_CHANNEL on one text (0) and one voice (2) channel: does it carry position / parent_id / messages?
        foreach (var (label, type) in new[] { ("text", 0), ("voice", 2) })
        {
            var ch = list.FirstOrDefault(c => c?["type"]?.GetValue<int>() == type);
            if (ch == null) continue;
            var detail = await ipc.CommandAsync("GET_CHANNEL", new JsonObject { ["channel_id"] = ch["id"]!.GetValue<string>() }, TimeSpan.FromSeconds(10));
            steps[$"getChannel_{label}"] = Result(detail, includeShape: true);

            var subEvt = label == "text" ? "MESSAGE_CREATE" : "SPEAKING_START";
            var sub = await ipc.CommandAsync("SUBSCRIBE", new JsonObject { ["channel_id"] = ch["id"]!.GetValue<string>() }, TimeSpan.FromSeconds(10), subEvt);
            steps[$"subscribe_{subEvt}"] = Result(sub);
        }
    }

    foreach (var evt in new[] { "VOICE_SETTINGS_UPDATE", "VOICE_CHANNEL_SELECT", "VOICE_CONNECTION_STATUS" })
    {
        var sub = await ipc.CommandAsync("SUBSCRIBE", null, TimeSpan.FromSeconds(10), evt);
        steps[$"subscribe_{evt}"] = Result(sub);
    }

    return Finish(0, "Test abgeschlossen.");
}
catch (TimeoutException)
{
    return Finish(1, "Zeitüberschreitung – wurde in Discord rechtzeitig autorisiert?");
}
catch (Exception ex)
{
    steps["exception"] = $"{ex.GetType().Name}: {ex.Message}";
    return Finish(1, "Unerwarteter Fehler.");
}

// ---------- helpers ----------

int Finish(int exitCode, string message)
{
    if (accessToken != null)
    {
        steps["revoke"] = RevokeAsync(accessToken).GetAwaiter().GetResult();
    }
    report["exitCode"] = exitCode;
    File.WriteAllText(reportPath, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    Say(message);
    Say($"Bericht: {reportPath}");
    return exitCode;
}

async Task<(bool ok, JsonObject? json, int status)> ExchangeAsync(string code, string? verifier, string? secret)
{
    var form = new Dictionary<string, string>
    {
        ["grant_type"] = "authorization_code",
        ["code"] = code,
        ["redirect_uri"] = redirectUri,
        ["client_id"] = clientId!,
    };
    if (verifier != null) form["code_verifier"] = verifier;
    if (secret != null) form["client_secret"] = secret;
    using var res = await http.PostAsync("https://discord.com/api/oauth2/token", new FormUrlEncodedContent(form));
    var text = await res.Content.ReadAsStringAsync();
    JsonObject? json = null;
    try { json = JsonNode.Parse(text) as JsonObject; } catch (JsonException) { }
    return (res.IsSuccessStatusCode && json?["access_token"] != null, json, (int)res.StatusCode);
}

async Task<JsonObject> RevokeAsync(string token)
{
    var form = new Dictionary<string, string>
    {
        ["token"] = token,
        ["token_type_hint"] = "access_token",
        ["client_id"] = clientId!,
    };
    if (usedSecret != null) form["client_secret"] = usedSecret;
    try
    {
        using var res = await http.PostAsync("https://discord.com/api/oauth2/token/revoke", new FormUrlEncodedContent(form));
        return new JsonObject { ["ok"] = res.IsSuccessStatusCode, ["status"] = (int)res.StatusCode };
    }
    catch (HttpRequestException ex)
    {
        return new JsonObject { ["ok"] = false, ["error"] = ex.Message };
    }
}

static JsonObject TokenResult(bool ok, JsonObject? json, int status) => new()
{
    ["ok"] = ok,
    ["status"] = status,
    // only error fields and the granted scope string – never the tokens themselves
    ["error"] = json?["error"]?.DeepClone(),
    ["errorDescription"] = json?["error_description"]?.DeepClone(),
    ["scope"] = json?["scope"]?.DeepClone(),
    ["responseKeys"] = json == null ? null : new JsonArray(json.Select(p => (JsonNode?)p.Key).ToArray()),
};

async Task<JsonObject> TryAsync(Func<Task<JsonObject>> call)
{
    try { return Result(await call()); }
    catch (Exception ex) { return new JsonObject { ["ok"] = false, ["exception"] = ex.GetType().Name }; }
}

static bool IsError(JsonObject msg) => msg["evt"]?.GetValue<string>() == "ERROR";

static JsonObject Result(JsonObject msg, bool includeShape = false)
{
    var r = new JsonObject { ["ok"] = !IsError(msg) };
    if (IsError(msg))
    {
        r["code"] = msg["data"]?["code"]?.DeepClone();
        r["message"] = msg["data"]?["message"]?.DeepClone();
    }
    else if (includeShape)
    {
        r["shape"] = Shape(msg["data"]);
    }
    return r;
}

// Describes structure only: object -> {key: shape}, array -> {count, item: merged shape}, value -> type name.
static JsonNode? Shape(JsonNode? node) => node switch
{
    null => "null",
    JsonObject o => new JsonObject(o.Select(p => KeyValuePair.Create(p.Key, Shape(p.Value)))),
    JsonArray a => new JsonObject
    {
        ["count"] = a.Count,
        ["item"] = a.Count == 0 ? null : MergeShapes(a.Select(Shape)),
    },
    JsonValue v => v.GetValueKind().ToString().ToLowerInvariant(),
    _ => "unknown",
};

static JsonNode? MergeShapes(IEnumerable<JsonNode?> shapes)
{
    JsonObject? merged = null;
    JsonNode? last = null;
    foreach (var s in shapes)
    {
        if (s is JsonObject o)
        {
            merged ??= new JsonObject();
            foreach (var p in o)
                if (!merged.ContainsKey(p.Key)) merged[p.Key] = p.Value?.DeepClone();
        }
        else last = s?.DeepClone();
    }
    return merged ?? last;
}

static JsonObject CountBy(JsonArray list, string key)
{
    var counts = new JsonObject();
    foreach (var g in list.GroupBy(c => c?[key]?.ToJsonString() ?? "null"))
        counts[g.Key] = g.Count();
    return counts;
}

static string Base64Url(byte[] bytes) =>
    Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

static string? ReadSecret()
{
    var env = Environment.GetEnvironmentVariable("VOICEDECK_SPIKE_SECRET");
    if (!string.IsNullOrEmpty(env)) return env;
    if (Console.IsInputRedirected) return null;

    Console.WriteLine();
    Console.WriteLine("Discord verlangt das Client-Secret. Bitte hier einfügen (wird nicht angezeigt, nicht gespeichert),");
    Console.Write("oder nur Enter drücken zum Überspringen: ");
    var sb = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
        if (!char.IsControl(key.KeyChar)) sb.Append(key.KeyChar);
    }
    Console.WriteLine();
    return sb.Length == 0 ? null : sb.ToString();
}

static void Say(string text) => Console.WriteLine($"[Voicedeck-Test] {text}");

string? GetArg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
