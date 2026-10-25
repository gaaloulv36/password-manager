using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VaultForge.Core.Generators;
using VaultForge.Core.Import;
using VaultForge.Core.Storage;
using VaultForge.Desktop.Browser;
using VaultForge.Desktop.Services;

namespace VaultForge.Desktop.ViewModels;

public sealed record Choice(int Value, string Label);

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly VaultSession _session;
    private readonly AutoLockService _autoLock;
    private readonly ClipboardService _clipboard;
    private readonly DialogService _dialogs;

    private readonly PairingStore _pairings;
    private readonly UpdateService _updates;

    public SettingsViewModel(AppSettings settings, VaultSession session, AutoLockService autoLock, ClipboardService clipboard, DialogService dialogs,
        PairingStore pairings, UpdateService updates)
    {
        _updates = updates;
        _startWithWindows = StartupRegistration.IsEnabled;
        _protectFromScreenCapture = settings.ProtectFromScreenCapture;
        _checkForUpdates = settings.CheckForUpdates;
        _installUpdatesWhenAway = settings.InstallUpdatesWhenAway;
        _updateStatus = updates.ReadyVersion is { } ready ? $"Version {ready} is downloaded and will be installed when you exit." : null;
        _settings = settings;
        _session = session;
        _autoLock = autoLock;
        _clipboard = clipboard;
        _dialogs = dialogs;
        _pairings = pairings;
        ConnectedBrowsers = new ObservableCollection<BrowserPairing>(pairings.All);

        _autoLockMinutes = AutoLockChoices.FirstOrDefault(c => c.Value == settings.AutoLockMinutes) ?? AutoLockChoices[2];
        _clipboardSeconds = ClipboardChoices.FirstOrDefault(c => c.Value == settings.ClipboardClearSeconds) ?? ClipboardChoices[2];
        _lockOnMinimize = settings.LockOnMinimize;
        _closeToTray = settings.CloseToTray;
    }

    /// <summary>Raised after a password action so the view can clear its PasswordBoxes.</summary>
    public event EventHandler? PasswordFieldsCleared;

    public string VaultPath => _session.Path ?? "";

    // ---- Security ----

    public IReadOnlyList<Choice> AutoLockChoices { get; } =
        [new(1, "1 minute"), new(2, "2 minutes"), new(5, "5 minutes"), new(10, "10 minutes"), new(15, "15 minutes"), new(30, "30 minutes"), new(60, "1 hour")];

    public IReadOnlyList<Choice> ClipboardChoices { get; } =
        [new(10, "10 seconds"), new(20, "20 seconds"), new(30, "30 seconds"), new(60, "1 minute"), new(90, "90 seconds")];

    [ObservableProperty] private Choice _autoLockMinutes;
    [ObservableProperty] private Choice _clipboardSeconds;
    [ObservableProperty] private bool _lockOnMinimize;
    [ObservableProperty] private bool _closeToTray;

    /// <summary>Preferences apply and save immediately — no OK button to forget.</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(AutoLockMinutes) or nameof(ClipboardSeconds) or nameof(LockOnMinimize) or nameof(CloseToTray)))
            return;

        _settings.AutoLockMinutes = AutoLockMinutes.Value;
        _settings.ClipboardClearSeconds = ClipboardSeconds.Value;
        _settings.LockOnMinimize = LockOnMinimize;
        _settings.CloseToTray = CloseToTray;
        _settings.Save();

        _autoLock.IdleLimit = TimeSpan.FromMinutes(AutoLockMinutes.Value);
        _clipboard.ClearAfter = TimeSpan.FromSeconds(ClipboardSeconds.Value);
    }

    // ---- Master password ----

    [ObservableProperty] private string _currentPassword = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewPasswordStrength))]
    private string _newPassword = "";

    [ObservableProperty] private string _confirmNewPassword = "";
    [ObservableProperty] private string? _passwordMessage;
    [ObservableProperty] private bool _passwordMessageIsError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangePasswordCommand), nameof(ExportCsvCommand))]
    private bool _isBusy;

    public string NewPasswordStrength => NewPassword.Length == 0 ? "" : PasswordStrength.Evaluate(NewPassword) switch
    {
        StrengthLevel.VeryWeak => "Very weak",
        StrengthLevel.Weak => "Weak",
        StrengthLevel.Fair => "Fair",
        StrengthLevel.Strong => "Strong",
        _ => "Very strong",
    };

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task ChangePasswordAsync()
    {
        if (NewPassword.Length < 10) { ShowPasswordMessage("Use at least 10 characters.", error: true); return; }
        if (NewPassword != ConfirmNewPassword) { ShowPasswordMessage("The new passwords don't match.", error: true); return; }

        IsBusy = true;
        ShowPasswordMessage("Changing…", error: false);
        try
        {
            var (current, next) = (CurrentPassword, NewPassword);
            if (!await Task.Run(() => _session.VerifyMasterPassword(current)))
            {
                ShowPasswordMessage("The current master password is wrong.", error: true);
                return;
            }

            await Task.Run(() => _session.Vault!.ChangeMasterPassword(next));
            _session.Save();
            ShowPasswordMessage("Master password changed. Your other PCs will ask for the new one next time they unlock.", error: false);
            PasswordFieldsCleared?.Invoke(this, EventArgs.Empty);
        }
        catch (IOException ex)
        {
            ShowPasswordMessage($"The vault couldn't be saved: {ex.Message}", error: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- App & updates ----

    public string Version => _updates.CurrentVersion;
    public bool IsInstalled => _updates.IsInstalled;
    public bool IsUpdateReady => _updates.ReadyVersion is not null;

    [ObservableProperty] private bool _protectFromScreenCapture;

    partial void OnProtectFromScreenCaptureChanged(bool value)
    {
        _settings.ProtectFromScreenCapture = value;
        _settings.Save();
        ScreenCaptureProtection.SetEnabled(value);
    }

    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _checkForUpdates;
    [ObservableProperty] private bool _installUpdatesWhenAway;

    partial void OnInstallUpdatesWhenAwayChanged(bool value)
    {
        _settings.InstallUpdatesWhenAway = value;
        _settings.Save();
    }
    [ObservableProperty] private string? _updateStatus;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdateNowCommand))]
    private bool _isCheckingForUpdate;

    partial void OnStartWithWindowsChanged(bool value) => StartupRegistration.SetEnabled(value);

    partial void OnCheckForUpdatesChanged(bool value)
    {
        _settings.CheckForUpdates = value;
        _settings.Save();
    }

    [RelayCommand(CanExecute = nameof(CanCheckForUpdate))]
    private async Task CheckForUpdateNowAsync()
    {
        IsCheckingForUpdate = true;
        UpdateStatus = "Checking…";
        try
        {
            var version = await _updates.CheckAndDownloadAsync(p => UpdateStatus = $"Downloading… {p}%");
            UpdateStatus = version is null ? "You have the latest version." : $"Version {version} is ready.";
            OnPropertyChanged(nameof(IsUpdateReady));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or InvalidOperationException)
        {
            UpdateStatus = "Couldn't check for updates. Are you online?";
        }
        finally
        {
            IsCheckingForUpdate = false;
        }
    }

    private bool CanCheckForUpdate() => IsInstalled && !IsCheckingForUpdate;

    [RelayCommand]
    private void RestartToUpdate() => _updates.RestartToUpdate();

    public const string UserGuideUrl = UpdateService.RepositoryUrl + "/blob/main/docs/USER-GUIDE.md";

    [RelayCommand]
    private static void OpenUserGuide() => Process.Start(new ProcessStartInfo(UserGuideUrl) { UseShellExecute = true });

    // ---- Windows Hello ----

    [ObservableProperty] private bool _isHelloAvailable;
    [ObservableProperty] private bool _isHelloEnabled;
    [ObservableProperty] private string? _helloMessage;

    public async Task LoadHelloStateAsync()
    {
        IsHelloAvailable = await WindowsHelloService.IsAvailableAsync();
        IsHelloEnabled = _session.Vault is { } vault && _session.Hello.IsEnabled(vault.Document.VaultId);
    }

    [RelayCommand]
    private async Task EnableHelloAsync()
    {
        var vault = _session.Vault!;
        var key = vault.ExportVaultKey();
        try
        {
            var outcome = await _session.Hello.EnableAsync(vault.Document.VaultId, key);
            IsHelloEnabled = outcome == HelloOutcome.Success;
            HelloMessage = outcome switch
            {
                HelloOutcome.Success => "Turned on. Next time, unlock with your face, fingerprint or PIN.",
                HelloOutcome.Cancelled => null,
                HelloOutcome.NotSetUp => "Set up Windows Hello first (Windows Settings → Accounts → Sign-in options).",
                _ => "Windows Hello couldn't be turned on.",
            };
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
        }
    }

    [RelayCommand]
    private async Task DisableHelloAsync()
    {
        await _session.Hello.DisableAsync(_session.Vault!.Document.VaultId);
        IsHelloEnabled = false;
        HelloMessage = "Turned off. Only the master password unlocks this vault now.";
    }

    // ---- Browser extension ----

    public ObservableCollection<BrowserPairing> ConnectedBrowsers { get; }

    [RelayCommand]
    private void DisconnectBrowser(BrowserPairing pairing)
    {
        if (!_dialogs.Confirm($"Disconnect {pairing.BrowserName}? It will need to be connected again to fill logins.", "Disconnect browser")) return;
        _pairings.Remove(pairing.Id);
        ConnectedBrowsers.Remove(pairing);
    }

    // ---- Backup & export ----

    [ObservableProperty] private string _exportPassword = "";
    [ObservableProperty] private string? _exportMessage;

    [RelayCommand]
    private void BackupNow()
    {
        // Spaces, not dashes: "Name-xyz" is how OneDrive names conflict copies.
        var name = $"{Path.GetFileNameWithoutExtension(VaultPath)} backup {DateTime.Now:yyyy-MM-dd}.pmvault";
        if (_dialogs.PickSaveFile("Save encrypted backup", name, "Password vault (*.pmvault)|*.pmvault") is not { } target) return;

        try
        {
            _session.Save();
            File.Copy(VaultPath, target, overwrite: true);
            _dialogs.ShowInfo($"Backup saved to {target}.\n\nIt opens with your current master password.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError($"The backup couldn't be saved.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private static void OpenBackupFolder()
    {
        Directory.CreateDirectory(VaultBackups.DefaultDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{VaultBackups.DefaultDirectory}\""));
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task ExportCsvAsync()
    {
        IsBusy = true;
        try
        {
            var password = ExportPassword;
            if (!await Task.Run(() => _session.VerifyMasterPassword(password)))
            {
                ExportMessage = "Wrong master password.";
                return;
            }
            ExportMessage = null;
            PasswordFieldsCleared?.Invoke(this, EventArgs.Empty);

            if (!_dialogs.Confirm(
                    "The exported file will contain ALL your passwords in plain text. Anyone who gets the file can read them.\n\n" +
                    "Only export to move to another password manager, and delete the file right after. Continue?",
                    "Export unencrypted passwords"))
                return;

            if (_dialogs.PickSaveFile("Export passwords (unencrypted)", "passwords-export.csv", "CSV file (*.csv)|*.csv") is not { } target) return;
            await File.WriteAllTextAsync(target, VaultExporter.ToCsv(_session.Vault!.GetItems()));
            ExportMessage = $"Exported to {target}. Delete it once you've imported it elsewhere.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ExportMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool IsNotBusy() => !IsBusy;

    private void ShowPasswordMessage(string message, bool error)
    {
        PasswordMessage = message;
        PasswordMessageIsError = error;
    }
}
