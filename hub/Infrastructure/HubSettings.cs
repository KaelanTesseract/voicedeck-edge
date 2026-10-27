using System.Text.Json;

namespace VoicedeckEdge.Hub.Infrastructure;

/// <summary>Non-secret settings, stored as plain JSON. Contains no personal data.</summary>
internal sealed class HubSettings
{
    public string? ClientId { get; set; }
    public bool UpdateCheck { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static HubSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<HubSettings>(File.ReadAllText(path)) ?? new HubSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Log.Warn($"settings.json nicht lesbar, verwende Standardwerte: {ex.Message}");
        }
        return new HubSettings();
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));

    /// <summary>Discord client IDs are snowflakes: digits only.</summary>
    public static bool IsValidClientId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length is >= 15 and <= 21 && value.All(char.IsAsciiDigit);
}
