using System.Windows;
using System.Windows.Threading;

namespace VaultForge.Desktop.Views;

/// <summary>Asks the user to approve a browser after comparing the pairing code. Times out as "rejected".</summary>
public partial class PairingWindow : Window
{
    private readonly TaskCompletionSource<bool> _decision = new();

    public PairingWindow(string browserName, string code)
    {
        InitializeComponent();
        Heading.Text = $"Connect {browserName} to VaultForge?";
        CodeText.Text = code;

        var timeout = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        timeout.Tick += (_, _) => Close();
        timeout.Start();
        Closed += (_, _) =>
        {
            timeout.Stop();
            _decision.TrySetResult(false);
        };
    }

    public Task<bool> Decision => _decision.Task;

    private void OnApprove(object sender, RoutedEventArgs e)
    {
        _decision.TrySetResult(true);
        Close();
    }

    private void OnReject(object sender, RoutedEventArgs e) => Close();
}
