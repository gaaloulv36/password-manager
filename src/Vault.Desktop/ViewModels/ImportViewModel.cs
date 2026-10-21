using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vault.Core.Import;
using Vault.Core.Models;

namespace Vault.Desktop.ViewModels;

public sealed record ImportRow(VaultItem Item, bool IsDuplicate)
{
    public string Title => Item.Title;
    public string? Username => Item.Username;
    public string? Site => Item.Urls.Select(VaultImporter.HostOf).FirstOrDefault(h => h is not null);
    public string Status => IsDuplicate ? "Already in vault" : "New";
}

/// <summary>Preview of an export file before anything is added to the vault.</summary>
public sealed partial class ImportViewModel : ObservableObject
{
    public ImportViewModel(string filePath, ImportResult result, IReadOnlyList<VaultItem> existing)
    {
        FileName = Path.GetFileName(filePath);
        Format = result.Format;
        SkippedCount = result.SkippedCount;
        Rows = result.Items.Select(i => new ImportRow(i, existing.Any(e => VaultImporter.IsDuplicate(i, e)))).ToList();
    }

    public event EventHandler? Confirmed;

    public string FileName { get; }
    public string Format { get; }
    public int SkippedCount { get; }
    public IReadOnlyList<ImportRow> Rows { get; }
    public int DuplicateCount => Rows.Count(r => r.IsDuplicate);

    public string Summary =>
        $"{Rows.Count} entries found in this {Format} export: {Rows.Count - DuplicateCount} new, {DuplicateCount} already in your vault."
        + (SkippedCount > 0 ? $" {SkippedCount} empty or unsupported rows (e.g. cards, recycle bin) were left out." : "");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportCount), nameof(ImportButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private bool _includeDuplicates;

    /// <summary>The export holds every password in plain text; deleting it is the safe default.</summary>
    [ObservableProperty]
    private bool _deleteFileAfterImport = true;

    public IEnumerable<VaultItem> ItemsToImport => Rows.Where(r => IncludeDuplicates || !r.IsDuplicate).Select(r => r.Item);
    public int ImportCount => ItemsToImport.Count();
    public string ImportButtonText => ImportCount == 1 ? "Import 1 entry" : $"Import {ImportCount} entries";

    private bool CanImport() => ImportCount > 0;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private void Import() => Confirmed?.Invoke(this, EventArgs.Empty);
}
