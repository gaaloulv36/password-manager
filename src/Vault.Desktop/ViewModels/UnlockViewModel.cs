using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vault.Core;
using Vault.Desktop.Services;

namespace Vault.Desktop.ViewModels;

public sealed partial class UnlockViewModel(
    MainViewModel navigation, VaultSession session, AppSettings settings, DialogService dialogs, string vaultPath) : ObservableObject
{
    public string VaultPath { get; } = vaultPath;
    public string VaultName { get; } = Path.GetFileNameWithoutExtension(vaultPath);

    /// <summary>Set from the view's PasswordBox (which can't be data-bound).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private string _password = "";

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private bool _isBusy;

    private bool CanUnlock() => Password.Length > 0 && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockAsync()
    {
        IsBusy = true;
        Error = null;
        try
        {
            var password = Password;
            await Task.Run(() => session.Unlock(VaultPath, password));
            settings.LastVaultPath = VaultPath;
            settings.Save();
            navigation.ShowVault();
        }
        catch (InvalidMasterPasswordException)
        {
            Error = "Wrong master password.";
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            Error = "The vault file can't be found. It may have been moved or deleted.";
        }
        catch (Exception ex) when (ex is CryptographicException or InvalidDataException or JsonException)
        {
            Error = $"The vault file is damaged or has been modified. ({ex.Message})";
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
    private void OpenOtherVault()
    {
        if (dialogs.PickVaultToOpen() is { } path) navigation.ShowUnlock(path);
    }

    [RelayCommand]
    private void CreateVault() => navigation.ShowCreate();

    partial void OnIsBusyChanged(bool value) => UnlockCommand.NotifyCanExecuteChanged();
}
