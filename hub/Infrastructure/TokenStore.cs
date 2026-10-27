using System.Security.Cryptography;
using System.Text.Json;

namespace VoicedeckEdge.Hub.Infrastructure;

internal sealed record DiscordTokens(
    string ClientId,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt);

/// <summary>Discord tokens, encrypted with Windows DPAPI for the current user only.</summary>
internal sealed class TokenStore(string path)
{
    // Extra entropy so other apps using plain DPAPI on the same account cannot read the blob by accident.
    private static readonly byte[] Entropy = "VoicedeckEdge.Tokens.v1"u8.ToArray();

    public DiscordTokens? Load()
    {
        try
        {
            if (!File.Exists(path)) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<DiscordTokens>(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            Log.Warn($"Gespeicherte Anmeldung nicht lesbar, wird verworfen ({ex.GetType().Name}).");
            Clear();
            return null;
        }
    }

    public void Save(DiscordTokens tokens)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(tokens);
        File.WriteAllBytes(path, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
    }

    public void Clear()
    {
        try { File.Delete(path); } catch (IOException) { }
    }
}
