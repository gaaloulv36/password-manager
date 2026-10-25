using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VaultForge.Desktop.Services;

/// <summary>
/// Gives every window the Forge Studio title bar: Charcoal background, Ash text, a hairline border and dark
/// caption buttons, instead of the user's Windows accent colour. Windows 11 only; older versions ignore it.
/// </summary>
public static class BrandWindowChrome
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    // COLORREF is 0x00BBGGRR.
    private const int Charcoal = 0x001C1715; // #15171C
    private const int Ash = 0x00ECF1F4;      // #F4F1EC
    private const int Line = 0x00372E2A;     // #2A2E37

    public static void Install() =>
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => Apply((Window)sender)));

    private static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        Set(handle, DwmwaUseImmersiveDarkMode, 1);
        Set(handle, DwmwaCaptionColor, Charcoal);
        Set(handle, DwmwaTextColor, Ash);
        Set(handle, DwmwaBorderColor, Line);
    }

    private static void Set(IntPtr handle, int attribute, int value) => DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
