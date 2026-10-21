using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vault.Core.Generators;
using Vault.Desktop.Services;

namespace Vault.Desktop.ViewModels;

public sealed partial class GeneratorViewModel : ObservableObject
{
    private readonly ClipboardService _clipboard;

    public GeneratorViewModel(ClipboardService clipboard, bool allowUse, bool passphrase = false)
    {
        _clipboard = clipboard;
        AllowUse = allowUse;
        _usePassphrase = passphrase;
        Regenerate();
    }

    public bool AllowUse { get; }

    public event EventHandler? Used;

    [ObservableProperty] private bool _usePassphrase;

    [ObservableProperty] private int _length = 20;
    [ObservableProperty] private bool _lowercase = true;
    [ObservableProperty] private bool _uppercase = true;
    [ObservableProperty] private bool _digits = true;
    [ObservableProperty] private bool _symbols = true;
    [ObservableProperty] private bool _excludeAmbiguous;

    [ObservableProperty] private int _wordCount = 5;
    [ObservableProperty] private string _separator = "-";
    [ObservableProperty] private bool _capitalize;
    [ObservableProperty] private bool _includeNumber;

    [ObservableProperty] private string _result = "";
    [ObservableProperty] private string _entropyText = "";
    [ObservableProperty] private string? _error;

    /// <summary>Any option change produces a fresh value immediately.</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(Result) or nameof(EntropyText) or nameof(Error)))
            Regenerate();
    }

    [RelayCommand]
    private void Regenerate()
    {
        try
        {
            double bits;
            if (UsePassphrase)
            {
                var options = new PassphraseOptions(WordCount, Separator, Capitalize, IncludeNumber);
                Result = PassphraseGenerator.Generate(options);
                bits = PassphraseGenerator.EntropyBits(options);
            }
            else
            {
                var options = new PasswordOptions(Length, Lowercase, Uppercase, Digits, Symbols, ExcludeAmbiguous);
                Result = PasswordGenerator.Generate(options);
                bits = PasswordStrength.EstimateBits(Result);
            }
            EntropyText = string.Create(CultureInfo.CurrentCulture, $"≈ {bits:0} bits of entropy");
            Error = null;
        }
        catch (ArgumentException)
        {
            Result = "";
            EntropyText = "";
            Error = UsePassphrase ? "Choose between 3 and 20 words." : "Turn on at least one character type.";
        }
    }

    [RelayCommand]
    private void Copy()
    {
        if (Result.Length > 0) _clipboard.CopySecret(Result);
    }

    [RelayCommand]
    private void Use()
    {
        if (Result.Length > 0) Used?.Invoke(this, EventArgs.Empty);
    }
}
