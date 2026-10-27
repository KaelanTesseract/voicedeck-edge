using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace VoicedeckEdge.Hub.Server;

/// <summary>
/// Chat images (attachments, embedded pictures) have long, signed Discord URLs. Instead of letting the
/// widget ask for arbitrary URLs, the hub registers each image it saw in a message and hands out an
/// opaque token. Only Discord's own image hosts are accepted. Lives in memory only.
/// </summary>
internal sealed class MediaRegistry
{
    private const int MaxEntries = 2000;
    private const int MaxPreviewWidth = 480;

    private readonly ConcurrentDictionary<string, string> _urls = new();

    /// <summary>Registers an image URL and returns the hub path for it, or null if the URL is not allowed.</summary>
    public string? Register(string? url, int width = 0, int height = 0)
    {
        if (!IsAllowed(url)) return null;
        var sized = Resize(url!, width, height);
        var token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sized)))[..32].ToLowerInvariant();
        if (_urls.Count >= MaxEntries) _urls.Clear(); // only the open chat needs its images
        _urls[token] = sized;
        return $"/img/m/{token}";
    }

    public string? Lookup(string token) => _urls.TryGetValue(token, out var url) ? url : null;

    public void Clear() => _urls.Clear();

    /// <summary>HTTPS on Discord's CDN / media proxy only.</summary>
    internal static bool IsAllowed(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && (uri.Host is "cdn.discordapp.com" or "media.discordapp.net"
            || (uri.Host.EndsWith(".discordapp.net", StringComparison.Ordinal) && uri.Host.StartsWith("images-ext-", StringComparison.Ordinal)));

    /// <summary>The media proxies can scale images down – the widget never needs more than a preview.</summary>
    internal static string Resize(string url, int width, int height)
    {
        var uri = new Uri(url);
        if (uri.Host == "cdn.discordapp.com" || width <= MaxPreviewWidth || width <= 0 || height <= 0) return url;
        var h = (int)Math.Round(height * (double)MaxPreviewWidth / width);
        return url + (url.Contains('?') ? "&" : "?") + $"width={MaxPreviewWidth}&height={h}";
    }
}
