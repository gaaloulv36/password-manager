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
        var item = (_original ?? new VaultItem { Title = "" }) with
        {
            Title = Title.Trim(),
            Username = NullIfBlank(Username.Trim()),
            Password = password,
            PasswordChangedAt = _original is not null && _original.Password == password ? _original.PasswordChangedAt : DateTimeOffset.UtcNow,
            Urls = urls,
            Notes = NullIfBlank(Notes),
            TotpSecret = NullIfBlank(totp),
        };
        _onSave(item);
    }

    [RelayCommand]
    private void Cancel() => _onCancel();

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
