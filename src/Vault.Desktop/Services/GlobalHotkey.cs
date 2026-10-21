using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Vault.Desktop.Services;

/// <summary>A system-wide keyboard shortcut (works while another app has focus).</summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModNoRepeat = 0x4000;
    private const int Id = 0x5057; // arbitrary, unique within this window

    private readonly HwndSource _source;
    private readonly Action _pressed;

    /// <summary>False when another app already owns the shortcut.</summary>
    public bool IsRegistered { get; }

    public GlobalHotkey(IntPtr windowHandle, uint virtualKey, bool control, bool alt, bool shift, Action pressed)
    {
        _pressed = pressed;
        _source = HwndSource.FromHwnd(windowHandle);
        _source.AddHook(WndProc);

        var modifiers = ModNoRepeat | (control ? ModControl : 0) | (alt ? ModAlt : 0) | (shift ? ModShift : 0);
        IsRegistered = RegisterHotKey(windowHandle, Id, modifiers, virtualKey);
    }

    public void Dispose()
    {
        if (IsRegistered) UnregisterHotKey(_source.Handle, Id);
        _source.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == Id)
        {
            _pressed();
            handled = true;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
