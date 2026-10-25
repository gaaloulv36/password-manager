using System.Text;

namespace VaultForge.Core.Generators;

public enum StrengthLevel { VeryWeak, Weak, Fair, Strong, VeryStrong }

/// <summary>
/// Entropy estimate that knows the usual tricks attackers try first: the 10k most common passwords,
/// those same words with l33t substitutions and a number/symbol tacked on ("P@ssw0rd1!"), keyboard
/// and alphabet runs ("qwerty", "abcd1234"), and repeated characters. Not zxcvbn, but close enough to
/// stop a meter from praising "Password1!".
/// </summary>
public static class PasswordStrength
{
    private static readonly Lazy<Dictionary<string, int>> CommonRanks = new(LoadCommonPasswords);

    private static readonly string[] Sequences =
    [
        "abcdefghijklmnopqrstuvwxyz", "0123456789", "01234567890",
        "qwertyuiopasdfghjklzxcvbnm", "azertyuiopqsdfghjklmwxcvbn", "qwertzuiopasdfghjklyxcvbnm",
        "1qaz2wsx3edc4rfv5tgb6yhn7ujm8ik9ol0p",
    ];

    private static readonly Dictionary<char, char> Leet = new()
    {
        ['0'] = 'o', ['1'] = 'i', ['3'] = 'e', ['4'] = 'a', ['5'] = 's', ['7'] = 't', ['8'] = 'b', ['9'] = 'g',
        ['@'] = 'a', ['$'] = 's', ['!'] = 'i', ['|'] = 'l', ['+'] = 't',
    };

    public static double EstimateBits(string password)
    {
        if (password.Length == 0) return 0;

        var lower = password.ToLowerInvariant();
        if (CommonRanks.Value.TryGetValue(lower, out var rank)) return Math.Log2(rank + 1);
        if (IsSequence(lower)) return Math.Log2(password.Length * Sequences.Length * 2);

        // A common word, possibly l33t-spelled, with digits/symbols before or after: guessed right after the list itself.
        var core = lower.Trim(TrimmedAffixes);
        if (core.Length >= 3 && core.Length < lower.Length || core != Deleet(core))
        {
            if (CommonRanks.Value.TryGetValue(Deleet(core), out rank) || CommonRanks.Value.TryGetValue(core, out rank))
                return Math.Log2(rank + 1) + AffixBits(lower, core) + (password != lower ? 1 : 0) + (core != Deleet(core) ? 2 : 0);
        }

        return BruteForceBits(password);
    }

    public static StrengthLevel Evaluate(string password) => EstimateBits(password) switch
    {
        < 30 => StrengthLevel.VeryWeak,
        < 50 => StrengthLevel.Weak,
        < 70 => StrengthLevel.Fair,
        < 90 => StrengthLevel.Strong,
        _ => StrengthLevel.VeryStrong,
    };

    private static readonly char[] TrimmedAffixes = "0123456789!@#$%^&*()-_=+.,?~ ".ToCharArray();

    /// <summary>Character-class pool × effective length, penalising repeats.</summary>
    private static double BruteForceBits(string password)
    {
        var pool = 0;
        if (password.Any(char.IsAsciiLetterLower)) pool += 26;
        if (password.Any(char.IsAsciiLetterUpper)) pool += 26;
        if (password.Any(char.IsAsciiDigit)) pool += 10;
        if (password.Any(c => char.IsAscii(c) && !char.IsAsciiLetterOrDigit(c))) pool += 33;
        if (password.Any(c => !char.IsAscii(c))) pool += 100;

        var effectiveLength = Math.Min(password.Length, password.Distinct().Count() * 2);
        return effectiveLength * Math.Log2(pool);
    }

    private static double AffixBits(string whole, string core)
    {
        var affix = whole.Length - core.Length;
        var digitsOnly = whole.Replace(core, "", StringComparison.Ordinal).All(char.IsAsciiDigit);
        return affix * (digitsOnly ? Math.Log2(10) : Math.Log2(33));
    }

    /// <summary>"abcd", "4321", "qwerty", "asdf1234" (runs of ≥ 4 along a sequence, either direction).</summary>
    private static bool IsSequence(string lower)
    {
        if (lower.Length < 4) return false;
        if (lower.Distinct().Count() == 1) return true; // "aaaaaa"

        // Split into chunks that each run along one sequence, e.g. "asdf" + "1234".
        var i = 0;
        while (i < lower.Length)
        {
            var longest = Sequences.Max(s => RunLength(s, lower, i));
            if (longest < 3) return false;
            i += longest;
        }
        return true;
    }

    private static int RunLength(string sequence, string text, int start)
    {
        var best = 0;
        foreach (var reversed in new[] { false, true })
        {
            var seq = reversed ? new string(sequence.Reverse().ToArray()) : sequence;
            var at = seq.IndexOf(text[start]);
            if (at < 0) continue;
            var length = 0;
            while (start + length < text.Length && at + length < seq.Length && seq[at + length] == text[start + length]) length++;
            best = Math.Max(best, length);
        }
        return best;
    }

    private static string Deleet(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text) builder.Append(Leet.TryGetValue(c, out var plain) ? plain : c);
        return builder.ToString();
    }

    private static Dictionary<string, int> LoadCommonPasswords()
    {
        using var stream = typeof(PasswordStrength).Assembly.GetManifestResourceStream("VaultForge.Core.CommonPasswords")
            ?? throw new InvalidOperationException("Embedded common-password list is missing.");
        using var reader = new StreamReader(stream);
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        var rank = 0;
        while (reader.ReadLine() is { } line)
            if (line.Length > 0) ranks.TryAdd(line, rank++);
        return ranks;
    }
}
