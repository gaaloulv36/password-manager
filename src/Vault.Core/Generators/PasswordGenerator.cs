using System.Security.Cryptography;

namespace Vault.Core.Generators;

public sealed record PasswordOptions(
    int Length = 20,
    bool Lowercase = true,
    bool Uppercase = true,
    bool Digits = true,
    bool Symbols = true,
    bool ExcludeAmbiguous = false);

public static class PasswordGenerator
{
    private const string LowerChars = "abcdefghijklmnopqrstuvwxyz";
    private const string UpperChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string DigitChars = "0123456789";
    private const string SymbolChars = "!@#$%^&*()-_=+[]{};:,.?/";
    private const string AmbiguousChars = "Il1O0o";

    /// <summary>Uniformly random password containing at least one character from each enabled set.</summary>
    public static string Generate(PasswordOptions options)
    {
        var sets = new[]
            {
                (options.Lowercase, LowerChars),
                (options.Uppercase, UpperChars),
                (options.Digits, DigitChars),
                (options.Symbols, SymbolChars),
            }
            .Where(s => s.Item1)
            .Select(s => options.ExcludeAmbiguous ? new string(s.Item2.Except(AmbiguousChars).ToArray()) : s.Item2)
            .ToArray();

        if (sets.Length == 0)
            throw new ArgumentException("At least one character set must be enabled.", nameof(options));
        if (options.Length < sets.Length)
            throw new ArgumentException($"Length must be at least {sets.Length}.", nameof(options));

        var allChars = string.Concat(sets);
        var password = new char[options.Length];

        for (var i = 0; i < sets.Length; i++)
            password[i] = sets[i][RandomNumberGenerator.GetInt32(sets[i].Length)];
        RandomNumberGenerator.GetItems<char>(allChars, password.AsSpan(sets.Length));
        RandomNumberGenerator.Shuffle(password.AsSpan());

        return new string(password);
    }
}
