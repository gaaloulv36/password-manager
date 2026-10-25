using VaultForge.Core.Generators;
using VaultForge.Core.Models;

namespace VaultForge.Core.Security;

/// <summary>Health of one login's password. <see cref="BreachCount"/> is null until a breach check has run.</summary>
public sealed record ItemHealth(VaultItem Item, StrengthLevel Strength, int ReusedCount, bool IsOld, int? BreachCount)
{
    public bool IsWeak => Strength <= StrengthLevel.Weak;
    public bool IsReused => ReusedCount > 0;
    public bool IsBreached => BreachCount > 0;
    public bool HasIssues => IsWeak || IsReused || IsOld || IsBreached;

    /// <summary>Breached first, then reused, weak, old: the order in which they're worth fixing.</summary>
    public int Severity => (IsBreached ? 8 : 0) + (IsReused ? 4 : 0) + (IsWeak ? 2 : 0) + (IsOld ? 1 : 0);
}

public sealed record HealthReport(IReadOnlyList<ItemHealth> Items)
{
    public int Total => Items.Count;
    public int Breached => Items.Count(i => i.IsBreached);
    public int Reused => Items.Count(i => i.IsReused);
    public int Weak => Items.Count(i => i.IsWeak);
    public int Old => Items.Count(i => i.IsOld);

    /// <summary>Share of logins with no issues, 0–100 (100 for an empty vault).</summary>
    public int Score => Total == 0 ? 100 : (int)Math.Round(100.0 * Items.Count(i => !i.HasIssues) / Total);
}

public static class PasswordHealth
{
    public static readonly TimeSpan OldAfter = TimeSpan.FromDays(365);

    public static HealthReport Analyze(IEnumerable<VaultItem> items, DateTimeOffset now, IReadOnlyDictionary<Guid, int>? breachCounts = null)
    {
        var withPasswords = items.Where(i => !string.IsNullOrEmpty(i.Password)).ToList();
        var uses = withPasswords.GroupBy(i => i.Password!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        return new HealthReport(withPasswords
            .Select(item => new ItemHealth(
                item,
                PasswordStrength.Evaluate(item.Password!),
                ReusedCount: uses[item.Password!] - 1,
                IsOld: now - (item.PasswordChangedAt ?? item.CreatedAt) > OldAfter,
                BreachCount: breachCounts is null ? null : breachCounts.GetValueOrDefault(item.Id)))
            .OrderByDescending(h => h.Severity)
            .ThenBy(h => h.Item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList());
    }
}
