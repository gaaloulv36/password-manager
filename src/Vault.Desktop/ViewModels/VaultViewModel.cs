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
using Vault.Core.Import;
using Vault.Core.Models;
using Vault.Core.Otp;
using Vault.Core.Storage;
using Vault.Desktop.Services;

namespace Vault.Desktop.ViewModels;

/// <summary>The unlocked vault: searchable list on the left, details or editor on the right.</summary>
public sealed partial class VaultViewModel : ObservableObject, IDisposable
{
    private readonly VaultSession _session;
    private readonly ClipboardService _clipboard;
    private readonly DialogService _dialogs;
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    public VaultViewModel(VaultSession session, ClipboardService clipboard, DialogService dialogs)
    {
        _session = session;
        _clipboard = clipboard;
        _dialogs = dialogs;

        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = o => Matches((VaultItem)o);
        ItemsView.SortDescriptions.Add(new SortDescription(nameof(VaultItem.Title), ListSortDirection.Ascending));

        _ticker.Tick += (_, _) => UpdateTotp();
        _ticker.Start();
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); StatusMessage = null; };

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

    public void Dispose()
    {
        _ticker.Stop();
        _statusTimer.Stop();
    }

    private void SaveItem(VaultItem item)
    {
        _session.Vault!.Upsert(item);
        Editor = null;
        Persist(item.Id);
        ShowStatus("Saved");
    }

    private void CloseEditor() => Editor = null;

    /// <summary>Writes the vault to disk, merging in anything another PC saved in the meantime.</summary>
    private void Persist(Guid? selectId = null)
    {
        try
        {
            var result = _session.Save();
            if (result?.HasChanges == true) ShowMergeStatus(result);
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

    private void ShowMergeStatus(MergeResult result)
    {
        var parts = new List<string>();
        if (result.Added > 0) parts.Add($"{result.Added} added");
        if (result.Updated > 0) parts.Add($"{result.Updated} updated");
        if (result.Deleted > 0) parts.Add($"{result.Deleted} deleted");
        if (parts.Count > 0) ShowStatus($"Merged changes from another PC: {string.Join(", ", parts)}");
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
