using System.Windows;
using Vault.Desktop.Services;
using Vault.Desktop.ViewModels;

namespace Vault.Desktop;

public partial class App : Application
{
    private VaultSession? _session;
    private ClipboardService? _clipboard;
    private AutoLockService? _autoLock;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = AppSettings.Load();
        _session = new VaultSession();
        _clipboard = new ClipboardService { ClearAfter = TimeSpan.FromSeconds(settings.ClipboardClearSeconds) };
        var dialogs = new DialogService(_clipboard);

        _autoLock = new AutoLockService(TimeSpan.FromMinutes(settings.AutoLockMinutes));
        _autoLock.LockRequested += (_, _) => _session.Lock();
        _session.Locked += (_, _) => _clipboard.ClearIfOurs();

        MainWindow = new MainWindow { DataContext = new MainViewModel(_session, settings, _clipboard, dialogs) };
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _autoLock?.Dispose();
        _session?.Lock();
        _clipboard?.ClearIfOurs();
        base.OnExit(e);
    }
}
