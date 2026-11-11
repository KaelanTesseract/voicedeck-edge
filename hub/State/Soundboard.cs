using System.Text.Json.Nodes;
using VoicedeckEdge.Hub.Discord;

namespace VoicedeckEdge.Hub.State;

/// <summary>A soundboard sound as the widget shows it. Built-in sounds have guild "0".</summary>
internal sealed record SoundboardSound(
    string Id, string GuildId, string? GuildName, string Name, string? Emoji, string? EmojiImage, bool Available);

/// <summary>Maps GET_SOUNDBOARD_SOUNDS (verified 2026-11-11: name, volume, available, sound_id, guild_id, emoji_id, emoji_name, user_id).</summary>
internal static class Soundboard
{
    public const int MaxSounds = 200;
    public const string BuiltInGuild = "0";

    /// <summary>Server sounds have snowflake ids, Discord's built-in ones short numbers ("1", "2", …).</summary>
    public static bool IsSoundId(string? id) => id is { Length: >= 1 and <= 21 } && id.All(char.IsAsciiDigit);

    public static List<SoundboardSound> Map(JsonNode? data, Func<string, string?> guildName)
    {
        var list = data as JsonArray ?? data?["sounds"] as JsonArray ?? [];
        return list.OfType<JsonObject>()
            .Select(s =>
            {
                var id = s.Str("sound_id");
                var guild = s.Str("guild_id");
                if (!IsSoundId(id) || guild == null || guild != BuiltInGuild && !Json.IsSnowflake(guild)) return null;
                var emojiId = s.Str("emoji_id");
                return new SoundboardSound(
                    id!, guild,
                    guild == BuiltInGuild ? null : guildName(guild),
                    (s.Str("name") ?? "").Trim() is { Length: > 0 } n ? (n.Length > 40 ? n[..40] : n) : "?",
                    emojiId == null && s.Str("emoji_name") is { Length: > 0 and <= 32 } e ? e : null,
                    emojiId != null && Json.IsSnowflake(emojiId) ? $"/img/emojis/{emojiId}" : null,
                    s.Bool("available", true));
            })
            .OfType<SoundboardSound>()
            .Take(MaxSounds)
            .ToList();
    }
}
