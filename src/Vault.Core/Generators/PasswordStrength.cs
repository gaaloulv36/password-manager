namespace Vault.Core.Generators;

public enum StrengthLevel { VeryWeak, Weak, Fair, Strong, VeryStrong }

/// <summary>
/// Rough entropy estimate from length and character variety, with a penalty for repeated characters.
/// Good enough for a strength meter; it can't spot dictionary words or patterns like "Password1!".
/// </summary>
public static class PasswordStrength
{
    public static double EstimateBits(string password)
    {
        if (password.Length == 0) return 0;

        var pool = 0;
        if (password.Any(char.IsAsciiLetterLower)) pool += 26;
        if (password.Any(char.IsAsciiLetterUpper)) pool += 26;
        if (password.Any(char.IsAsciiDigit)) pool += 10;
        if (password.Any(c => char.IsAscii(c) && !char.IsAsciiLetterOrDigit(c))) pool += 33;
        if (password.Any(c => !char.IsAscii(c))) pool += 100;

        var effectiveLength = Math.Min(password.Length, password.Distinct().Count() * 2);
        return effectiveLength * Math.Log2(pool);
    }

    public static StrengthLevel Evaluate(string password) => EstimateBits(password) switch
    {
        < 30 => StrengthLevel.VeryWeak,
        < 50 => StrengthLevel.Weak,
        < 70 => StrengthLevel.Fair,
        < 90 => StrengthLevel.Strong,
        _ => StrengthLevel.VeryStrong,
    };
}
