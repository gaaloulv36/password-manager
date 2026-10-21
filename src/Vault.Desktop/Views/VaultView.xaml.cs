using System.Windows.Controls;
using System.Windows.Input;

namespace Vault.Desktop.Views;

public partial class VaultView : UserControl
{
    public VaultView()
    {
        InitializeComponent();
        Loaded += (_, _) => SearchInput.Focus();

        var focusSearch = new RoutedCommand();
        CommandBindings.Add(new CommandBinding(focusSearch, (_, _) => SearchInput.Focus()));
        InputBindings.Add(new KeyBinding(focusSearch, Key.F, ModifierKeys.Control));
    }
}
