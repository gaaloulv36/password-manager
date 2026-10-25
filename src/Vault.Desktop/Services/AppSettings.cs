using System.IO;
using System.Text.Json;

namespace Vault.Desktop.Services;

/// <summary>Non-secret app preferences, stored in %APPDATA%\PasswordManager\settings.json.</summary>
public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PasswordManager", "settings.json");

    public string? LastVaultPath { get; set; }
    public int AutoLockMinutes { get; set; } = 5;
    public int ClipboardClearSeconds { get; set; } = 30;
    public bool LockOnMinimize { get; set; }

    /// <summary>Closing the window keeps the app in the tray (needed later for the browser extension).</summary>
    public bool CloseToTray { get; set; } = true;

    public bool HasShownTrayHint { get; set; }

    /// <summary>Hide the app's windows from screenshots and screen sharing.</summary>
    public bool ProtectFromScreenCapture { get; set; } = true;

    /// <summary>Install a downloaded update by itself while the vault is locked and the PC has been idle a while.</summary>
    public bool InstallUpdatesWhenAway { get; set; } = true;

    /// <summary>Download new versions in the background (installed copies only).</summary>
    public bool CheckForUpdates { get; set; } = true;

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
