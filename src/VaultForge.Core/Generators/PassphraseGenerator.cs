using System.Globalization;
using System.Security.Cryptography;

namespace VaultForge.Core.Generators;

public sealed record PassphraseOptions(
    int WordCount = 5,
    string Separator = "-",
    bool Capitalize = false,
    bool IncludeNumber = false);

/// <summary>Random-word passphrases from the EFF large wordlist (7776 words ≈ 12.9 bits of entropy per word).</summary>
public static class PassphraseGenerator
{
    private static readonly Lazy<string[]> Words = new(LoadWords);

    public static IReadOnlyList<string> Wordlist => Words.Value;

    public static double EntropyBits(PassphraseOptions options) =>
        options.WordCount * Math.Log2(Words.Value.Length) + (options.IncludeNumber ? Math.Log2(10 * options.WordCount) : 0);

    public static string Generate(PassphraseOptions options)
    {
        if (options.WordCount is < 3 or > 20)
            throw new ArgumentException("Word count must be between 3 and 20.", nameof(options));

        var words = RandomNumberGenerator.GetItems<string>(Words.Value, options.WordCount);
        if (options.Capitalize)
        {
            for (var i = 0; i < words.Length; i++)
                words[i] = char.ToUpperInvariant(words[i][0]) + words[i][1..];
        }

        if (options.IncludeNumber)
        {
            var index = RandomNumberGenerator.GetInt32(words.Length);
            words[index] += RandomNumberGenerator.GetInt32(10).ToString(CultureInfo.InvariantCulture);
        }

        return string.Join(options.Separator, words);
    }

    private static string[] LoadWords()
    {
        using var stream = typeof(PassphraseGenerator).Assembly.GetManifestResourceStream("VaultForge.Core.EffWordlist")
            ?? throw new InvalidOperationException("Embedded wordlist is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
