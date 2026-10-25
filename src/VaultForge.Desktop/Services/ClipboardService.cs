using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace VaultForge.Desktop.Services;

/// <summary>
/// Copies secrets so they stay out of Windows clipboard history (Win+V) and cloud clipboard,
/// and clears them after a delay unless the user has copied something else since.
/// </summary>
public sealed class ClipboardService
{
    private static readonly string[] PrivacyFormats =
        ["ExcludeClipboardContentFromMonitorProcessing", "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard"];

    private readonly DispatcherTimer _clearTimer = new();

    // Only a hash of what we copied is kept, so the secret itself isn't held in memory.
    private byte[]? _copiedHash;

    public ClipboardService() => _clearTimer.Tick += (_, _) => ClearIfOurs();

    public TimeSpan ClearAfter { get; set; } = TimeSpan.FromSeconds(30);

    public void CopySecret(string text)
    {
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        foreach (var format in PrivacyFormats)
            data.SetData(format, new MemoryStream(BitConverter.GetBytes(0)));

        WithRetry(() => Clipboard.SetDataObject(data, copy: true));

        _copiedHash = Hash(text);
        _clearTimer.Stop();
        _clearTimer.Interval = ClearAfter;
        _clearTimer.Start();
    }

    public void ClearIfOurs()
    {
        _clearTimer.Stop();
        if (_copiedHash is null) return;
        try
        {
            if (Clipboard.ContainsText() && CryptographicOperations.FixedTimeEquals(Hash(Clipboard.GetText()), _copiedHash))
                WithRetry(Clipboard.Clear);
        }
        catch (COMException)
        {
            // Another app holds the clipboard open; nothing more we can do.
        }
        _copiedHash = null;
    }

    private static byte[] Hash(string text) => SHA256.HashData(Encoding.UTF8.GetBytes(text));

    /// <summary>The clipboard is a shared resource; another app may briefly have it open.</summary>
    private static void WithRetry(Action action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (COMException) when (attempt < 5)
            {
                Thread.Sleep(50);
            }
        }
    }
}
