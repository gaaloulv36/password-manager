using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vault.Core.Models;
using Vault.Core.Otp;
using Vault.Desktop.Services;

namespace Vault.Desktop.ViewModels;

/// <summary>Add/edit form for one entry. Hands the finished item back through <c>onSave</c>.</summary>
public sealed partial class ItemEditorViewModel : ObservableObject
{
    private readonly VaultItem? _original;
    private readonly Action<VaultItem> _onSave;
    private readonly Action _onCancel;
    private readonly DialogService _dialogs;

    public ItemEditorViewModel(VaultItem? original, Action<VaultItem> onSave, Action onCancel, DialogService dialogs)
    {
        _original = original;
        _onSave = onSave;
        _onCancel = onCancel;
        _dialogs = dialogs;

        _title = original?.Title ?? "";
        _username = original?.Username ?? "";
        _password = original?.Password ?? "";
        _urls = string.Join(Environment.NewLine, original?.Urls ?? []);
        _matchExactHost = original?.MatchExactHost ?? false;
        _notes = original?.Notes ?? "";
        _totpSecret = original?.TotpSecret ?? "";
    }

    public string Heading => _original is null ? "New entry" : "Edit entry";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _title;

    [ObservableProperty] private string _username;
    [ObservableProperty] private string _password;

    /// <summary>Masked by default, like in the details view.</summary>
    [ObservableProperty] private bool _isPasswordRevealed;

    [RelayCommand]
    private void ToggleReveal() => IsPasswordRevealed = !IsPasswordRevealed;

    /// <summary>One website per line.</summary>
    [ObservableProperty] private string _urls;

    /// <summary>Don't offer this login on subdomains (e.g. sites.google.com for a google.com login).</summary>
    [ObservableProperty] private bool _matchExactHost;

    [ObservableProperty] private string _notes;
    [ObservableProperty] private string _totpSecret;
    [ObservableProperty] private string? _error;

    [RelayCommand]
    private void Generate()
    {
        if (_dialogs.ShowGenerator(allowUse: true) is { } generated) Password = generated;
    }

    private bool CanSave() => !string.IsNullOrWhiteSpace(Title);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var totp = TotpSecret.Trim();
        if (totp.Length > 0)
        {
            try
            {
                TotpParameters.Parse(totp);
            }
            catch (Exception ex) when (ex is FormatException or UriFormatException)
            {
                Error = $"2FA secret: {ex.Message}";
                return;
            }
        }

        var urls = Urls.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(u => u.Contains("://", StringComparison.Ordinal) ? u : "https://" + u)
            .ToList();

        var password = NullIfBlank(Password);
        if (password is null && !ConfirmSaveWithoutPassword(urls)) return;

        // WithPassword keeps the previous password in the history when it changes.
        var item = (_original ?? new VaultItem { Title = "" }).WithPassword(password, DateTimeOffset.UtcNow) with
        {
            Title = Title.Trim(),
            Username = NullIfBlank(Username.Trim()),
            Urls = urls,
            MatchExactHost = MatchExactHost,
            Notes = NullIfBlank(Notes),
            TotpSecret = NullIfBlank(totp),
        };
        _onSave(item);
    }

    /// <summary>
    /// A login saved without a password is almost always a mistake (and once was a bug), so ask.
    /// Notes-only entries (no username, no website) don't need a password.
    /// </summary>
    private bool ConfirmSaveWithoutPassword(List<string> urls)
    {
        if (_original?.Password is not null)
            return _dialogs.Confirm(
                "You removed this entry's password. Save it without a password?\n\nThe old password is kept under \"Previous passwords\".",
                "No password");

        var looksLikeLogin = !string.IsNullOrWhiteSpace(Username) || urls.Count > 0;
        return !looksLikeLogin || _dialogs.Confirm("This login has no password. Save it anyway?", "No password");
    }

    [RelayCommand]
    private void Cancel() => _onCancel();

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
