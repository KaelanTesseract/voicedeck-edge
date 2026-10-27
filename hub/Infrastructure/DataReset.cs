using VoicedeckEdge.Hub.Discord;
using VoicedeckEdge.Hub.Server;

namespace VoicedeckEdge.Hub.Infrastructure;

internal sealed record ResetResult(bool HadSignIn, bool Revoked);

/// <summary>
/// "Sign out and delete all data": revokes the Discord grant, unpairs every widget and deletes
/// every file the hub ever wrote (settings, tokens, pairings, logs). Also used by the uninstaller.
/// </summary>
internal static class DataReset
{
    public static async Task<ResetResult> RunAsync(
        DiscordOAuth oauth, TokenStore tokenStore, PairingStore? pairings, ImageCache? images,
        string dataDir, string logDir, CancellationToken ct)
    {
        Log.Info("Alle Daten werden gelöscht.");
        var tokens = tokenStore.Load();
        var revoked = false;
        if (tokens != null)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await oauth.RevokeAsync(tokens, timeout.Token);
                revoked = true;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                // Offline: delete locally anyway, the user is told how to revoke in Discord.
            }
        }

        tokenStore.Clear();
        pairings?.RevokeAll();
        images?.Clear();
        DeleteFiles(dataDir, "*");
        DeleteFiles(logDir, "hub-*.log");
        return new ResetResult(tokens != null, revoked);
    }

    private static void DeleteFiles(string dir, string pattern)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir, pattern))
        {
            try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
