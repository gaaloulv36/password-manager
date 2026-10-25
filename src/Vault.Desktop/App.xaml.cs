using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Threading;
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
    private readonly UpdateService _updates = new();
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

        // WPF formats bound dates/numbers as en-US unless told otherwise; use the Windows locale.
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

        _settings = AppSettings.Load();
        ScreenCaptureProtection.Install(_settings.ProtectFromScreenCapture);
        _session = new VaultSession();
        _clipboard = new ClipboardService { ClearAfter = TimeSpan.FromSeconds(_settings.ClipboardClearSeconds) };
        var dialogs = new DialogService(_clipboard);

        _autoLock = new AutoLockService(TimeSpan.FromMinutes(_settings.AutoLockMinutes));
        _autoLock.LockRequested += (_, _) => _session.Lock();
        _session.Locked += (_, _) => _clipboard.ClearIfOurs();

        var window = new MainWindow { DataContext = new MainViewModel(_session, _settings, _clipboard, dialogs, _autoLock, _pairings, _updates) };
        window.Closing += OnMainWindowClosing;
        window.StateChanged += (_, _) =>
        {
            if (window.WindowState == WindowState.Minimized && _settings.LockOnMinimize) _session.Lock();
        };
        MainWindow = window;

        // Started with Windows: stay in the tray until summoned.
        if (e.Args.Contains(StartupRegistration.MinimizedArgument)) new WindowInteropHelper(window).EnsureHandle();
        else window.Show();

        _tray = new TrayIcon(ShowMainWindow, () => _session.Lock(), ExitApp);
        _hotkey = new GlobalHotkey(new WindowInteropHelper(window).Handle, HotkeyKey, control: true, alt: true, shift: false, ShowMainWindow);

        StartBrowserIntegration();
        StartUpdateSchedule();
    }

    // ---- Updates --------------------------------------------------------------------------------

    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan AwayBeforeAutoInstall = TimeSpan.FromMinutes(10);

    private DispatcherTimer? _updateTimer;
    private DateTime _nextUpdateCheck;
    private bool _checkingForUpdate;
    private bool _preparedForExit;

    /// <summary>
    /// The app lives in the tray for days, so it checks every few hours rather than only at startup. A downloaded
    /// update is announced (tray notification, menu item, bar in the window) and installed on "Restart", on exit,
    /// or by itself once the vault is locked and nobody has touched the PC for a while.
    /// </summary>
    private void StartUpdateSchedule()
    {
        if (!_updates.IsInstalled) return;

        _updates.UpdateReady += (_, version) =>
        {
            ((MainViewModel)MainWindow.DataContext).UpdateReadyVersion = version;
            _tray?.ShowUpdateAvailable(version, () => _updates.RestartToUpdate());
            _tray?.ShowHint("Update ready", $"Password Manager {version} is ready. Click here to restart and install it.",
                () => _updates.RestartToUpdate());
        };
        _updates.BeforeRestart += (_, _) => PrepareForExit();

        _nextUpdateCheck = DateTime.UtcNow.AddSeconds(15);
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _updateTimer.Tick += async (_, _) => await UpdateTickAsync();
        _updateTimer.Start();
    }

    private async Task UpdateTickAsync()
    {
        if (_updates.ReadyVersion is not null)
        {
            // Nothing to interrupt: vault locked, PC idle, no window waiting for an answer.
            if (_settings.InstallUpdatesWhenAway && _session is { IsUnlocked: false } && !HasOpenDialogs()
                && AutoLockService.GetSystemIdleTime() >= AwayBeforeAutoInstall)
                _updates.RestartToUpdate(minimized: true);
            return;
        }

        if (!_settings.CheckForUpdates || _checkingForUpdate || DateTime.UtcNow < _nextUpdateCheck) return;
        _checkingForUpdate = true;
        try
        {
            await _updates.CheckAndDownloadAsync();
            _nextUpdateCheck = DateTime.UtcNow + CheckInterval;
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or IOException or TaskCanceledException or InvalidOperationException)
        {
            _nextUpdateCheck = DateTime.UtcNow + RetryInterval; // offline or GitHub unavailable
        }
        finally
        {
            _checkingForUpdate = false;
        }
    }

    private bool HasOpenDialogs() => Windows.OfType<Window>().Any(w => w != MainWindow && w.IsVisible);

    /// <summary>Shared by normal exit and update restarts (which end the process without running OnExit).</summary>
    private void PrepareForExit()
    {
        if (_preparedForExit) return; // an update restart that failed still exits normally later
        _preparedForExit = true;
        _updateTimer?.Stop();
        _bridge?.Dispose();
        _hotkey?.Dispose();
        _tray?.Dispose();
        _tray = null;
        _autoLock?.Dispose();
        _session?.Lock();
        _clipboard?.ClearIfOurs();
        _instance?.Dispose();
        _instance = null;
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
        private PairingWindow? _open;

        public void ShowApp() => app.ShowMainWindow();

        /// <summary>One prompt at a time: anything else asking to pair meanwhile is rejected, not stacked up.</summary>
        public Task<bool> ConfirmPairingAsync(string browserName, string code)
        {
            if (_open is not null) return Task.FromResult(false);

            var window = new PairingWindow(browserName, code);
            _open = window;
            window.Closed += (_, _) => _open = null;
            window.Show();
            window.Activate();
            return window.Decision;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        PrepareForExit();
        _updates.ApplyOnExit();
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
