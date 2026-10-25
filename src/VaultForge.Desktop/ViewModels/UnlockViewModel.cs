using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VaultForge.Core;
using VaultForge.Core.Storage;
using VaultForge.Desktop.Services;

namespace VaultForge.Desktop.ViewModels;

public sealed partial class UnlockViewModel : ObservableObject
{
    private readonly MainViewModel _navigation;
    private readonly VaultSession _session;
    private readonly AppSettings _settings;
    private readonly DialogService _dialogs;

    public UnlockViewModel(MainViewModel navigation, VaultSession session, AppSettings settings, DialogService dialogs, string vaultPath)
    {
        _navigation = navigation;
        _session = session;
        _settings = settings;
        _dialogs = dialogs;
        VaultPath = vaultPath;
        VaultName = Path.GetFileNameWithoutExtension(vaultPath);
        CanUseHello = session.IsHelloEnabled(vaultPath);
    }

    public string VaultPath { get; }
    public string VaultName { get; }

    /// <summary>Windows Hello was turned on for this vault on this PC.</summary>
    [ObservableProperty]
    private bool _canUseHello;

    /// <summary>Set from the view's PasswordBox (which can't be data-bound).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private string _password = "";

    [ObservableProperty]
    private string? _error;

    /// <summary>The file can't be opened (damaged or modified): offer to restore a backup.</summary>
    [ObservableProperty]
    private bool _isFileDamaged;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand), nameof(UnlockWithHelloCommand))]
    private bool _isBusy;

    private bool CanUnlock() => Password.Length > 0 && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private Task UnlockAsync()
    {
        var password = Password;
        return RunUnlockAsync(() => Task.Run(() => _session.Unlock(VaultPath, password)));
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private Task UnlockWithHelloAsync() => RunUnlockAsync(async () =>
    {
        var outcome = await _session.UnlockWithHelloAsync(VaultPath);
        if (outcome != HelloOutcome.Success)
            throw new HelloNotCompletedException(outcome);
    });

    private bool IsNotBusy() => !IsBusy;

    private async Task RunUnlockAsync(Func<Task> unlock)
    {
        IsBusy = true;
        Error = null;
        IsFileDamaged = false;
        try
        {
            await unlock();
            _settings.LastVaultPath = VaultPath;
            _settings.Save();
            _navigation.ShowVault();
        }
        catch (HelloNotCompletedException ex)
        {
            Error = ex.Outcome switch
            {
                HelloOutcome.Cancelled => null,
                HelloOutcome.NotSetUp => "Windows Hello isn't available right now. Use your master password.",
                _ => "Windows Hello didn't work. Use your master password.",
            };
        }
        catch (InvalidMasterPasswordException)
        {
            Error = "Wrong master password.";
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            Error = "The vault file can't be found. It may have been moved or deleted.";
        }
        catch (HelloKeyChangedException)
        {
            CanUseHello = false;
            Error = "Windows Hello unlock stopped working (for example after resetting your PIN) and was turned off. " +
                    "Unlock with your master password, then turn it on again in Settings.";
        }
        catch (Exception ex) when (ex is CryptographicException or InvalidDataException or JsonException)
        {
            Error = $"The vault file is damaged or has been modified. ({ex.Message})";
            IsFileDamaged = true;
        }
        catch (IOException ex)
        {
            Error = $"The vault file can't be read. ({ex.Message})";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void RestoreFromBackup()
    {
        if (_dialogs.PickBackupToRestore() is not { } backup) return;
        if (!_dialogs.Confirm(
                $"Replace {VaultName} with this backup?\n\n{Path.GetFileName(backup)}\n\n" +
                "The current file is kept in the backups folder. Changes made after the backup was taken will be missing, " +
                "and you'll need the master password that was in use at that time.",
                "Restore backup"))
            return;

        try
        {
            VaultBackups.Restore(backup, VaultPath, VaultBackups.DamagedDirectory);
            Error = null;
            IsFileDamaged = false;
            _dialogs.ShowInfo("Backup restored. Unlock it with your master password.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            _dialogs.ShowError($"That backup couldn't be restored.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenOtherVault()
    {
        if (_dialogs.PickVaultToOpen() is { } path) _navigation.ShowUnlock(path);
    }

    [RelayCommand]
    private void CreateVault() => _navigation.ShowCreate();

    private sealed class HelloNotCompletedException(HelloOutcome outcome) : Exception
    {
        public HelloOutcome Outcome { get; } = outcome;
    }
}
