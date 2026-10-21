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
