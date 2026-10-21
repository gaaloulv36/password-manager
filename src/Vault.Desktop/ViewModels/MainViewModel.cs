using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Vault.Desktop.Services;

namespace Vault.Desktop.ViewModels;

/// <summary>Window shell: decides which page is shown and owns navigation between pages.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly VaultSession _session;
    private readonly AppSettings _settings;
    private readonly ClipboardService _clipboard;
    private readonly DialogService _dialogs;
    private readonly AutoLockService _autoLock;

    [ObservableProperty]
    private ObservableObject _currentPage = null!;

    public MainViewModel(VaultSession session, AppSettings settings, ClipboardService clipboard, DialogService dialogs, AutoLockService autoLock)
    {
        _session = session;
        _settings = settings;
        _clipboard = clipboard;
        _dialogs = dialogs;
        _autoLock = autoLock;

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
            () => new SettingsViewModel(_settings, _session, _autoLock, _clipboard, _dialogs));

    partial void OnCurrentPageChanging(ObservableObject value)
    {
        (CurrentPage as IDisposable)?.Dispose();
    }
}
