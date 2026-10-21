using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Vault.Core.Crypto;
using Windows.Security.Credentials;
using Windows.Security.Cryptography;

namespace Vault.Desktop.Services;

public enum HelloOutcome { Success, Cancelled, NotSetUp, Failed }

/// <summary>
/// Windows Hello unlock. A TPM-backed key (KeyCredentialManager) signs a random challenge after PIN/face/fingerprint;
/// the signature (RSA PKCS#1 v1.5, so deterministic) is the secret that wraps the vault key. The wrapped key lives
/// in %LOCALAPPDATA% and is useless without this PC's TPM and the user's Hello gesture.
/// </summary>
public sealed class WindowsHelloService
{
    private static readonly string Directory_ = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PasswordManager", "QuickUnlock");

    public static async Task<bool> IsAvailableAsync()
    {
        try
        {
            return await KeyCredentialManager.IsSupportedAsync();
        }
        catch (COMException)
        {
            return false;
        }
    }

    public bool IsEnabled(Guid vaultId) => File.Exists(BlobPath(vaultId));

    public async Task<HelloOutcome> EnableAsync(Guid vaultId, byte[] vaultKey)
    {
        using var focus = BringPromptToFront();
        var created = await KeyCredentialManager.RequestCreateAsync(KeyName(vaultId), KeyCredentialCreationOption.ReplaceExisting);
        if (created.Status != KeyCredentialStatus.Success) return Map(created.Status);

        var challenge = QuickUnlockBlob.NewChallenge();
        var (outcome, signature) = await SignAsync(created.Credential, challenge);
        if (outcome != HelloOutcome.Success) return outcome;

        var blob = QuickUnlockBlob.Create(vaultId, challenge, signature!, vaultKey);
        Directory.CreateDirectory(Directory_);
        File.WriteAllText(BlobPath(vaultId), JsonSerializer.Serialize(blob));
        return HelloOutcome.Success;
    }

    /// <summary>Returns the vault key, or null with the reason (cancelled, Hello removed, …).</summary>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The Hello key no longer matches (e.g. it was reset).</exception>
    public async Task<(HelloOutcome Outcome, byte[]? VaultKey)> UnlockAsync(Guid vaultId)
    {
        if (!File.Exists(BlobPath(vaultId))) return (HelloOutcome.NotSetUp, null);
        var blob = JsonSerializer.Deserialize<QuickUnlockBlob>(File.ReadAllText(BlobPath(vaultId)))
            ?? throw new InvalidDataException("Quick unlock data is empty.");

        using var focus = BringPromptToFront();
        var opened = await KeyCredentialManager.OpenAsync(KeyName(vaultId));
        if (opened.Status != KeyCredentialStatus.Success) return (Map(opened.Status), null);

        var (outcome, signature) = await SignAsync(opened.Credential, blob.Challenge);
        return outcome == HelloOutcome.Success ? (outcome, blob.UnwrapVaultKey(signature!)) : (outcome, null);
    }

    public async Task DisableAsync(Guid vaultId)
    {
        File.Delete(BlobPath(vaultId));
        try
        {
            await KeyCredentialManager.DeleteAsync(KeyName(vaultId));
        }
        catch (Exception ex) when (ex is COMException or FileNotFoundException)
        {
            // Already gone.
        }
    }

    private static async Task<(HelloOutcome, byte[]?)> SignAsync(KeyCredential credential, byte[] challenge)
    {
        var result = await credential.RequestSignAsync(CryptographicBuffer.CreateFromByteArray(challenge));
        if (result.Status != KeyCredentialStatus.Success) return (Map(result.Status), null);
        CryptographicBuffer.CopyToByteArray(result.Result, out var signature);
        return (HelloOutcome.Success, signature);
    }

    private static HelloOutcome Map(KeyCredentialStatus status) => status switch
    {
        KeyCredentialStatus.Success => HelloOutcome.Success,
        KeyCredentialStatus.UserCanceled or KeyCredentialStatus.UserPrefersPassword => HelloOutcome.Cancelled,
        KeyCredentialStatus.NotFound or KeyCredentialStatus.SecurityDeviceLocked => HelloOutcome.NotSetUp,
        _ => HelloOutcome.Failed,
    };

    private static string KeyName(Guid vaultId) => $"PasswordManager-{vaultId:N}";
    private static string BlobPath(Guid vaultId) => Path.Combine(Directory_, $"{vaultId:N}.json");

    /// <summary>
    /// Desktop apps can't parent the Hello prompt, and Windows often opens it behind the app.
    /// Bring it forward while it's showing.
    /// </summary>
    private static CancellationTokenSource BringPromptToFront()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                var prompt = FindWindow("Credential Dialog Xaml Host", null);
                if (prompt != IntPtr.Zero)
                {
                    SetForegroundWindow(prompt);
                    return;
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
        });
        return cts;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
