using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Vault.Desktop.Browser;

public sealed record BrowserPairing(string Id, string BrowserName, DateTimeOffset PairedAt, byte[] ProtectedKey);

/// <summary>
/// Browsers paired with this PC. Keys are protected with Windows DPAPI (current user), so the file is
/// useless on another machine or account. Pairings are per PC on purpose and never synced.
/// </summary>
public sealed class PairingStore
{
    private static readonly byte[] Entropy = "pm-browser-pairing"u8.ToArray();
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PasswordManager", "browser-pairings.json");

    private readonly List<BrowserPairing> _pairings;

    public PairingStore()
    {
        try
        {
            _pairings = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<BrowserPairing>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _pairings = [];
        }
    }

    public event EventHandler? Changed;

    public IReadOnlyList<BrowserPairing> All => _pairings;

    public BrowserPairing Add(string browserName, byte[] key)
    {
        var pairing = new BrowserPairing(Guid.NewGuid().ToString("N"), browserName, DateTimeOffset.Now,
            ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser));
        _pairings.Add(pairing);
        Save();
        return pairing;
    }

    public byte[]? GetKey(string pairingId)
    {
        var pairing = _pairings.Find(p => p.Id == pairingId);
        if (pairing is null) return null;
        try
        {
            return ProtectedData.Unprotect(pairing.ProtectedKey, Entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public void Remove(string pairingId)
    {
        if (_pairings.RemoveAll(p => p.Id == pairingId) > 0) Save();
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(_pairings, new JsonSerializerOptions { WriteIndented = true }));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
