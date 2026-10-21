using System.Text.RegularExpressions;

namespace Vault.Core.Storage;

/// <summary>What a sync brought in from disk.</summary>
public sealed record SyncResult(int Added, int Updated, int Deleted, bool MasterPasswordChanged, IReadOnlyList<string> MergedConflictCopies)
{
    public bool HasChanges => Added > 0 || Updated > 0 || Deleted > 0 || MasterPasswordChanged || MergedConflictCopies.Count > 0;
}

/// <summary>
/// Keeps an open vault and its synced file (OneDrive, Dropbox, …) in step, including the
/// "conflict copies" sync services create when two PCs save at the same time.
/// </summary>
public static class VaultSync
{
    /// <summary>
    /// Merges the vault file and any conflict copies into <paramref name="vault"/>, then writes the result back
    /// if the file is missing anything — or always, with <paramref name="forceWrite"/> (after a local edit).
    /// Not writing when nothing changed matters: otherwise two PCs would keep re-saving in response to each other.
    /// Merged conflict copies are moved to <paramref name="archiveDirectory"/>.
    /// </summary>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The file was tampered with.</exception>
    public static SyncResult SyncWithDisk(UnlockedVault vault, string path, string archiveDirectory, bool forceWrite)
    {
        int added = 0, updated = 0, deleted = 0;
        var passwordChanged = false;
        var needsWrite = forceWrite || !File.Exists(path);

        void Absorb(MergeResult result)
        {
            added += result.Added;
            updated += result.Updated;
            deleted += result.Deleted;
            passwordChanged |= result.MasterPasswordChanged;
        }

        if (File.Exists(path))
        {
            var onDisk = VaultFileStore.Load(path);
            Absorb(vault.Merge(onDisk));
            // After pulling everything in, does the file lack anything we have?
            needsWrite |= VaultMerger.Merge(onDisk, vault.Document) is { HasChanges: true };
        }

        var merged = new List<string>();
        foreach (var copy in FindConflictCopies(path))
        {
            VaultDocument document;
            try
            {
                document = VaultFileStore.Load(copy);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                continue; // unreadable or mid-sync; try again next time
            }
            if (document.VaultId != vault.Document.VaultId) continue;

            Absorb(vault.Merge(document));
            merged.Add(copy);
            needsWrite = true;
        }

        if (needsWrite) VaultFileStore.Save(vault.Document, path);

        // Only archive once their content is safely in the main file.
        foreach (var copy in merged)
        {
            Directory.CreateDirectory(archiveDirectory);
            var target = Path.Combine(archiveDirectory,
                $"{Path.GetFileNameWithoutExtension(copy)} (merged {DateTime.Now:yyyy-MM-dd HHmmss}).pmvault");
            File.Move(copy, target, overwrite: true);
        }

        return new SyncResult(added, updated, deleted, passwordChanged, merged);
    }

    /// <summary>
    /// Conflict copies next to the vault, as named by common sync services. For "Passwords.pmvault":
    /// OneDrive "Passwords-DESKTOP-AB12.pmvault", Dropbox "Passwords (Bob's conflicted copy 2026-10-21).pmvault",
    /// Syncthing "Passwords.sync-conflict-20261021-101500-ABCDEFG.pmvault", numbered "Passwords (1).pmvault".
    /// </summary>
    public static IReadOnlyList<string> FindConflictCopies(string vaultPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(vaultPath))!;
        if (!Directory.Exists(directory)) return [];

        var name = Regex.Escape(Path.GetFileNameWithoutExtension(vaultPath));
        var pattern = new Regex(
            $@"^{name}(-[A-Za-z0-9-]+|\s\(.*conflicted copy.*\)|\.sync-conflict-[0-9-]+-[A-Z0-9]+|\s\(\d+\))$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return Directory.GetFiles(directory, "*.pmvault")
            .Where(f => !string.Equals(Path.GetFullPath(f), Path.GetFullPath(vaultPath), StringComparison.OrdinalIgnoreCase))
            .Where(f => pattern.IsMatch(Path.GetFileNameWithoutExtension(f)))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
