using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using VoicedeckEdge.Hub.Infrastructure;

namespace VoicedeckEdge.Hub.Discord;

/// <summary>
/// OAuth2 against discord.com for a *public client* using PKCE – no client secret involved
/// (verified in the spike, 2026-10-27).
/// </summary>
internal sealed class DiscordOAuth(HttpClient http)
{
    public const string RedirectUri = "http://localhost";
    // rpc.notifications.read: incoming direct messages arrive as notifications - the only way to learn
    // about DM conversations over RPC (verified 2026-10-27; GET_CHANNELS never lists DMs).
    public static readonly string[] Scopes = ["rpc", "rpc.voice.read", "rpc.voice.write", "messages.read", "rpc.notifications.read"];

    /// <summary>True when the AUTHENTICATE answer grants every scope the hub needs (older sign-ins lack some).</summary>
    public static bool HasRequiredScopes(JsonNode? authenticateData)
    {
        var granted = authenticateData?["scopes"] as JsonArray;
        if (granted == null) return false;
        var set = granted.Select(s => s?.ToString()).ToHashSet();
        return Scopes.All(set.Contains);
    }

    private const string TokenUrl = "https://discord.com/api/oauth2/token";
    private const string RevokeUrl = "https://discord.com/api/oauth2/token/revoke";

    public sealed record Pkce(string Verifier, string Challenge);

    public static Pkce CreatePkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return new Pkce(verifier, challenge);
    }

    public Task<DiscordTokens> ExchangeCodeAsync(string clientId, string code, string verifier, CancellationToken ct) =>
        RequestTokenAsync(clientId, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = verifier,
        }, ct);

    public Task<DiscordTokens> RefreshAsync(DiscordTokens tokens, CancellationToken ct) =>
        RequestTokenAsync(tokens.ClientId, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = tokens.RefreshToken,
            ["client_id"] = tokens.ClientId,
        }, ct);

    public async Task RevokeAsync(DiscordTokens tokens, CancellationToken ct)
    {
        foreach (var (token, hint) in new[] { (tokens.RefreshToken, "refresh_token"), (tokens.AccessToken, "access_token") })
        {
            using var res = await http.PostAsync(RevokeUrl, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = token,
                ["token_type_hint"] = hint,
                ["client_id"] = tokens.ClientId,
            }), ct);
            Log.Info($"Token-Widerruf ({hint}): HTTP {(int)res.StatusCode}");
        }
    }

    private async Task<DiscordTokens> RequestTokenAsync(string clientId, Dictionary<string, string> form, CancellationToken ct)
    {
        using var res = await http.PostAsync(TokenUrl, new FormUrlEncodedContent(form), ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        var json = JsonNode.Parse(text) as JsonObject;
        var access = json?["access_token"]?.GetValue<string>();
        var refresh = json?["refresh_token"]?.GetValue<string>();
        if (!res.IsSuccessStatusCode || access == null || refresh == null)
        {
            // only Discord's error code, never the response body with possible tokens
            throw new OAuthException((int)res.StatusCode, json?["error"]?.GetValue<string>() ?? "unknown");
        }
        var expiresIn = json!["expires_in"]?.GetValue<int>() ?? 3600;
        return new DiscordTokens(clientId, access, refresh, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal sealed class OAuthException(int status, string error)
    : Exception($"OAuth fehlgeschlagen: HTTP {status}, {error}")
{
    public int Status { get; } = status;
    public string Error { get; } = error;

    /// <summary>The stored grant is no longer usable – user has to authorize again.</summary>
    public bool IsInvalidGrant => Error is "invalid_grant" or "invalid_client" || Status == 401;
}
