namespace Vault.Core.Models;

/// <summary>A password the entry used before, kept so a change can be undone.</summary>
public sealed record PasswordHistoryEntry(string Password, DateTimeOffset ReplacedAt);

/// <summary>Decrypted login entry. Only exists in memory while the vault is unlocked.</summary>
public sealed record VaultItem
{
    public const int MaxPasswordHistory = 10;

    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Title { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public List<string> Urls { get; init; } = [];

    /// <summary>Offer this login only on the saved site itself, not on its subdomains.</summary>
    public bool MatchExactHost { get; init; }

    public string? Notes { get; init; }
    public string? TotpSecret { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>When the password itself last changed (null: since <see cref="CreatedAt"/>). Used for "old password" warnings.</summary>
    public DateTimeOffset? PasswordChangedAt { get; init; }

    /// <summary>Previous passwords, newest first (at most <see cref="MaxPasswordHistory"/>).</summary>
    public List<PasswordHistoryEntry> PasswordHistory { get; init; } = [];

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// This entry with <paramref name="newPassword"/>. If it differs from the current one, the current one moves
    /// into the history and the change time is recorded; otherwise the entry is returned unchanged.
    /// </summary>
    public VaultItem WithPassword(string? newPassword, DateTimeOffset now)
    {
        if (newPassword == Password) return this;

        var history = Password is null
            ? PasswordHistory
            : [new PasswordHistoryEntry(Password, now), .. PasswordHistory.Where(h => h.Password != newPassword)];
        return this with
        {
            Password = newPassword,
            PasswordChangedAt = now,
            PasswordHistory = history.Take(MaxPasswordHistory).ToList(),
        };
    }
}
