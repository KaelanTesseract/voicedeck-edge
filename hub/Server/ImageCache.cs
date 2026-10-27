using System.Text.RegularExpressions;
using VoicedeckEdge.Hub.Infrastructure;

namespace VoicedeckEdge.Hub.Server;

internal sealed record CachedImage(byte[] Data, string ContentType);

/// <summary>
/// Images from Discord, cached in memory only (max 50 MB, LRU). Accepted hub paths:
/// /img/avatars/{id}/{hash}, /img/icons/{id}/{hash}, /img/emojis/[a/]{id} and /img/m/{token}
/// (chat images registered in <see cref="MediaRegistry"/>). No arbitrary URLs are fetched.
/// </summary>
internal sealed partial class ImageCache(HttpClient http, MediaRegistry media)
{
    private const long MaxBytes = 50L * 1024 * 1024;
    private const long MaxImageBytes = 6L * 1024 * 1024;
    private static readonly TimeSpan NegativeCacheTime = TimeSpan.FromMinutes(5);

    private readonly object _gate = new();
    private readonly LinkedList<(string Key, CachedImage Image)> _lru = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, CachedImage Image)>> _index = [];
    private readonly Dictionary<string, DateTime> _failed = [];
    private long _bytes;

    [GeneratedRegex(@"^/img/(avatars|icons)/(\d{15,21})/((?:a_)?[0-9a-f]{32})$")]
    private static partial Regex ProfilePath();

    [GeneratedRegex(@"^/img/emojis/(a/)?(\d{15,21})$")]
    private static partial Regex EmojiPath();

    [GeneratedRegex(@"^/img/m/([0-9a-f]{32})$")]
    private static partial Regex MediaPath();

    /// <summary>Maps a hub path to the Discord URL it stands for, or null if the path is not allowed.</summary>
    internal string? Resolve(string path)
    {
        if (ProfilePath().Match(path) is { Success: true } p)
            return $"https://cdn.discordapp.com/{p.Groups[1].Value}/{p.Groups[2].Value}/{p.Groups[3].Value}.png?size=128";
        if (EmojiPath().Match(path) is { Success: true } e)
            return $"https://cdn.discordapp.com/emojis/{e.Groups[2].Value}.{(e.Groups[1].Success ? "gif" : "webp")}?size=64&quality=lossless";
        if (MediaPath().Match(path) is { Success: true } m)
            return media.Lookup(m.Groups[1].Value);
        return null;
    }

    /// <summary>Returns the image for a hub path, or null if invalid/unavailable.</summary>
    public async Task<CachedImage?> GetAsync(string path, CancellationToken ct)
    {
        var url = Resolve(path);
        if (url == null) return null;
        var key = path;

        lock (_gate)
        {
            if (_index.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value.Image;
            }
            if (_failed.TryGetValue(key, out var failedAt) && DateTime.UtcNow - failedAt < NegativeCacheTime) return null;
        }

        CachedImage image;
        try
        {
            using var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            var type = res.Content.Headers.ContentType?.MediaType ?? "";
            if (!res.IsSuccessStatusCode || !type.StartsWith("image/", StringComparison.Ordinal)
                || res.Content.Headers.ContentLength > MaxImageBytes)
            {
                lock (_gate) _failed[key] = DateTime.UtcNow;
                return null;
            }
            var data = await res.Content.ReadAsByteArrayAsync(ct);
            if (data.Length > MaxImageBytes)
            {
                lock (_gate) _failed[key] = DateTime.UtcNow;
                return null;
            }
            image = new CachedImage(data, type);
        }
        catch (HttpRequestException ex)
        {
            Log.Warn($"Bild nicht ladbar: {ex.Message}");
            lock (_gate) _failed[key] = DateTime.UtcNow;
            return null;
        }

        lock (_gate)
        {
            if (!_index.ContainsKey(key))
            {
                _index[key] = _lru.AddFirst((key, image));
                _bytes += image.Data.Length;
                while (_bytes > MaxBytes && _lru.Last is { } last)
                {
                    _lru.RemoveLast();
                    _index.Remove(last.Value.Key);
                    _bytes -= last.Value.Image.Data.Length;
                }
            }
        }
        return image;
    }

    internal static bool IsValidPath(string path) =>
        ProfilePath().IsMatch(path) || EmojiPath().IsMatch(path) || MediaPath().IsMatch(path);

    public void Clear()
    {
        lock (_gate)
        {
            _lru.Clear();
            _index.Clear();
            _failed.Clear();
            _bytes = 0;
        }
    }
}
