using System.Net.Http;
using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace Vault.Desktop.Services;

/// <summary>
/// Updates from this project's GitHub Releases (Velopack). Only active in an installed copy;
/// a development build run from the source folder reports itself as such and never updates.
/// </summary>
public sealed class UpdateService
{
    public const string RepositoryUrl = "https://github.com/gaaloulv36/password-manager";

    private readonly UpdateManager? _manager;
    private UpdateInfo? _ready;

    public UpdateService()
    {
        try
        {
            var manager = new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
            if (manager.IsInstalled) _manager = manager;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // Not installed by Velopack (e.g. `dotnet run`).
        }
    }

    public bool IsInstalled => _manager is not null;

    public string CurrentVersion => _manager?.CurrentVersion?.ToString()
        ?? $"{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)} (development build)";

    /// <summary>Version downloaded and waiting to be installed, if any.</summary>
    public string? ReadyVersion => _ready?.TargetFullRelease.Version.ToString();

    /// <summary>Checks GitHub and downloads a newer version if there is one. Returns its version, or null if up to date.</summary>
    /// <exception cref="HttpRequestException">No connection to GitHub.</exception>
    public async Task<string?> CheckAndDownloadAsync(Action<int>? progress = null)
    {
        if (_manager is null) return null;
        var update = await _manager.CheckForUpdatesAsync();
        if (update is null) return null;

        await _manager.DownloadUpdatesAsync(update, progress);
        _ready = update;
        return ReadyVersion;
    }

    /// <summary>Installs the downloaded update now and starts the new version.</summary>
    public void RestartToUpdate()
    {
        if (_manager is not null && _ready is not null) _manager.ApplyUpdatesAndRestart(_ready.TargetFullRelease);
    }

    /// <summary>If an update is waiting, install it silently once the app has exited.</summary>
    public void ApplyOnExit()
    {
        if (_manager is not null && _ready is not null)
            _manager.WaitExitThenApplyUpdates(_ready.TargetFullRelease, silent: true, restart: false);
    }
}
