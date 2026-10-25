using System.IO;
using Microsoft.Win32;
using VaultForge.Core;

namespace VaultForge.Desktop.Services;

/// <summary>
/// One-time copy of data from "Password Manager" (≤ 1.0.6) to VaultForge's folders. It copies rather than
/// moves, so the old app keeps working until it's uninstalled, and nothing is lost if this fails halfway.
/// Runs only while VaultForge has no settings of its own yet.
/// </summary>
public static class LegacyDataMigration
{
    /// <summary>True when data was copied (first start after switching from Password Manager).</summary>
    public static bool Run()
    {
        var newSettings = Path.Combine(ProductInfo.RoamingDataDirectory, "settings.json");
        var oldSettings = Path.Combine(ProductInfo.LegacyRoamingDataDirectory, "settings.json");
        if (File.Exists(newSettings) || !File.Exists(oldSettings)) return false;

        try
        {
            // Settings and connected browsers (pairing keys are DPAPI-protected for this Windows user, so they carry over).
            CopyFile(Path.Combine(ProductInfo.LegacyRoamingDataDirectory, "browser-pairings.json"), ProductInfo.RoamingDataDirectory);
            // Backups (incl. Damaged and merged conflict copies) and Windows Hello data. NativeMessaging is rewritten on start.
            CopyDirectory(Path.Combine(ProductInfo.LegacyLocalDataDirectory, "Backups"), Path.Combine(ProductInfo.LocalDataDirectory, "Backups"));
            CopyDirectory(Path.Combine(ProductInfo.LegacyLocalDataDirectory, "QuickUnlock"), Path.Combine(ProductInfo.LocalDataDirectory, "QuickUnlock"));
            // Settings last: their presence marks the migration as done.
            CopyFile(oldSettings, ProductInfo.RoamingDataDirectory);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // start fresh rather than fail to start; the old data is untouched
        }
    }

    /// <summary>Whether the old app was set to start with Windows (so VaultForge can take that over).</summary>
    public static bool LegacyStartWithWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue(StartupRegistration.LegacyValueName) is string;
    }

    private static void CopyFile(string source, string targetDirectory)
    {
        if (!File.Exists(source)) return;
        Directory.CreateDirectory(targetDirectory);
        File.Copy(source, Path.Combine(targetDirectory, Path.GetFileName(source)), overwrite: false);
    }

    private static void CopyDirectory(string source, string target)
    {
        if (!Directory.Exists(source)) return;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (!File.Exists(destination)) File.Copy(file, destination);
        }
    }
}
