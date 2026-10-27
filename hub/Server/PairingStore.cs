using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VoicedeckEdge.Hub.Infrastructure;

namespace VoicedeckEdge.Hub.Server;

/// <summary>A paired widget. Only the SHA-256 hash of its device token is stored, never the token.</summary>
internal sealed record Pairing(string Id, string TokenHash, DateTimeOffset Created, string Origin);

/// <summary>Paired widgets, persisted as JSON (contains no personal data).</summary>
internal sealed class PairingStore
{
    private readonly string? _path;
    private readonly object _gate = new();
    private List<Pairing> _pairings;

    /// <summary>A pairing was removed; open connections using it must close.</summary>
    public event Action<string>? Revoked;

    public PairingStore(string? path)
    {
        _path = path;
        _pairings = Load(path);
    }

    public IReadOnlyList<Pairing> All
    {
        get { lock (_gate) return _pairings.ToList(); }
    }

    /// <summary>Creates a pairing and returns (pairing, device token). The token is shown to the widget once.</summary>
    public (Pairing Pairing, string Token) Create(string origin)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var pairing = new Pairing(
            Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
            Hash(token),
            DateTimeOffset.Now,
            origin);
        lock (_gate)
        {
            _pairings.Add(pairing);
            Save();
        }
        return (pairing, token);
    }

    /// <summary>Returns the pairing for a device token, or null. Constant-time comparison.</summary>
    public Pairing? Verify(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length != 64) return null;
        var hash = Encoding.ASCII.GetBytes(Hash(token));
        lock (_gate)
        {
            return _pairings.FirstOrDefault(p =>
                CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(p.TokenHash), hash));
        }
    }

    public void Revoke(string pairingId)
    {
        lock (_gate)
        {
            if (_pairings.RemoveAll(p => p.Id == pairingId) == 0) return;
            Save();
        }
        Log.Info("Kopplung aufgehoben.");
        Revoked?.Invoke(pairingId);
    }

    public void RevokeAll()
    {
        List<Pairing> removed;
        lock (_gate)
        {
            removed = _pairings;
            _pairings = [];
            Save();
        }
        foreach (var p in removed) Revoked?.Invoke(p.Id);
    }

    internal static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private void Save()
    {
        if (_path != null) File.WriteAllText(_path, JsonSerializer.Serialize(_pairings));
    }

    private static List<Pairing> Load(string? path)
    {
        try
        {
            if (path != null && File.Exists(path))
                return JsonSerializer.Deserialize<List<Pairing>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Log.Warn($"pairings.json nicht lesbar – alle Widgets müssen neu gekoppelt werden ({ex.GetType().Name}).");
        }
        return [];
    }
}
