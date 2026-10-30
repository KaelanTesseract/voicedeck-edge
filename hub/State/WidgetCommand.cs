using System.Text.Json.Nodes;
using VoicedeckEdge.Hub.Discord;

namespace VoicedeckEdge.Hub.State;

/// <summary>An operation the widget may request. Anything not listed here is rejected.</summary>
internal sealed record WidgetCommand(
    string Op,
    string? ChannelId = null,
    string? GuildId = null,
    string? UserId = null,
    int? Volume = null,
    bool? Mute = null,
    string? Mode = null,
    bool? Next = null,
    string? DeviceId = null)
{
    public static readonly IReadOnlySet<string> KnownOps = new HashSet<string>
    {
        "voice.toggleMute", "voice.toggleDeaf", "voice.setMode", "voice.leave", "voice.join",
        "user.setVolume", "user.setMute", "user.reset",
        "guilds.list", "channels.list", "chat.open", "chat.close",
        "edge.page",
        "audio.setInput", "audio.setOutput", "audio.setInputVolume", "audio.setOutputVolume",
    };

    /// <summary>Validates a widget message strictly; throws <see cref="OpException"/> on anything unexpected.</summary>
    public static WidgetCommand Parse(JsonObject msg)
    {
        var op = msg.Str("op");
        if (op == null || !KnownOps.Contains(op)) throw new OpException("unknown_op");

        return op switch
        {
            "voice.setMode" => new WidgetCommand(op, Mode: msg.Str("mode") is "VOICE_ACTIVITY" or "PUSH_TO_TALK"
                ? msg.Str("mode") : throw new OpException("invalid_args")),
            "voice.join" or "chat.open" => new WidgetCommand(op, ChannelId: Snowflake(msg, "channelId")),
            "channels.list" => new WidgetCommand(op, GuildId: Snowflake(msg, "guildId")),
            "user.setVolume" => new WidgetCommand(op, UserId: Snowflake(msg, "userId"), Volume: ParseVolume(msg)),
            "user.setMute" => new WidgetCommand(op, UserId: Snowflake(msg, "userId"), Mute: Flag(msg, "mute")),
            "user.reset" => new WidgetCommand(op, UserId: Snowflake(msg, "userId")),
            "audio.setInput" or "audio.setOutput" => new WidgetCommand(op, DeviceId: DeviceIdOf(msg)),
            "audio.setInputVolume" => new WidgetCommand(op, Volume: Math.Min(ParseVolume(msg), 100)),
            "audio.setOutputVolume" => new WidgetCommand(op, Volume: ParseVolume(msg)),
            "edge.page" => new WidgetCommand(op, Next: msg.Str("dir") switch
            {
                "next" => true,
                "prev" => false,
                _ => throw new OpException("invalid_args"),
            }),
            _ => new WidgetCommand(op),
        };
    }

    private static string Snowflake(JsonObject msg, string key) =>
        msg.Str(key) is { } v && Json.IsSnowflake(v) ? v : throw new OpException("invalid_args");

    /// <summary>Discord device ids are opaque strings; only their shape is checked here, membership in the controller.</summary>
    private static string DeviceIdOf(JsonObject msg) =>
        msg.Str("deviceId") is { Length: > 0 and <= 512 } id && !id.Any(char.IsControl) ? id : throw new OpException("invalid_args");

    private static int ParseVolume(JsonObject msg)
    {
        if (msg["volume"] is not JsonValue v || !v.TryGetValue<double>(out var d) || double.IsNaN(d))
            throw new OpException("invalid_args");
        return (int)Math.Clamp(Math.Round(d), 0, 200);
    }

    private static bool Flag(JsonObject msg, string key) =>
        msg[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : throw new OpException("invalid_args");
}

/// <summary>Error sent back to the widget as { ok: false, error: Code }.</summary>
internal sealed class OpException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
