using System.Text.Json;

namespace Vault.Core.Storage;

public static class VaultFileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static VaultDocument Load(string path)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<VaultDocument>(stream, JsonOptions)
            ?? throw new InvalidDataException("Vault file is empty.");

        if (document.FormatVersion > VaultDocument.CurrentFormatVersion)
            throw new InvalidDataException($"Vault format v{document.FormatVersion} is newer than this app supports.");
        return document;
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
