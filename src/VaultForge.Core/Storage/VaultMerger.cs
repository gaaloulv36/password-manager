namespace VaultForge.Core.Storage;

/// <summary>What a merge brought in from the other copy.</summary>
public sealed record MergeResult(VaultDocument Document, int Added, int Updated, int Deleted, bool MasterPasswordChanged)
{
    public bool HasChanges => Added > 0 || Updated > 0 || Deleted > 0 || MasterPasswordChanged;
}

/// <summary>
/// Merges two copies of the same vault without decrypting anything. The result is the same whichever
/// copy is "local", so every PC converges to identical content.
/// Does not authenticate <paramref name="remote"/>: use <see cref="UnlockedVault.Merge"/> for untrusted input.
/// </summary>
public static class VaultMerger
{
    public static MergeResult Merge(VaultDocument local, VaultDocument remote)
    {
        if (local.VaultId != remote.VaultId)
            throw new InvalidOperationException("Cannot merge two different vaults.");

        var header = CompareHeaders(remote, local) > 0 ? remote : local;
        var winners = local.Items.ToDictionary(i => i.Id);
        var newIds = new List<Guid>();
        int added = 0, updated = 0, deleted = 0;

        foreach (var theirs in remote.Items)
        {
            if (!winners.TryGetValue(theirs.Id, out var ours))
            {
                winners[theirs.Id] = theirs;
                newIds.Add(theirs.Id);
                if (!theirs.IsDeleted) added++;
                continue;
            }

            if (CompareItems(theirs, ours) <= 0) continue;

            winners[theirs.Id] = theirs;
            if (theirs.IsDeleted && !ours.IsDeleted) deleted++;
            else if (!theirs.IsDeleted && ours.IsDeleted) added++;
            else if (!theirs.IsDeleted) updated++;
        }

        var merged = new VaultDocument
        {
            VaultId = local.VaultId,
            KeyGeneration = header.KeyGeneration,
            Kdf = header.Kdf,
            WrappedVaultKey = header.WrappedVaultKey,
            HeaderMac = header.HeaderMac,
            Items = [.. local.Items.Select(i => winners[i.Id]), .. newIds.Select(id => winners[id])],
        };

        return new MergeResult(merged, added, updated, deleted, MasterPasswordChanged: !ReferenceEquals(header, local));
    }

    /// <summary>
    /// Positive when <paramref name="a"/> should replace <paramref name="b"/>.
    /// Latest edit wins; the remaining keys only make ties deterministic.
    /// </summary>
    public static int CompareItems(EncryptedItem a, EncryptedItem b)
    {
        var result = a.UpdatedAt.CompareTo(b.UpdatedAt);
        if (result == 0) result = a.Revision.CompareTo(b.Revision);
        if (result == 0) result = a.IsDeleted.CompareTo(b.IsDeleted);
        if (result == 0) result = a.Payload.AsSpan().SequenceCompareTo(b.Payload);
        return result;
    }

    /// <summary>Newest master password wins; a simultaneous change on two PCs is settled deterministically.</summary>
    private static int CompareHeaders(VaultDocument a, VaultDocument b)
    {
        var result = a.KeyGeneration.CompareTo(b.KeyGeneration);
        return result != 0 ? result : a.WrappedVaultKey.AsSpan().SequenceCompareTo(b.WrappedVaultKey);
    }
}
