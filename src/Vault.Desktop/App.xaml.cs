using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using Vault.Desktop.Browser;
using Vault.Desktop.Services;
using Vault.Desktop.ViewModels;
using Vault.Desktop.Views;

namespace Vault.Desktop;

public partial class App : Application
{
    private const uint HotkeyKey = 0x50; // Ctrl+Alt+P

    private SingleInstance? _instance;
    private AppSettings _settings = null!;
    private VaultSession? _session;
    private ClipboardService? _clipboard;
    private AutoLockService? _autoLock;
    private TrayIcon? _tray;
    private GlobalHotkey? _hotkey;
    private BrowserBridge? _bridge;
    private readonly PairingStore _pairings = new();
    private bool _exiting;

    public PairingStore Pairings => _pairings;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instance = new SingleInstance();
        if (!_instance.IsFirstInstance)
        {
            _instance.SignalFirstInstance();
            Shutdown();
            return;
        }
        _instance.ListenForShowRequests(() => Dispatcher.BeginInvoke(ShowMainWindow));

        base.OnStartup(e);

        _settings = AppSettings.Load();
        _session = new VaultSession();
        _clipboard = new ClipboardService { ClearAfter = TimeSpan.FromSeconds(_settings.ClipboardClearSeconds) };
        var dialogs = new DialogService(_clipboard);

        _autoLock = new AutoLockService(TimeSpan.FromMinutes(_settings.AutoLockMinutes));
        _autoLock.LockRequested += (_, _) => _session.Lock();
        _session.Locked += (_, _) => _clipboard.ClearIfOurs();

        var window = new MainWindow { DataContext = new MainViewModel(_session, _settings, _clipboard, dialogs, _autoLock, _pairings) };
        window.Closing += OnMainWindowClosing;
        window.StateChanged += (_, _) =>
        {
            if (window.WindowState == WindowState.Minimized && _settings.LockOnMinimize) _session.Lock();
        };
        MainWindow = window;
        window.Show();

        _tray = new TrayIcon(ShowMainWindow, () => _session.Lock(), ExitApp);
        _hotkey = new GlobalHotkey(new WindowInteropHelper(window).Handle, HotkeyKey, control: true, alt: true, shift: false, ShowMainWindow);

        StartBrowserIntegration();
    }

    private void StartBrowserIntegration()
    {
        try
        {
            NativeHostRegistration.Register();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // The browser extension won't connect, but the app itself works fine.
        }

        var handler = new BrowserRequestHandler(_session!, _pairings, new BrowserUi(this));
        _bridge = new BrowserBridge(handler, Dispatcher, NativeHostRegistration.HostPath);
        _bridge.Start();
    }

    /// <summary>The browser link's window to the UI.</summary>
    private sealed class BrowserUi(App app) : IBrowserUi
    {
        public void ShowApp() => app.ShowMainWindow();

        public Task<bool> ConfirmPairingAsync(string browserName, string code)
        {
            var window = new PairingWindow(browserName, code);
            window.Show();
            window.Activate();
            return window.Decision;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _bridge?.Dispose();
        _hotkey?.Dispose();
        _tray?.Dispose();
        _autoLock?.Dispose();
        _session?.Lock();
        _clipboard?.ClearIfOurs();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting || !_settings.CloseToTray) return;

        e.Cancel = true;
        MainWindow.Hide();
        if (!_settings.HasShownTrayHint)
        {
            _tray?.ShowHint("Password Manager is still running",
                "Open it from here or with Ctrl+Alt+P. Right-click the icon to exit.");
            _settings.HasShownTrayHint = true;
            _settings.Save();
        }
    }

    private void ShowMainWindow()
    {
        var window = (MainWindow)MainWindow;
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        window.FocusPrimaryInput();
    }

    private void ExitApp()
    {
        _exiting = true;
        MainWindow.Close();
    }
}
