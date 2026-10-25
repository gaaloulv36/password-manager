using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VaultForge.Desktop.Services;

namespace VaultForge.Desktop.ViewModels;

/// <summary>First-run page: create a new vault or open an existing file (e.g. from another PC).</summary>
public sealed partial class WelcomeViewModel(MainViewModel navigation, DialogService dialogs) : ObservableObject
{
    [RelayCommand]
    private void CreateVault() => navigation.ShowCreate();

    [RelayCommand]
    private void OpenVault()
    {
        if (dialogs.PickVaultToOpen() is { } path) navigation.ShowUnlock(path);
    }
}
