using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VaultForge.Core.Import;
using VaultForge.Core.Models;
using VaultForge.Core.Otp;
using VaultForge.Core.Storage;
using VaultForge.Desktop.Services;

namespace VaultForge.Desktop.ViewModels;

/// <summary>The unlocked vault: searchable list on the left, details or editor on the right.</summary>
public sealed partial class VaultViewModel : ObservableObject, IDisposable
{
    private readonly VaultSession _session;
    private readonly ClipboardService _clipboard;
    private readonly DialogService _dialogs;
    private readonly Func<SettingsViewModel> _createSettings;
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    public VaultViewModel(VaultSession session, ClipboardService clipboard, DialogService dialogs, Func<SettingsViewModel> createSettings)
    {
        _session = session;
        _clipboard = clipboard;
        _dialogs = dialogs;
        _createSettings = createSettings;

        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = o => Matches((VaultItem)o);
        ItemsView.SortDescriptions.Add(new SortDescription(nameof(VaultItem.Title), ListSortDirection.Ascending));

        _ticker.Tick += (_, _) => UpdateTotp();
        _ticker.Start();
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); StatusMessage = null; };

        _session.ExternalChangesMerged += OnExternalChangesMerged;
        _session.SyncFailed += OnSyncFailed;
        _session.ChangedByBrowser += OnChangedByBrowser;

        Reload();
        SelectedItem = ItemsView.Cast<VaultItem>().FirstOrDefault();
    }

    public ObservableCollection<VaultItem> Items { get; } = [];
    public ICollectionView ItemsView { get; }
    public string VaultName => Path.GetFileNameWithoutExtension(_session.Path) ?? "";
    public bool IsEmpty => Items.Count == 0;
    public bool HasSelection => SelectedItem is not null;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(MaskedPassword), nameof(LastModified))]
    [NotifyCanExecuteChangedFor(nameof(EditItemCommand), nameof(DeleteItemCommand))]
    private VaultItem? _selectedItem;

    /// <summary>Non-null while the add/edit form is shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    private ItemEditorViewModel? _editor;

    public bool IsEditing => Editor is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaskedPassword))]
    private bool _isPasswordRevealed;

    [ObservableProperty] private string? _totpCode;
    [ObservableProperty] private int _totpSecondsRemaining;
    [ObservableProperty] private string? _statusMessage;

    public string? LastModified => SelectedItem?.UpdatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string MaskedPassword => SelectedItem?.Password is not { } password ? ""
        : IsPasswordRevealed ? password
        : new string('•', Math.Min(password.Length, 16));

    partial void OnSearchTextChanged(string value)
    {
        ItemsView.Refresh();
        if (SelectedItem is null || !Matches(SelectedItem))
            SelectedItem = ItemsView.Cast<VaultItem>().FirstOrDefault();
    }

    partial void OnSelectedItemChanged(VaultItem? value)
    {
        IsPasswordRevealed = false;
        UpdateTotp();
    }

    [RelayCommand]
    private void NewItem() => Editor = new ItemEditorViewModel(null, SaveItem, CloseEditor, _dialogs);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void EditItem() => Editor = new ItemEditorViewModel(SelectedItem, SaveItem, CloseEditor, _dialogs);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteItem()
    {
        var item = SelectedItem!;
        if (!_dialogs.Confirm($"Delete \"{item.Title}\"? This can't be undone.", "Delete entry")) return;

        _session.Vault!.Delete(item.Id);
        Persist();
        ShowStatus($"Deleted \"{item.Title}\"");
    }

    [RelayCommand]
    private void CopyUsername()
    {
        if (SelectedItem?.Username is { } username)
        {
            _clipboard.CopySecret(username);
            ShowStatus("Username copied");
        }
    }

    [RelayCommand]
    private void CopyPassword()
    {
        if (SelectedItem?.Password is { } password)
        {
            _clipboard.CopySecret(password);
            ShowStatus($"Password copied — clipboard clears in {_clipboard.ClearAfter.TotalSeconds:0} s");
        }
    }

    [RelayCommand]
    private void CopyPreviousPassword(PasswordHistoryEntry entry)
    {
        _clipboard.CopySecret(entry.Password);
        ShowStatus($"Previous password copied — clipboard clears in {_clipboard.ClearAfter.TotalSeconds:0} s");
    }

    [RelayCommand]
    private void CopyTotp()
    {
        if (TotpCode is { Length: > 0 } code && char.IsDigit(code[0]))
        {
            _clipboard.CopySecret(code);
            ShowStatus("2FA code copied");
        }
    }

    [RelayCommand]
    private void ToggleReveal() => IsPasswordRevealed = !IsPasswordRevealed;

    [RelayCommand]
    private static void OpenUrl(string url)
    {
        // Only real web links: never let a stored value launch arbitrary programs.
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenGenerator() => _dialogs.ShowGenerator(allowUse: false);

    [RelayCommand]
    private void Import()
    {
        if (_dialogs.PickImportFile() is not { } path) return;

        ImportResult result;
        try
        {
            result = VaultImporter.Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError($"This file can't be imported.\n\n{ex.Message}");
            return;
        }

        var preview = new ImportViewModel(path, result, Items);
        if (!_dialogs.ShowImport(preview)) return;

        var imported = preview.ItemsToImport.ToList();
        foreach (var item in imported) _session.Vault!.Upsert(item);
        Persist();
        ShowStatus($"Imported {imported.Count} entries from {result.Format}");

        if (preview.DeleteFileAfterImport)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _dialogs.ShowError($"The import worked, but {Path.GetFileName(path)} couldn't be deleted. Please delete it yourself.\n\n{ex.Message}");
            }
        }
    }

    [RelayCommand]
    private void Lock() => _session.Lock();

    [RelayCommand]
    private void OpenHealth()
    {
        if (_dialogs.ShowHealth(new HealthViewModel(_session)) is not { } itemId || !_session.IsUnlocked) return;
        SearchText = "";
        SelectedItem = Items.FirstOrDefault(i => i.Id == itemId);
        if (SelectedItem is not null) EditItem();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        _dialogs.ShowSettings(_createSettings());
        if (_session.IsUnlocked) Reload();
    }

    public void Dispose()
    {
        _session.ExternalChangesMerged -= OnExternalChangesMerged;
        _session.SyncFailed -= OnSyncFailed;
        _session.ChangedByBrowser -= OnChangedByBrowser;
        _ticker.Stop();
        _statusTimer.Stop();
    }

    private void SaveItem(VaultItem item)
    {
        _session.Vault!.Upsert(item);
        Editor = null;
        ShowStatus("Saved"); // replaced by the merge summary if the save pulled in changes from another PC
        Persist(item.Id);
    }

    /// <summary>The file changed on disk (another PC saved) and was merged in. An open editor keeps its unsaved edits.</summary>
    private void OnExternalChangesMerged(object? sender, SyncResult result)
    {
        if (!_session.IsUnlocked) return;
        Reload();
        ShowMergeStatus(result);
    }

    private bool _showingSyncFailure;

    private void OnSyncFailed(object? sender, SyncFailure failure)
    {
        // The file may keep changing while the message is up; one dialog at a time.
        if (_showingSyncFailure || !_session.IsUnlocked) return;
        _showingSyncFailure = true;
        try
        {
            if (!failure.CanOverwrite)
            {
                _dialogs.ShowError(failure.Message);
                return;
            }

            if (!_dialogs.Confirm(failure.Message + "\n\nReplace the file on disk with your open vault? The rejected file is kept in the backups folder.",
                    "Vault file rejected"))
                return;

            _session.ReplaceDiskFileWithOpenVault();
            ShowStatus("The vault file on disk was replaced with your open vault");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError($"The vault file couldn't be replaced.\n\n{ex.Message}");
        }
        finally
        {
            _showingSyncFailure = false;
        }
    }

    private void OnChangedByBrowser(object? sender, string message)
    {
        if (!_session.IsUnlocked) return;
        Reload();
        ShowStatus(message);
    }

    private void CloseEditor() => Editor = null;

    /// <summary>Writes the vault to disk, merging in anything another PC saved in the meantime.</summary>
    private void Persist(Guid? selectId = null)
    {
        try
        {
            var result = _session.Save();
            if (result.HasChanges) ShowMergeStatus(result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError($"The vault couldn't be saved. Your change is kept in memory until the next save.\n\n{ex.Message}");
        }
        catch (Exception ex) when (ex is CryptographicException or InvalidDataException or InvalidOperationException)
        {
            _dialogs.ShowError($"The vault file on disk is damaged, was modified, or belongs to another vault, so it wasn't overwritten.\n\n{ex.Message}");
        }
        Reload(selectId);
    }

    private void Reload(Guid? selectId = null)
    {
        var id = selectId ?? SelectedItem?.Id;
        Items.Clear();
        foreach (var item in _session.Vault!.GetItems()) Items.Add(item);
        SelectedItem = Items.FirstOrDefault(i => i.Id == id) ?? ItemsView.Cast<VaultItem>().FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private bool Matches(VaultItem item)
    {
        var query = SearchText.Trim();
        if (query.Length == 0) return true;

        return Contains(item.Title) || Contains(item.Username) || Contains(item.Notes) || item.Urls.Any(Contains);
        bool Contains(string? field) => field?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
    }

    private void UpdateTotp()
    {
        if (SelectedItem?.TotpSecret is not { } secret)
        {
            TotpCode = null;
            return;
        }

        try
        {
            var parameters = TotpParameters.Parse(secret);
            var now = DateTimeOffset.UtcNow;
            TotpCode = Totp.Generate(parameters, now);
            TotpSecondsRemaining = Totp.SecondsRemaining(parameters, now);
        }
        catch (Exception ex) when (ex is FormatException or UriFormatException or ArgumentException)
        {
            TotpCode = "Invalid 2FA secret";
        }
    }

    private void ShowMergeStatus(SyncResult result)
    {
        var parts = new List<string>();
        if (result.Added > 0) parts.Add($"{result.Added} added");
        if (result.Updated > 0) parts.Add($"{result.Updated} updated");
        if (result.Deleted > 0) parts.Add($"{result.Deleted} deleted");
        if (parts.Count > 0) ShowStatus($"Synced changes from another PC: {string.Join(", ", parts)}");
        else if (result.MergedConflictCopies.Count > 0) ShowStatus("Merged a sync conflict copy of the vault");
        if (result.MasterPasswordChanged)
            _dialogs.ShowInfo("The master password was changed on another PC. Use the new password next time you unlock.");
    }

    private void ShowStatus(string message)
    {
        StatusMessage = message;
        _statusTimer.Stop();
        _statusTimer.Start();
    }
}
