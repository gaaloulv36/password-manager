using System.Net;

namespace VaultForge.Core.Browser;

/// <summary>
/// Decides whether a saved login may be offered on a web page. This is the phishing guard, so it errs
/// on the side of not matching: same site or one of its subdomains only, never an HTTPS login on HTTP.
/// </summary>
public static class UrlMatcher
{
    /// <param name="exactHost">Only the saved host itself (still ignoring "www."), never its subdomains.</param>
    public static bool Matches(string savedUrl, string pageUrl, bool exactHost = false)
    {
        if (!TryParse(savedUrl, out var saved) || !TryParse(pageUrl, out var page)) return false;
        if (page.Scheme is not ("https" or "http")) return false;
        if (saved.Scheme == "https" && page.Scheme != "https") return false;
        if (!saved.IsDefaultPort && saved.Port != page.Port) return false;

        // IdnHost is the punycode form, so look-alikes such as "gіthub.com" (Cyrillic і) never equal "github.com".
        if (!TryIdnHost(saved, out var savedIdn) || !TryIdnHost(page, out var pageIdn)) return false;
        var savedHost = Normalize(savedIdn);
        var pageHost = Normalize(pageIdn);
        if (!IsSpecificHost(savedHost)) return false;

        return pageHost == savedHost || (!exactHost && pageHost.EndsWith("." + savedHost, StringComparison.Ordinal));
    }

    /// <summary>Host shown to the user and used as the title for logins saved from the browser.</summary>
    public static string? DisplayHost(string url) => TryParse(url, out var uri) ? Normalize(uri.Host) : null;

    /// <summary>Scheme + host (+ port): what gets stored for a login saved from the browser.</summary>
    public static string? Origin(string url) => TryParse(url, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : null;

    private static bool TryParse(string url, out Uri uri)
    {
        var candidate = url.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal)) candidate = "https://" + candidate;
        return Uri.TryCreate(candidate, UriKind.Absolute, out uri!) && uri.Host.Length > 0;
    }

    /// <summary>A host that isn't valid IDN (found by fuzzing) is simply not a match, never an exception.</summary>
    private static bool TryIdnHost(Uri uri, out string host)
    {
        try
        {
            host = uri.IdnHost;
            return true;
        }
        catch (UriFormatException)
        {
            host = "";
            return false;
        }
    }

    private static string Normalize(string host)
    {
        host = host.ToLowerInvariant().TrimEnd('.');
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    /// <summary>A bare TLD ("com") would otherwise match every site under it.</summary>
    private static bool IsSpecificHost(string host) =>
        host == "localhost" || IPAddress.TryParse(host.Trim('[', ']'), out _) || host.Contains('.');
}
