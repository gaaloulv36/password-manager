using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VaultForge.Desktop.Browser;
using VaultForge.Desktop.Services;

namespace VaultForge.Desktop.ViewModels;

/// <summary>Window shell: decides which page is shown and owns navigation between pages.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly VaultSession _session;
    private readonly AppSettings _settings;
    private readonly ClipboardService _clipboard;
    private readonly DialogService _dialogs;
    private readonly AutoLockService _autoLock;
    private readonly PairingStore _pairings;
    private readonly UpdateService _updates;

    [ObservableProperty]
    private ObservableObject _currentPage = null!;

    /// <summary>Set when an update has been downloaded: shows the "restart to update" bar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateMessage))]
    private string? _updateReadyVersion;

    public string UpdateMessage => $"VaultForge {UpdateReadyVersion} is ready to install.";

    [RelayCommand]
    private void RestartToUpdate() => _updates.RestartToUpdate();

    [RelayCommand]
    private void DismissUpdate() => UpdateReadyVersion = null;

    public MainViewModel(VaultSession session, AppSettings settings, ClipboardService clipboard, DialogService dialogs, AutoLockService autoLock, PairingStore pairings, UpdateService updates)
    {
        _session = session;
        _settings = settings;
        _clipboard = clipboard;
        _dialogs = dialogs;
        _autoLock = autoLock;
        _pairings = pairings;
        _updates = updates;

        _session.Locked += (_, _) => ShowUnlock(_session.Path!);
        ShowStart();
    }

    public void ShowStart()
    {
        if (_settings.LastVaultPath is { } path && File.Exists(path)) ShowUnlock(path);
        else CurrentPage = new WelcomeViewModel(this, _dialogs);
    }

    public void ShowUnlock(string vaultPath) => CurrentPage = new UnlockViewModel(this, _session, _settings, _dialogs, vaultPath);

    public void ShowCreate() => CurrentPage = new CreateVaultViewModel(this, _session, _settings, _dialogs);

    public void ShowVault() =>
        CurrentPage = new VaultViewModel(_session, _clipboard, _dialogs,
            () => new SettingsViewModel(_settings, _session, _autoLock, _clipboard, _dialogs, _pairings, _updates));

    partial void OnCurrentPageChanging(ObservableObject value)
    {
        (CurrentPage as IDisposable)?.Dispose();
    }
}
