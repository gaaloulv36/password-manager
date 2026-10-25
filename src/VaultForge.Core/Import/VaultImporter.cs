using System.Text.Json;
using VaultForge.Core.Models;

namespace VaultForge.Core.Import;

public sealed record ImportResult(string Format, IReadOnlyList<VaultItem> Items, int SkippedCount);

/// <summary>
/// Reads password exports from other managers. The format is detected from the content:
/// Chrome/Edge, Firefox, Bitwarden (CSV and unencrypted JSON), KeePass 2, KeePassXC, and this app's own CSV export.
/// </summary>
public static class VaultImporter
{
    /// <summary>Header written by <see cref="VaultExporter"/>; also accepted on import.</summary>
    public static readonly string[] OwnCsvHeader = ["title", "url", "username", "password", "notes", "totp"];

    private static readonly string[] TitleColumns = ["title", "name", "account"];
    private static readonly string[] UrlColumns = ["url", "login_uri", "web site", "website"];
    private static readonly string[] UsernameColumns = ["username", "login_username", "login name"];
    private static readonly string[] PasswordColumns = ["password", "login_password"];
    private static readonly string[] NotesColumns = ["notes", "note", "comments", "extra"];
    private static readonly string[] TotpColumns = ["totp", "login_totp", "otp"];

    /// <exception cref="FormatException">Unrecognised or unsupported (e.g. encrypted) export.</exception>
    public static ImportResult Parse(string content) =>
        content.TrimStart('﻿', ' ', '\r', '\n', '\t').StartsWith('{') ? ParseBitwardenJson(content) : ParseCsv(content);

    private static ImportResult ParseCsv(string content)
    {
        var rows = Csv.Parse(content);
        if (rows.Count == 0) throw new FormatException("The file is empty.");

        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToArray();
        var format = DetectFormat(header);
        int Column(string[] names) => Array.FindIndex(header, names.Contains);

        var (title, url, username, password, notes, totp) =
            (Column(TitleColumns), Column(UrlColumns), Column(UsernameColumns), Column(PasswordColumns), Column(NotesColumns), Column(TotpColumns));
        if (password < 0) throw new FormatException("This doesn't look like a password export: there's no password column.");

        var type = Array.IndexOf(header, "type");
        var group = Array.IndexOf(header, "group");
        var splitUrls = format == "Bitwarden (CSV)";

        var items = new List<VaultItem>();
        var skipped = 0;
        foreach (var row in rows.Skip(1))
        {
            string? Get(int index) => index >= 0 && index < row.Length && row[index].Trim().Length > 0 ? row[index].Trim() : null;

            if (Get(group)?.Equals("Recycle Bin", StringComparison.OrdinalIgnoreCase) == true
                || Get(type) is { } t && t is not ("login" or "note"))
            {
                skipped++;
                continue;
            }

            var urls = Get(url) is { } u ? (splitUrls ? u.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [u]) : [];
            var item = CreateItem(Get(title), Get(username), row.ElementAtOrDefault(password), urls, Get(notes), Get(totp));
            if (item is null) skipped++;
            else items.Add(item);
        }

        return new ImportResult(format, items, skipped);
    }

    private static string DetectFormat(string[] header)
    {
        bool Has(params string[] names) => names.All(header.Contains);

        if (Has("login_uri", "login_password")) return "Bitwarden (CSV)";
        if (Has("httprealm", "formactionorigin")) return "Firefox";
        if (Has("group", "title", "username", "password")) return "KeePassXC";
        if (Has("account", "login name", "password")) return "KeePass";
        if (header.SequenceEqual(OwnCsvHeader)) return "VaultForge";
        if (Has("name", "url", "username", "password")) return "Chrome / Edge";
        return "CSV";
    }

    private static ImportResult ParseBitwardenJson(string content)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"The JSON file is invalid: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.TryGetProperty("encrypted", out var encrypted) && encrypted.ValueKind == JsonValueKind.True)
                throw new FormatException("This Bitwarden export is encrypted. Export again choosing \"JSON\" (not \"JSON (Encrypted)\") or CSV.");
            if (!root.TryGetProperty("items", out var array) || array.ValueKind != JsonValueKind.Array)
                throw new FormatException("This JSON file isn't a Bitwarden export.");

            var items = new List<VaultItem>();
            var skipped = 0;
            foreach (var entry in array.EnumerateArray())
            {
                // 1 = login, 2 = secure note. Cards and identities aren't supported yet.
                var type = entry.TryGetProperty("type", out var t) && t.TryGetInt32(out var value) ? value : 0;
                if (type is not (1 or 2))
                {
                    skipped++;
                    continue;
                }

                var login = entry.TryGetProperty("login", out var l) && l.ValueKind == JsonValueKind.Object ? l : default;
                var urls = login.ValueKind == JsonValueKind.Object && login.TryGetProperty("uris", out var uris) && uris.ValueKind == JsonValueKind.Array
                    ? uris.EnumerateArray().Select(u => String(u, "uri")).OfType<string>().ToArray()
                    : [];

                var item = CreateItem(String(entry, "name"), String(login, "username"), String(login, "password"), urls,
                    String(entry, "notes"), String(login, "totp"));
                if (item is null) skipped++;
                else items.Add(item);
            }
            return new ImportResult("Bitwarden (JSON)", items, skipped);
        }

        static string? String(JsonElement element, string property) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String && value.GetString()!.Trim() is { Length: > 0 } text
                ? text
                : null;
    }

    /// <summary>Returns null for rows with nothing worth keeping.</summary>
    private static VaultItem? CreateItem(string? title, string? username, string? password, IReadOnlyList<string> urls, string? notes, string? totp)
    {
        password = string.IsNullOrEmpty(password) ? null : password;
        if (username is null && password is null && urls.Count == 0 && notes is null) return null;

        return new VaultItem
        {
            Title = title ?? urls.Select(HostOf).FirstOrDefault(h => h is not null) ?? username ?? "Untitled",
            Username = username,
            Password = password,
            Urls = [.. urls],
            Notes = notes,
            TotpSecret = totp,
        };
    }

    /// <summary>Host without "www.", or null if the text isn't a URL.</summary>
    public static string? HostOf(string url)
    {
        var withScheme = url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url;
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var uri) || uri.Host.Length == 0) return null;
        return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
    }

    /// <summary>Same username and password on the same site (or both without a site).</summary>
    public static bool IsDuplicate(VaultItem candidate, VaultItem existing)
    {
        if (!string.Equals(candidate.Username, existing.Username, StringComparison.OrdinalIgnoreCase)
            || candidate.Password != existing.Password)
            return false;

        var hosts = existing.Urls.Select(HostOf).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidateHosts = candidate.Urls.Select(HostOf).OfType<string>().ToList();
        return candidateHosts.Count == 0 && hosts.Count == 0 || candidateHosts.Any(hosts.Contains);
    }
}
