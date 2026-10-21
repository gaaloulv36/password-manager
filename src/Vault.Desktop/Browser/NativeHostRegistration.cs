using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using Vault.Core.Browser;

namespace Vault.Desktop.Browser;

/// <summary>
/// Tells the browsers where the native host is (per-user registry, no admin rights needed).
/// Re-run on every start so the path stays right if the app moves.
/// </summary>
public static class NativeHostRegistration
{
    /// <summary>Fixed by the "key" in extension/manifest.json, so it's the same in every Chromium browser.</summary>
    public const string ChromiumExtensionId = "mdkfdlddmhpcgcdlbnbjmedhbpfnodgp";

    /// <summary>Set in extension/build.mjs (browser_specific_settings.gecko.id).</summary>
    public const string FirefoxExtensionId = "password-manager@gaaloulv36.github.io";

    private const string FirefoxKey = @"Software\Mozilla\NativeMessagingHosts\";

    // Opera (and Brave, Vivaldi) read Chrome's registry location on Windows.
    private static readonly string[] ChromiumKeys =
    [
        @"Software\Google\Chrome\NativeMessagingHosts\",
        @"Software\Microsoft\Edge\NativeMessagingHosts\",
        @"Software\Chromium\NativeMessagingHosts\",
    ];

    private static readonly string ManifestDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PasswordManager", "NativeMessaging");

    public static string HostPath { get; } = Path.Combine(AppContext.BaseDirectory, "Vault.NativeHost.exe");

    public static void Register()
    {
        if (!File.Exists(HostPath)) return;
        Directory.CreateDirectory(ManifestDirectory);

        var chromiumManifest = Path.Combine(ManifestDirectory, "chromium.json");
        File.WriteAllText(chromiumManifest, JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["name"] = BrowserProtocol.HostName,
            ["description"] = "Password Manager browser integration",
            ["path"] = HostPath,
            ["type"] = "stdio",
            ["allowed_origins"] = new[] { $"chrome-extension://{ChromiumExtensionId}/" },
        }, new JsonSerializerOptions { WriteIndented = true }));

        foreach (var key in ChromiumKeys)
        {
            using var registryKey = Registry.CurrentUser.CreateSubKey(key + BrowserProtocol.HostName);
            registryKey.SetValue("", chromiumManifest);
        }

        // Firefox identifies extensions by id ("allowed_extensions") instead of origin.
        var firefoxManifest = Path.Combine(ManifestDirectory, "firefox.json");
        File.WriteAllText(firefoxManifest, JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["name"] = BrowserProtocol.HostName,
            ["description"] = "Password Manager browser integration",
            ["path"] = HostPath,
            ["type"] = "stdio",
            ["allowed_extensions"] = new[] { FirefoxExtensionId },
        }, new JsonSerializerOptions { WriteIndented = true }));

        using var firefoxKey = Registry.CurrentUser.CreateSubKey(FirefoxKey + BrowserProtocol.HostName);
        firefoxKey.SetValue("", firefoxManifest);
    }
}
