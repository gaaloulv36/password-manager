using System.Text;
using VaultForge.Core.Otp;

namespace VaultForge.Core.Tests;

public class TotpTests
{
    // RFC 6238 Appendix B
    [Theory]
    [InlineData(59L, TotpAlgorithm.Sha1, "94287082")]
    [InlineData(59L, TotpAlgorithm.Sha256, "46119246")]
    [InlineData(59L, TotpAlgorithm.Sha512, "90693936")]
    [InlineData(1111111109L, TotpAlgorithm.Sha1, "07081804")]
    [InlineData(1111111109L, TotpAlgorithm.Sha256, "68084774")]
    [InlineData(1111111109L, TotpAlgorithm.Sha512, "25091201")]
    [InlineData(1111111111L, TotpAlgorithm.Sha1, "14050471")]
    [InlineData(1111111111L, TotpAlgorithm.Sha256, "67062674")]
    [InlineData(1111111111L, TotpAlgorithm.Sha512, "99943326")]
    [InlineData(1234567890L, TotpAlgorithm.Sha1, "89005924")]
    [InlineData(1234567890L, TotpAlgorithm.Sha256, "91819424")]
    [InlineData(1234567890L, TotpAlgorithm.Sha512, "93441116")]
    [InlineData(2000000000L, TotpAlgorithm.Sha1, "69279037")]
    [InlineData(2000000000L, TotpAlgorithm.Sha256, "90698825")]
    [InlineData(2000000000L, TotpAlgorithm.Sha512, "38618901")]
    [InlineData(20000000000L, TotpAlgorithm.Sha1, "65353130")]
    [InlineData(20000000000L, TotpAlgorithm.Sha256, "77737706")]
    [InlineData(20000000000L, TotpAlgorithm.Sha512, "47863826")]
    public void Generate_MatchesRfc6238Vectors(long unixTime, TotpAlgorithm algorithm, string expected)
    {
        var seed = algorithm switch
        {
            TotpAlgorithm.Sha1 => "12345678901234567890",
            TotpAlgorithm.Sha256 => "12345678901234567890123456789012",
            _ => "1234567890123456789012345678901234567890123456789012345678901234",
        };
        var parameters = new TotpParameters(Encoding.ASCII.GetBytes(seed), Digits: 8, Algorithm: algorithm);

        Assert.Equal(expected, Totp.Generate(parameters, DateTimeOffset.FromUnixTimeSeconds(unixTime)));
    }

    [Fact]
    public void Parse_PlainBase32Secret_IsLenient()
    {
        // "12345678901234567890" in Base32, as a setup page might show it
        var parameters = TotpParameters.Parse("gezd gnbv gy3t qojq gezd gnbv gy3t qojq");

        Assert.Equal("12345678901234567890"u8.ToArray(), parameters.Secret);
        Assert.Equal(6, parameters.Digits);
        Assert.Equal("287082", Totp.Generate(parameters, DateTimeOffset.FromUnixTimeSeconds(59)));
    }

    [Fact]
    public void Parse_OtpauthUri_ReadsAllSettings()
    {
        var parameters = TotpParameters.Parse(
            "otpauth://totp/GitHub:me?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ&issuer=GitHub&algorithm=SHA256&digits=8&period=60");

        Assert.Equal(TotpAlgorithm.Sha256, parameters.Algorithm);
        Assert.Equal(8, parameters.Digits);
        Assert.Equal(60, parameters.PeriodSeconds);
    }

    [Theory]
    [InlineData("not base32!")]
    [InlineData("")]
    [InlineData("otpauth://hotp/x?secret=GEZDGNBV")]
    [InlineData("otpauth://totp/x?issuer=NoSecret")]
    public void Parse_RejectsInvalidInput(string input) =>
        Assert.Throws<FormatException>(() => TotpParameters.Parse(input));

    [Fact]
    public void SecondsRemaining_CountsDownWithinPeriod()
    {
        var parameters = new TotpParameters([1]);
        Assert.Equal(30, Totp.SecondsRemaining(parameters, DateTimeOffset.FromUnixTimeSeconds(60)));
        Assert.Equal(1, Totp.SecondsRemaining(parameters, DateTimeOffset.FromUnixTimeSeconds(89)));
    }
}
