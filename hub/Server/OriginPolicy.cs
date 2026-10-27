namespace VoicedeckEdge.Hub.Server;

/// <summary>
/// Which WebSocket origins may even reach the pairing prompt.
/// iCUE loads widgets from local files, so the browser engine sends <c>Origin: null</c> (verified
/// 2026-10-27 with iCUE 5.52). Real web pages (http/https) and browser extensions are refused –
/// otherwise any website open in a browser could talk to localhost:47820.
/// </summary>
internal static class OriginPolicy
{
    public const string IcueOrigin = "null";

    public static bool IsAllowed(string? origin, bool allowLocalDevPages)
    {
        // No Origin header: not a browser (e.g. a local tool). A local program can do more than this anyway.
        if (string.IsNullOrEmpty(origin)) return true;
        if (origin == IcueOrigin) return true;
        if (allowLocalDevPages && Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttp && uri.Host is "localhost" or "127.0.0.1")
            return true;
        return false;
    }
}
