using System.Windows;
using System.Windows.Media;
using VaultForge.Desktop.Views;

namespace VaultForge.Desktop;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    /// <summary>Puts the cursor where the user will type next (password on the lock screen, search in the vault).</summary>
    public void FocusPrimaryInput()
    {
        var pending = new Queue<DependencyObject>([this]);
        while (pending.TryDequeue(out var node))
        {
            if (node is IHasPrimaryInput page)
            {
                page.FocusPrimaryInput();
                return;
            }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                pending.Enqueue(VisualTreeHelper.GetChild(node, i));
        }
    }
}
