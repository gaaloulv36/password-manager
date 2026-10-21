using System.IO;
using System.Windows;
using Microsoft.Win32;
using Vault.Desktop.ViewModels;
using Vault.Desktop.Views;

namespace Vault.Desktop.Services;

/// <summary>Keeps windows and dialogs out of the view models.</summary>
public sealed class DialogService(ClipboardService clipboard)
{
    private const string VaultFilter = "Password vault (*.pmvault)|*.pmvault|All files (*.*)|*.*";

    /// <summary>The active window, so dialogs opened from e.g. Settings stack on top of it rather than the main window.</summary>
    private static Window? Owner =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;

    public string? PickVaultToOpen()
    {
        var dialog = new OpenFileDialog { Title = "Open vault", Filter = VaultFilter };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? PickVaultToCreate(string suggestedPath)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Create vault",
            Filter = VaultFilter,
            DefaultExt = ".pmvault",
            InitialDirectory = Path.GetDirectoryName(suggestedPath),
            FileName = Path.GetFileName(suggestedPath),
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? PickImportFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import passwords",
            Filter = "Password exports (*.csv;*.json)|*.csv;*.json|All files (*.*)|*.*",
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? PickSaveFile(string title, string fileName, string filter)
    {
        var dialog = new SaveFileDialog { Title = title, FileName = fileName, Filter = filter, OverwritePrompt = true };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public void ShowSettings(SettingsViewModel viewModel) =>
        new SettingsWindow { DataContext = viewModel, Owner = Owner }.ShowDialog();

    public bool ShowImport(ImportViewModel viewModel)
    {
        var window = new ImportWindow { DataContext = viewModel, Owner = Owner };
        viewModel.Confirmed += (_, _) => window.DialogResult = true;
        return window.ShowDialog() == true;
    }

    public bool Confirm(string message, string title) =>
        MessageBox.Show(Owner!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public void ShowError(string message) =>
        MessageBox.Show(Owner!, message, "Password Manager", MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInfo(string message) =>
        MessageBox.Show(Owner!, message, "Password Manager", MessageBoxButton.OK, MessageBoxImage.Information);

    /// <summary>Opens the generator. Returns the chosen value when <paramref name="allowUse"/> and the user clicked Use.</summary>
    public string? ShowGenerator(bool allowUse, bool passphrase = false)
    {
        var viewModel = new GeneratorViewModel(clipboard, allowUse, passphrase);
        var window = new GeneratorWindow { DataContext = viewModel, Owner = Owner };
        viewModel.Used += (_, _) => window.DialogResult = true;
        return window.ShowDialog() == true ? viewModel.Result : null;
    }
}
