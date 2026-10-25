using System.Windows;
using VaultForge.Desktop.ViewModels;

namespace VaultForge.Desktop.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is SettingsViewModel viewModel)
            {
                viewModel.PasswordFieldsCleared += (_, _) => ClearPasswordFields();
                _ = viewModel.LoadHelloStateAsync();
            }
        };
    }

    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

    // PasswordBox.Password isn't bindable (by design), so pass it on by hand.
    private void OnCurrentChanged(object sender, RoutedEventArgs e) => ViewModel.CurrentPassword = CurrentInput.Password;
    private void OnNewChanged(object sender, RoutedEventArgs e) => ViewModel.NewPassword = NewInput.Password;
    private void OnConfirmChanged(object sender, RoutedEventArgs e) => ViewModel.ConfirmNewPassword = ConfirmInput.Password;
    private void OnExportChanged(object sender, RoutedEventArgs e) => ViewModel.ExportPassword = ExportInput.Password;

    private void ClearPasswordFields()
    {
        CurrentInput.Clear();
        NewInput.Clear();
        ConfirmInput.Clear();
        ExportInput.Clear();
    }
}
