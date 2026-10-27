using System.Text.Json;
using System.Text.Json.Nodes;

namespace VoicedeckEdge.Hub.Discord;

/// <summary>Tolerant accessors: Discord payloads vary, a wrong type must never crash the hub.</summary>
internal static class Json
{
    public static string? Str(this JsonNode? node, string key) =>
        node?[key] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    public static bool Bool(this JsonNode? node, string key, bool fallback = false) =>
        node?[key] is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? v.GetValue<bool>() : fallback;

    public static int Int(this JsonNode? node, string key, int fallback = 0) =>
        node?[key] is JsonValue v && v.GetValueKind() == JsonValueKind.Number
        && double.TryParse(v.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? (int)Math.Round(d)
            : fallback;

    public static JsonArray Arr(this JsonNode? node, string key) => node?[key] as JsonArray ?? [];

    /// <summary>Discord snowflake: 15–21 ASCII digits.</summary>
    public static bool IsSnowflake(string? value) =>
        value is { Length: >= 15 and <= 21 } && value.All(char.IsAsciiDigit);
}
