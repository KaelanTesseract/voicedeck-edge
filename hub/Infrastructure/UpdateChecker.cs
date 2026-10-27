using System.Text.Json.Nodes;

namespace VoicedeckEdge.Hub.Infrastructure;

internal sealed record UpdateInfo(string Version, string Url);

/// <summary>
/// Opt-in only: asks the GitHub releases API for the latest version number. Sends nothing
/// but a plain HTTPS request (no IDs, no usage data).
/// </summary>
internal sealed class UpdateChecker(HttpClient http, string currentVersion)
{
    public const string Repository = "KaelanTesseract/voicedeck-edge";
    private const string LatestUrl = $"https://api.github.com/repos/{Repository}/releases/latest";

    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return null; // no release yet, rate limit, …
            var json = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
            var tag = json?["tag_name"]?.GetValue<string>();
            var url = json?["html_url"]?.GetValue<string>();
            return tag != null && url != null && IsNewer(tag, currentVersion) && url.StartsWith("https://github.com/", StringComparison.Ordinal)
                ? new UpdateInfo(tag.TrimStart('v', 'V'), url)
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            Log.Warn($"Update-Suche fehlgeschlagen: {ex.GetType().Name}");
            return null;
        }
    }

    /// <summary>Compares "v1.2.3" style tags; anything unparsable is never "newer".</summary>
    internal static bool IsNewer(string tag, string current) =>
        Version.TryParse(tag.TrimStart('v', 'V'), out var latest)
        && Version.TryParse(current, out var mine)
        && latest > mine;
}
