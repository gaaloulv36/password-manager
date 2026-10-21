using System.Windows;
using System.Windows.Controls;
using Vault.Desktop.ViewModels;

namespace Vault.Desktop.Views;

public partial class CreateVaultView : UserControl
{
    public CreateVaultView()
    {
        InitializeComponent();
        Loaded += (_, _) => PasswordInput.Focus();
    }

    private CreateVaultViewModel ViewModel => (CreateVaultViewModel)DataContext;

    // PasswordBox.Password isn't bindable (by design), so pass it on by hand.
    private void OnPasswordChanged(object sender, RoutedEventArgs e) => ViewModel.Password = PasswordInput.Password;

    private void OnConfirmChanged(object sender, RoutedEventArgs e) => ViewModel.ConfirmPassword = ConfirmInput.Password;
}
