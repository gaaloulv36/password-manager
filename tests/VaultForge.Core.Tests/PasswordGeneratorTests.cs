using VaultForge.Core.Generators;

namespace VaultForge.Core.Tests;

public class PasswordGeneratorTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(20)]
    [InlineData(128)]
    public void Generate_ReturnsRequestedLength(int length) =>
        Assert.Equal(length, PasswordGenerator.Generate(new PasswordOptions(Length: length)).Length);

    [Fact]
    public void Generate_ContainsEveryEnabledSet()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = PasswordGenerator.Generate(new PasswordOptions(Length: 4));
            Assert.Contains(password, char.IsLower);
            Assert.Contains(password, char.IsUpper);
            Assert.Contains(password, char.IsDigit);
            Assert.Contains(password, c => !char.IsLetterOrDigit(c));
        }
    }

    [Fact]
    public void Generate_RespectsExcludeAmbiguous()
    {
        var password = PasswordGenerator.Generate(new PasswordOptions(Length: 500, ExcludeAmbiguous: true));
        Assert.DoesNotContain(password, "Il1O0o".Contains);
    }

    [Fact]
    public void Generate_WithNoSets_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            PasswordGenerator.Generate(new PasswordOptions(Lowercase: false, Uppercase: false, Digits: false, Symbols: false)));
}
