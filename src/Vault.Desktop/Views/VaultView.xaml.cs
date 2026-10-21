using System.Windows.Controls;
using System.Windows.Input;

namespace Vault.Desktop.Views;

public partial class VaultView : UserControl, IHasPrimaryInput
{
    public VaultView()
    {
        InitializeComponent();
        Loaded += (_, _) => FocusPrimaryInput();

        var focusSearch = new RoutedCommand();
        CommandBindings.Add(new CommandBinding(focusSearch, (_, _) => FocusPrimaryInput()));
        InputBindings.Add(new KeyBinding(focusSearch, Key.F, ModifierKeys.Control));
    }

    public void FocusPrimaryInput()
    {
        SearchInput.Focus();
        SearchInput.SelectAll();
    }
}
