using System.Diagnostics;
using Microsoft.Win32;

namespace Vault.Desktop.Services;

/// <summary>"Start with Windows" via the per-user Run key (no admin rights). Starts straight to the tray.</summary>
public static class StartupRegistration
{
    public const string MinimizedArgument = "--minimized";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PasswordManager";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName}\" {MinimizedArgument}");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
