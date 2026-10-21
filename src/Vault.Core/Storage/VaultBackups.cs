using System.Globalization;

namespace Vault.Core.Storage;

/// <summary>
/// Dated copies of the (encrypted) vault file, kept outside the sync folder so a bad sync or an
/// accidental mass-delete can be undone. Backups open with the master password that was current when they were made.
/// </summary>
public static class VaultBackups
{
    public const int DefaultKeepCount = 14;

    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PasswordManager", "Backups");

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
