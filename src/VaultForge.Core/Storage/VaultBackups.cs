using System.Globalization;

namespace VaultForge.Core.Storage;

/// <summary>
/// Dated copies of the (encrypted) vault file, kept outside the sync folder so a bad sync or an
/// accidental mass-delete can be undone. Backups open with the master password that was current when they were made.
/// </summary>
public static class VaultBackups
{
    public const int DefaultKeepCount = 14;

    /// <summary>Outside the install folder, so uninstalling the app keeps the backups.</summary>
    public static string DefaultDirectory { get; } = Path.Combine(ProductInfo.LocalDataDirectory, "Backups");

    /// <summary>Where a damaged or rejected vault file is moved before it's replaced.</summary>
    public static string DamagedDirectory { get; } = Path.Combine(DefaultDirectory, "Damaged");

    /// <summary>
    /// Replaces the vault file with a backup. The backup is checked to be a readable vault first,
    /// and the current file (if any) is moved to <paramref name="damagedDirectory"/> rather than deleted.
    /// </summary>
    /// <exception cref="InvalidDataException">The backup isn't a readable vault file.</exception>
    public static void Restore(string backupPath, string vaultPath, string damagedDirectory)
    {
        VaultFileStore.Load(backupPath);
        SetAside(vaultPath, damagedDirectory);
        File.Copy(backupPath, vaultPath);
    }

    /// <summary>Moves a vault file out of the way (keeping it), e.g. before overwriting a damaged file.</summary>
    public static void SetAside(string vaultPath, string damagedDirectory)
    {
        if (!File.Exists(vaultPath)) return;
        Directory.CreateDirectory(damagedDirectory);
        var target = Path.Combine(damagedDirectory,
            $"{Path.GetFileNameWithoutExtension(vaultPath)} (set aside {DateTime.Now.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture)}).pmvault");
        File.Move(vaultPath, target, overwrite: true);
    }

    /// <summary>Copies the vault once per day and prunes old copies. Returns the backup path, or null if today's already exists.</summary>
    public static string? CreateDailyBackup(string vaultPath, string backupDirectory, DateOnly today, int keepCount = DefaultKeepCount)
    {
        Directory.CreateDirectory(backupDirectory);
        var name = Path.GetFileNameWithoutExtension(vaultPath);
        var backupPath = Path.Combine(backupDirectory, $"{name}-{today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.pmvault");
        if (File.Exists(backupPath)) return null;

        File.Copy(vaultPath, backupPath);

        // ISO dates sort chronologically, so the oldest come first.
        var backups = Directory.GetFiles(backupDirectory, $"{name}-????-??-??.pmvault").Order(StringComparer.Ordinal).ToList();
        foreach (var old in backups.Take(Math.Max(0, backups.Count - keepCount)))
            File.Delete(old);

        return backupPath;
    }
}
