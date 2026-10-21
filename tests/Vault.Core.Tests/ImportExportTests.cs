using Vault.Core.Import;
using Vault.Core.Models;

namespace Vault.Core.Tests;

public class CsvTests
{
    [Fact]
    public void Parse_HandlesQuotesCommasNewlinesAndBom()
    {
        var rows = Csv.Parse("﻿a,b,c\r\n\"x, y\",\"say \"\"hi\"\"\",\"line1\nline2\"\r\n\r\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal(["a", "b", "c"], rows[0]);
        Assert.Equal(["x, y", "say \"hi\"", "line1\nline2"], rows[1]);
    }

    [Fact]
    public void Format_RoundTripsThroughParse()
    {
        string[] fields = ["plain", "with, comma", "with \"quote\"", "multi\nline", " padded ", ""];
        Assert.Equal(fields, Csv.Parse(Csv.Format(fields)).Single());
    }
}

public class VaultImporterTests
{
    [Fact]
    public void Chrome()
    {
        var result = VaultImporter.Parse(
            "name,url,username,password,note\n" +
            "github.com,https://github.com/login,me,p1,\n" +
            "mail,https://mail.example.com/,me@example.com,\"p,2\",personal\n");

        Assert.Equal("Chrome / Edge", result.Format);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("p,2", result.Items[1].Password);
        Assert.Equal("personal", result.Items[1].Notes);
        Assert.Equal(["https://github.com/login"], result.Items[0].Urls);
    }

    [Fact]
    public void Firefox_DerivesTitleFromSite()
    {
        var result = VaultImporter.Parse(
            "\"url\",\"username\",\"password\",\"httpRealm\",\"formActionOrigin\",\"guid\",\"timeCreated\",\"timeLastUsed\",\"timePasswordChanged\"\n" +
            "\"https://www.reddit.com\",\"me\",\"p1\",,\"https://www.reddit.com\",\"{guid}\",\"1\",\"2\",\"3\"\n");

        Assert.Equal("Firefox", result.Format);
        Assert.Equal("reddit.com", result.Items.Single().Title);
    }

    [Fact]
    public void BitwardenCsv_SplitsUrlsAndKeepsTotp()
    {
        var result = VaultImporter.Parse(
            "folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp\n" +
            "Work,1,login,GitLab,,,0,\"https://gitlab.com,https://gitlab.example.com\",me,p1,JBSWY3DPEHPK3PXP\n" +
            ",,note,Wifi,Password is on the router,,0,,,,\n");

        Assert.Equal("Bitwarden (CSV)", result.Format);
        Assert.Equal(["https://gitlab.com", "https://gitlab.example.com"], result.Items[0].Urls);
        Assert.Equal("JBSWY3DPEHPK3PXP", result.Items[0].TotpSecret);
        Assert.Equal("Wifi", result.Items[1].Title);
    }

    [Fact]
    public void BitwardenJson_ImportsLoginsAndNotes_SkipsCards()
    {
        var result = VaultImporter.Parse("""
            { "encrypted": false, "folders": [], "items": [
              { "type": 1, "name": "GitHub", "notes": null,
                "login": { "username": "me", "password": "p1", "totp": "otpauth://totp/x?secret=JBSWY3DPEHPK3PXP",
                           "uris": [ { "match": null, "uri": "https://github.com" } ] } },
              { "type": 2, "name": "Alarm code", "notes": "1234" },
              { "type": 3, "name": "Visa", "card": { "number": "4111" } }
            ] }
            """);

        Assert.Equal("Bitwarden (JSON)", result.Format);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(["https://github.com"], result.Items[0].Urls);
        Assert.Equal("1234", result.Items[1].Notes);
    }

    [Fact]
    public void BitwardenJson_Encrypted_IsRejectedWithHelpfulMessage()
    {
        var ex = Assert.Throws<FormatException>(() => VaultImporter.Parse("""{ "encrypted": true, "items": [] }"""));
        Assert.Contains("encrypted", ex.Message);
    }

    [Fact]
    public void KeePassXC_SkipsRecycleBin()
    {
        var result = VaultImporter.Parse(
            "\"Group\",\"Title\",\"Username\",\"Password\",\"URL\",\"Notes\",\"TOTP\",\"Icon\",\"Last Modified\",\"Created\"\n" +
            "\"Root/Email\",\"Mail\",\"me\",\"p1\",\"https://mail.example.com\",\"\",\"\",\"0\",\"2024-01-01\",\"2024-01-01\"\n" +
            "\"Recycle Bin\",\"Old\",\"me\",\"p0\",\"\",\"\",\"\",\"0\",\"2024-01-01\",\"2024-01-01\"\n");

        Assert.Equal("KeePassXC", result.Format);
        Assert.Equal("Mail", result.Items.Single().Title);
        Assert.Equal(1, result.SkippedCount);
    }

    [Fact]
    public void KeePass2()
    {
        var result = VaultImporter.Parse(
            "\"Account\",\"Login Name\",\"Password\",\"Web Site\",\"Comments\"\n" +
            "\"Bank\",\"me\",\"p1\",\"https://bank.example\",\"call first\"\n");

        Assert.Equal("KeePass", result.Format);
        Assert.Equal("call first", result.Items.Single().Notes);
    }

    [Fact]
    public void EmptyRows_AreSkipped()
    {
        var result = VaultImporter.Parse("name,url,username,password,note\nempty,,,,\n");
        Assert.Empty(result.Items);
        Assert.Equal(1, result.SkippedCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("just,some,columns\n1,2,3\n")]
    [InlineData("{ not json")]
    [InlineData("""{ "something": "else" }""")]
    public void UnrecognisedFiles_AreRejected(string content) =>
        Assert.Throws<FormatException>(() => VaultImporter.Parse(content));

    [Fact]
    public void Export_ThenImport_RoundTrips()
    {
        VaultItem[] items =
        [
            new() { Title = "A, with comma", Username = "me", Password = "p\"1", Urls = ["https://a.example"], Notes = "line1\nline2", TotpSecret = "JBSWY3DPEHPK3PXP" },
            new() { Title = "B", Password = "p2" },
        ];

        var result = VaultImporter.Parse(VaultExporter.ToCsv(items));

        Assert.Equal("Password Manager", result.Format);
        Assert.Equal(2, result.Items.Count);
        var a = result.Items[0];
        Assert.Equal(("A, with comma", "me", "p\"1", "line1\nline2", "JBSWY3DPEHPK3PXP"), (a.Title, a.Username, a.Password, a.Notes, a.TotpSecret));
        Assert.Equal(["https://a.example"], a.Urls);
    }

    [Theory]
    [InlineData("https://github.com/login", "https://www.github.com", true)]
    [InlineData("https://github.com", "https://gitlab.com", false)]
    public void IsDuplicate_ComparesSiteUsernameAndPassword(string candidateUrl, string existingUrl, bool expected)
    {
        var existing = new VaultItem { Title = "x", Username = "Me", Password = "p", Urls = [existingUrl] };
        var candidate = new VaultItem { Title = "y", Username = "me", Password = "p", Urls = [candidateUrl] };

        Assert.Equal(expected, VaultImporter.IsDuplicate(candidate, existing));
        Assert.False(VaultImporter.IsDuplicate(candidate with { Password = "other" }, existing));
    }
}
