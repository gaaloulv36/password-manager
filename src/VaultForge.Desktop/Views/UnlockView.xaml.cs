using System.Windows;
using System.Windows.Controls;
using VaultForge.Desktop.ViewModels;

namespace VaultForge.Desktop.Views;

public partial class UnlockView : UserControl, IHasPrimaryInput
{
    public UnlockView()
    {
        InitializeComponent();
        Loaded += (_, _) => FocusPrimaryInput();
    }

    /// <summary>With Windows Hello on, Space/Enter on the focused Hello button is the quickest way in.</summary>
    public void FocusPrimaryInput()
    {
        if (((UnlockViewModel)DataContext).CanUseHello) HelloButton.Focus();
        else PasswordInput.Focus();
    }

    // PasswordBox.Password isn't bindable (by design), so pass it on by hand.
    private void OnPasswordChanged(object sender, RoutedEventArgs e) =>
        ((UnlockViewModel)DataContext).Password = PasswordInput.Password;
}
