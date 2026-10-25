using VaultForge.Core.Generators;

namespace VaultForge.Core.Tests;

public class PassphraseGeneratorTests
{
    [Fact]
    public void Wordlist_IsTheFullEffList()
    {
        Assert.Equal(7776, PassphraseGenerator.Wordlist.Count);
        Assert.Equal(7776, PassphraseGenerator.Wordlist.Distinct().Count());
        Assert.Equal("abacus", PassphraseGenerator.Wordlist[0]);
        Assert.Equal("zoom", PassphraseGenerator.Wordlist[^1]);
    }

    [Fact]
    public void Generate_UsesRequestedWordCountAndSeparator()
    {
        var words = PassphraseGenerator.Generate(new PassphraseOptions(WordCount: 6, Separator: ".")).Split('.');

        Assert.Equal(6, words.Length);
        Assert.All(words, w => Assert.Contains(w, PassphraseGenerator.Wordlist));
    }

    [Fact]
    public void Generate_CapitalizeAndNumber()
    {
        var passphrase = PassphraseGenerator.Generate(new PassphraseOptions(Capitalize: true, IncludeNumber: true));
        var words = passphrase.Split('-');

        Assert.All(words, w => Assert.True(char.IsUpper(w[0])));
        Assert.Single(words, w => char.IsDigit(w[^1]));
    }

    [Fact]
    public void DefaultOptions_GiveAtLeast64BitsOfEntropy() =>
        Assert.True(PassphraseGenerator.EntropyBits(new PassphraseOptions()) >= 64);

    [Theory]
    [InlineData(2)]
    [InlineData(21)]
    public void Generate_RejectsOutOfRangeWordCount(int count) =>
        Assert.Throws<ArgumentException>(() => PassphraseGenerator.Generate(new PassphraseOptions(WordCount: count)));
}
