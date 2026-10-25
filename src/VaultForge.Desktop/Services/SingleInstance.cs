namespace VaultForge.Desktop.Services;

/// <summary>
/// Only one copy of the app may hold the vault open. Starting it again just brings the running
/// copy to the front (e.g. when it's hidden in the tray).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string Name = "VaultForge-7f3c2a";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showSignal;
    private RegisteredWaitHandle? _registration;

    public bool IsFirstInstance { get; }

    public SingleInstance()
    {
        // "Local\" scopes both objects to the current Windows session (i.e. this user).
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{Name}", out var createdNew);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{Name}-show");
        IsFirstInstance = createdNew;
    }

    /// <summary>First instance: run <paramref name="onShowRequested"/> (on a pool thread) whenever another launch asks.</summary>
    public SingleInstance ListenForShowRequests(Action onShowRequested)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, _) => onShowRequested(), null, Timeout.Infinite, executeOnlyOnce: false);
        return this;
    }

    /// <summary>Second instance: ask the first one to show itself.</summary>
    public void SignalFirstInstance() => _showSignal.Set();

    public void Dispose()
    {
        _registration?.Unregister(null);
        if (IsFirstInstance) _mutex.ReleaseMutex();
        _mutex.Dispose();
        _showSignal.Dispose();
    }
}
