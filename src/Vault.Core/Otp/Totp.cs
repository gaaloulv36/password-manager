using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Web;

namespace Vault.Core.Otp;

public enum TotpAlgorithm { Sha1, Sha256, Sha512 }

/// <summary>Two-factor code settings, as found in an authenticator QR code.</summary>
public sealed record TotpParameters(byte[] Secret, int Digits = 6, int PeriodSeconds = 30, TotpAlgorithm Algorithm = TotpAlgorithm.Sha1)
{
    /// <summary>Accepts a plain Base32 secret ("JBSW Y3DP …") or an <c>otpauth://totp/…</c> URI.</summary>
    /// <exception cref="FormatException">Not a valid secret or URI.</exception>
    public static TotpParameters Parse(string input)
    {
        input = input.Trim();
        if (!input.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
            return new TotpParameters(Base32.Decode(input));

        var uri = new Uri(input);
        if (!uri.Host.Equals("totp", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Only time-based (TOTP) codes are supported.");

        var query = HttpUtility.ParseQueryString(uri.Query);
        var secret = query["secret"] ?? throw new FormatException("The URI has no secret.");
        return new TotpParameters(
            Base32.Decode(secret),
            Digits: ParseInt(query["digits"], 6),
            PeriodSeconds: ParseInt(query["period"], 30),
            Algorithm: (query["algorithm"] ?? "SHA1").ToUpperInvariant() switch
            {
                "SHA1" => TotpAlgorithm.Sha1,
                "SHA256" => TotpAlgorithm.Sha256,
                "SHA512" => TotpAlgorithm.Sha512,
                var other => throw new FormatException($"Unsupported algorithm '{other}'."),
            });
    }

    private static int ParseInt(string? value, int fallback) =>
        value is null ? fallback
        : int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) && result > 0 ? result
        : throw new FormatException($"Invalid number '{value}'.");
}

/// <summary>Time-based one-time passwords (RFC 6238).</summary>
public static class Totp
{
    public static string Generate(TotpParameters parameters, DateTimeOffset time)
    {
        if (parameters.Digits is < 6 or > 10)
            throw new ArgumentException("Digits must be between 6 and 10.", nameof(parameters));

        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, time.ToUnixTimeSeconds() / parameters.PeriodSeconds);

        var hash = parameters.Algorithm switch
        {
            TotpAlgorithm.Sha1 => HMACSHA1.HashData(parameters.Secret, counter),
            TotpAlgorithm.Sha256 => HMACSHA256.HashData(parameters.Secret, counter),
            _ => HMACSHA512.HashData(parameters.Secret, counter),
        };

        // Dynamic truncation, RFC 4226 §5.3
        var offset = hash[^1] & 0x0F;
        var binary = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset)) & 0x7FFFFFFF;
        var code = binary % (long)Math.Pow(10, parameters.Digits);
        return code.ToString(CultureInfo.InvariantCulture).PadLeft(parameters.Digits, '0');
    }

    public static int SecondsRemaining(TotpParameters parameters, DateTimeOffset time) =>
        parameters.PeriodSeconds - (int)(time.ToUnixTimeSeconds() % parameters.PeriodSeconds);
}

/// <summary>RFC 4648 Base32, lenient about case, spaces, dashes and padding (as typed from setup pages).</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static byte[] Decode(string input)
    {
        var output = new List<byte>(input.Length * 5 / 8);
        int buffer = 0, bits = 0;

        foreach (var c in input)
        {
            if (c is ' ' or '-' or '=') continue;
            var value = Alphabet.IndexOf(char.ToUpperInvariant(c));
            if (value < 0) throw new FormatException($"'{c}' is not a valid Base32 character.");

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)(buffer >> bits));
                buffer &= (1 << bits) - 1;
            }
        }

        if (output.Count == 0) throw new FormatException("The secret is empty.");
        return [.. output];
    }
}
