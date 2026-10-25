using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VaultForge.Desktop.Services;

/// <summary>
/// Keeps the app's windows out of screenshots, screen recordings and screen sharing (Teams, Zoom, Discord…):
/// they show up as black/empty to the capturing app. Applied to every window the app opens.
/// </summary>
public static class ScreenCaptureProtection
{
    private const uint WdaNone = 0x0;
    private const uint WdaMonitor = 0x1;            // older Windows: content shows black in captures
    private const uint WdaExcludeFromCapture = 0x11; // Windows 10 2004+: window is left out of captures entirely

    private static bool _enabled;

    /// <summary>Call once at startup, before any window is created.</summary>
    public static void Install(bool enabled)
    {
        _enabled = enabled;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => Apply((Window)sender)));
    }

    public static void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        foreach (Window window in Application.Current.Windows) Apply(window);
    }

    private static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        if (!SetWindowDisplayAffinity(handle, _enabled ? WdaExcludeFromCapture : WdaNone) && _enabled)
            SetWindowDisplayAffinity(handle, WdaMonitor);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
}
