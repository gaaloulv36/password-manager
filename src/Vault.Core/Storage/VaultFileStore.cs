using System.Text.Json;

namespace Vault.Core.Storage;

public static class VaultFileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Far beyond any real vault (≈ 100k entries); stops a planted giant file from exhausting memory.</summary>
    public const long MaxFileSize = 64 * 1024 * 1024;

    /// <summary>
    /// Reads a vault file. The file is untrusted (it lives in a sync folder), so its structure is checked
    /// here; its contents are authenticated when it's unlocked or merged.
    /// </summary>
    /// <exception cref="InvalidDataException">Not a well-formed vault file.</exception>
    public static VaultDocument Load(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaxFileSize) throw new InvalidDataException("The file is too large to be a vault.");

        var document = JsonSerializer.Deserialize<VaultDocument>(stream, JsonOptions)
            ?? throw new InvalidDataException("Vault file is empty.");

        if (document.FormatVersion > VaultDocument.CurrentFormatVersion)
            throw new InvalidDataException($"Vault format v{document.FormatVersion} is newer than this app supports.");
        Validate(document);
        return document;
    }

    private static void Validate(VaultDocument document)
    {
        if (document.Kdf is null || document.WrappedVaultKey is null || document.HeaderMac is null || document.Items is null)
            throw new InvalidDataException("The vault header is incomplete.");
        document.Kdf.Validate();

        var ids = new HashSet<Guid>();
        foreach (var item in document.Items)
        {
            if (item?.Payload is null) throw new InvalidDataException("A vault entry is incomplete.");
            if (!ids.Add(item.Id)) throw new InvalidDataException("The vault contains the same entry twice.");
        }
    }

    /// <summary>
    /// Saves the vault without losing changes another PC made to the file since it was opened:
    /// the current file is merged in first. Returns what the merge brought in, or null for a new file.
    /// </summary>
    public static MergeResult? SaveMerged(UnlockedVault vault, string path)
    {
        var result = File.Exists(path) ? vault.Merge(Load(path)) : null;
        Save(vault.Document, path);
        return result;
    }

    /// <summary>Writes to a temp file first, then swaps it in, so a crash never leaves a half-written vault.</summary>
    public static void Save(VaultDocument document, string path)
    {
        var tempPath = path + ".tmp";
        using (var stream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(stream, document, JsonOptions);
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(path))
            File.Replace(tempPath, path, path + ".bak");
        else
            File.Move(tempPath, path);
    }
}
