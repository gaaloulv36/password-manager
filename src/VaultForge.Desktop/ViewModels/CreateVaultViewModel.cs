using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VaultForge.Core.Generators;
using VaultForge.Desktop.Services;

namespace VaultForge.Desktop.ViewModels;

public sealed partial class CreateVaultViewModel : ObservableObject
{
    private const int MinimumLength = 10;

    private readonly MainViewModel _navigation;
    private readonly VaultSession _session;
    private readonly AppSettings _settings;
    private readonly DialogService _dialogs;

    public CreateVaultViewModel(MainViewModel navigation, VaultSession session, AppSettings settings, DialogService dialogs)
    {
        _navigation = navigation;
        _session = session;
        _settings = settings;
        _dialogs = dialogs;

        // Suggest OneDrive so the vault syncs to other PCs out of the box.
        var folder = Environment.GetEnvironmentVariable("OneDrive") is { Length: > 0 } oneDrive && Directory.Exists(oneDrive)
            ? oneDrive
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _vaultPath = Path.Combine(folder, "Passwords.pmvault");
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _vaultPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Strength), nameof(StrengthText), nameof(StrengthValue))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _password = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _confirmPassword = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private bool _understandsNoRecovery;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private bool _isBusy;

    public StrengthLevel Strength => PasswordStrength.Evaluate(Password);
    public int StrengthValue => Password.Length == 0 ? 0 : (int)Strength + 1;

    public string StrengthText => Password.Length == 0 ? "" : Strength switch
    {
        StrengthLevel.VeryWeak => "Very weak",
        StrengthLevel.Weak => "Weak",
        StrengthLevel.Fair => "Fair — consider a longer passphrase",
        StrengthLevel.Strong => "Strong",
        _ => "Very strong",
    };

    private bool CanCreate() =>
        !IsBusy && UnderstandsNoRecovery && Password.Length > 0 && ConfirmPassword.Length > 0 && VaultPath.Length > 0;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        if (Password.Length < MinimumLength)
        {
            Error = $"Use at least {MinimumLength} characters. A passphrase of 5+ random words is ideal.";
            return;
        }
        if (Password != ConfirmPassword)
        {
            Error = "The passwords don't match.";
            return;
        }

        IsBusy = true;
        Error = null;
        try
        {
            var (path, password) = (VaultPath, Password);
            await Task.Run(() => _session.Create(path, password));
            _settings.LastVaultPath = path;
            _settings.Save();
            _navigation.ShowVault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = $"The vault can't be saved there. ({ex.Message})";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Browse()
    {
        if (_dialogs.PickVaultToCreate(VaultPath) is { } path) VaultPath = path;
    }

    [RelayCommand]
    private void SuggestPassphrase()
    {
        var passphrase = _dialogs.ShowGenerator(allowUse: true, passphrase: true);
        if (passphrase is not null) SuggestedPassphrase = passphrase;
    }

    /// <summary>Shown (not auto-filled) so the user consciously types — and memorises — it.</summary>
    [ObservableProperty]
    private string? _suggestedPassphrase;

    [RelayCommand]
    private void Cancel() => _navigation.ShowStart();
}
