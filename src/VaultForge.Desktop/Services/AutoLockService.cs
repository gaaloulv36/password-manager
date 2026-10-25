using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;

namespace VaultForge.Desktop.Services;

/// <summary>
/// Requests a lock when the PC has been idle too long (no keyboard/mouse input anywhere, not just in
/// this app — so the vault stays available to the browser while you're actively using the PC),
/// when Windows is locked, or when the PC goes to sleep.
/// </summary>
public sealed class AutoLockService : IDisposable
{
    private readonly DispatcherTimer _idleTimer;
    private readonly Dispatcher _dispatcher;

    /// <summary>Always raised on the UI thread.</summary>
    public event EventHandler? LockRequested;

    public TimeSpan IdleLimit { get; set; }

    public AutoLockService(TimeSpan idleLimit)
    {
        IdleLimit = idleLimit;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _idleTimer.Tick += (_, _) =>
        {
            if (GetSystemIdleTime() >= IdleLimit) RaiseLockRequested();
        };
        _idleTimer.Start();

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public void Dispose()
    {
        _idleTimer.Stop();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            RaiseLockRequested();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) RaiseLockRequested();
    }

    // SystemEvents fire on a background thread.
    private void RaiseLockRequested() => _dispatcher.Invoke(() => LockRequested?.Invoke(this, EventArgs.Empty));

    /// <summary>Time since the last keyboard/mouse input anywhere on the PC.</summary>
    public static TimeSpan GetSystemIdleTime()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
}
