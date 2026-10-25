using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace VaultForge.Core.Security;

/// <summary>
/// Have I Been Pwned "Pwned Passwords" range API (k-anonymity): only the first 5 hex characters of the
/// password's SHA-1 are sent; the service returns every breached hash suffix in that range (padded with
/// decoys) and the match is done locally. The password and its full hash never leave the PC.
/// </summary>
public sealed class PwnedPasswordsClient(HttpClient http)
{
    public static readonly Uri RangeEndpoint = new("https://api.pwnedpasswords.com/range/");

    private readonly Dictionary<string, Dictionary<string, int>> _rangeCache = new(StringComparer.Ordinal);

    /// <summary>How often the password appears in known breaches; 0 if never.</summary>
    /// <exception cref="HttpRequestException">No connection, or the service is unavailable.</exception>
    public async Task<int> GetBreachCountAsync(string password, CancellationToken cancellationToken = default)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        var (prefix, suffix) = (hash[..5], hash[5..]);

        if (!_rangeCache.TryGetValue(prefix, out var range))
        {
            range = ParseRange(await FetchRangeAsync(prefix, cancellationToken));
            _rangeCache[prefix] = range;
        }
        return range.GetValueOrDefault(suffix);
    }

    private async Task<string> FetchRangeAsync(string prefix, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(RangeEndpoint, prefix));
        request.Headers.Add("Add-Padding", "true"); // fixed-size-ish responses: the reply size reveals nothing either
        request.Headers.UserAgent.ParseAdd($"{ProductInfo.Name}/{typeof(PwnedPasswordsClient).Assembly.GetName().Version?.ToString(3)}");
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>Lines of "SUFFIX:COUNT"; padding entries have a count of 0.</summary>
    internal static Dictionary<string, int> ParseRange(string body)
    {
        var range = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = line.IndexOf(':');
            if (colon == 35 && int.TryParse(line.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count > 0)
                range[line[..colon]] = count;
        }
        return range;
    }
}
