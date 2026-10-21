using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;
using Vault.Core;
using Vault.Core.Storage;

namespace Vault.Desktop.Services;

/// <summary>
/// The currently open vault file and, while unlocked, its decrypted session. Watches the file so
/// changes synced in from another PC are merged in as soon as they arrive.
/// </summary>
public sealed class VaultSession
{
    private const int MaxReadRetries = 5;
    private static readonly string ConflictArchive = System.IO.Path.Combine(VaultBackups.DefaultDirectory, "Merged conflict copies");

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _debounce;
    private FileSystemWatcher? _watcher;
    private byte[]? _lastKnownFileHash;
    private int _readRetries;

    public VaultSession()
    {
        // Sync clients write in bursts; wait for the file to settle before reading it.
        _debounce = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            SyncFromDisk();
        };
    }

    public string? Path { get; private set; }
    public UnlockedVault? Vault { get; private set; }
    public bool IsUnlocked => Vault is not null;

    public event EventHandler? Locked;

    /// <summary>Changes from another PC (or a conflict copy) were merged in. Raised on the UI thread.</summary>
    public event EventHandler<SyncResult>? ExternalChangesMerged;

    /// <summary>The file changed on disk but couldn't be merged. Raised on the UI thread.</summary>
    public event EventHandler<string>? SyncFailed;

    /// <summary>Slow (Argon2id) — call off the UI thread.</summary>
    public void Create(string path, string masterPassword)
    {
        var vault = UnlockedVault.Create(masterPassword);
        VaultFileStore.Save(vault.Document, path);
        Open(path, vault);
    }

    /// <summary>Slow (Argon2id) — call off the UI thread.</summary>
    public void Unlock(string path, string masterPassword)
    {
        Open(path, UnlockedVault.Unlock(VaultFileStore.Load(path), masterPassword));

        try
        {
            VaultBackups.CreateDailyBackup(path, VaultBackups.DefaultDirectory, DateOnly.FromDateTime(DateTime.Now));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A missed backup must never block unlocking.
        }
    }

    /// <summary>Checks a master password without touching the open session. Slow — call off the UI thread.</summary>
    public bool VerifyMasterPassword(string masterPassword)
    {
        try
        {
            using var check = UnlockedVault.Unlock(Vault!.Document, masterPassword);
            return true;
        }
        catch (InvalidMasterPasswordException)
        {
            return false;
        }
    }

    /// <summary>Saves a local change, merging in whatever is on disk first so nothing from another PC is lost.</summary>
    public SyncResult Save()
    {
        var result = VaultSync.SyncWithDisk(Vault ?? throw new InvalidOperationException("The vault is locked."), Path!, ConflictArchive, forceWrite: true);
        _lastKnownFileHash = HashFile(Path!);
        return result;
    }

    public void Lock()
    {
        if (Vault is null) return;
        StopWatching();
        Vault.Dispose();
        Vault = null;
        Locked?.Invoke(this, EventArgs.Empty);
    }

    private void Open(string path, UnlockedVault vault)
    {
        StopWatching();
        Vault?.Dispose();
        Path = path;
        Vault = vault;
        _lastKnownFileHash = HashFile(path);
        StartWatching(path);

        // Pick up conflict copies left behind while the app wasn't running.
        _dispatcher.BeginInvoke(SyncFromDisk);
    }

    private void SyncFromDisk()
    {
        if (Vault is null || Path is null) return;

        try
        {
            var hash = HashFile(Path);
            var unchanged = hash is not null && _lastKnownFileHash is not null && hash.AsSpan().SequenceEqual(_lastKnownFileHash);
            if (unchanged && VaultSync.FindConflictCopies(Path).Count == 0) return; // e.g. our own save

            var result = VaultSync.SyncWithDisk(Vault, Path, ConflictArchive, forceWrite: false);
            _lastKnownFileHash = HashFile(Path);
            _readRetries = 0;
            if (result.HasChanges) ExternalChangesMerged?.Invoke(this, result);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            // Most likely the sync client is still writing the file: try again shortly.
            if (++_readRetries <= MaxReadRetries)
            {
                _debounce.Start();
                return;
            }
            _readRetries = 0;
            SyncFailed?.Invoke(this, $"Changes to the vault file couldn't be read. ({ex.Message})");
        }
        catch (Exception ex) when (ex is CryptographicException or InvalidOperationException)
        {
            SyncFailed?.Invoke(this,
                "The vault file on disk was modified by something other than this app, or replaced by a different vault, " +
                $"so it was not merged. Your open vault is unchanged. ({ex.Message})");
        }
    }

    private void StartWatching(string path)
    {
        _watcher = new FileSystemWatcher(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!, "*.pmvault")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        FileSystemEventHandler onChange = (_, _) => _dispatcher.BeginInvoke(() =>
        {
            _debounce.Stop();
            _debounce.Start();
        });
        _watcher.Changed += onChange;
        _watcher.Created += onChange;
        _watcher.Renamed += (sender, e) => onChange(sender, e);
        _watcher.EnableRaisingEvents = true;
    }

    private void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
        _dispatcher.BeginInvoke(_debounce.Stop);
    }

    private static byte[]? HashFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return SHA256.HashData(stream);
        }
        catch (IOException)
        {
            return null;
        }
    }
}
