namespace VaultForge.Core;

/// <summary>Product identity and data locations (Forge Studio brand: "product word + Forge").</summary>
public static class ProductInfo
{
    public const string Name = "VaultForge";
    public const string Studio = "Forge Studio";
    public const string RepositoryUrl = "https://github.com/gaaloulv36/vaultforge";

    /// <summary>Settings and connected browsers (%APPDATA%\Forge Studio\VaultForge).</summary>
    public static string RoamingDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Studio, Name);

    /// <summary>
    /// Backups, Windows Hello data, browser connector manifests (%LOCALAPPDATA%\Forge Studio\VaultForge).
    /// Deliberately outside the install folder (%LOCALAPPDATA%\VaultForge), which the uninstaller deletes.
    /// </summary>
    public static string LocalDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Studio, Name);

    /// <summary>Where versions up to 1.0.6 ("Password Manager") kept their data; copied over once on first start.</summary>
    public static string LegacyRoamingDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PasswordManager");

    /// <inheritdoc cref="LegacyRoamingDataDirectory"/>
    public static string LegacyLocalDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PasswordManager");
}
