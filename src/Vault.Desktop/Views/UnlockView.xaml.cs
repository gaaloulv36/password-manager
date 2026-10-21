using System.Windows;
using System.Windows.Controls;
using Vault.Desktop.ViewModels;

namespace Vault.Desktop.Views;

public partial class UnlockView : UserControl
{
    public UnlockView()
    {
        InitializeComponent();
        Loaded += (_, _) => PasswordInput.Focus();
    }

    // PasswordBox.Password isn't bindable (by design), so pass it on by hand.
    private void OnPasswordChanged(object sender, RoutedEventArgs e) =>
        ((UnlockViewModel)DataContext).Password = PasswordInput.Password;
}
