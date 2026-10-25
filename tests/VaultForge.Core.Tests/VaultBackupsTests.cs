using VaultForge.Core.Storage;

namespace VaultForge.Core.Tests;

public sealed class VaultBackupsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pm-backups-{Guid.NewGuid():N}");
    private readonly string _vault;

    public VaultBackupsTests()
    {
        Directory.CreateDirectory(_dir);
        _vault = Path.Combine(_dir, "Passwords.pmvault");
        File.WriteAllText(_vault, "v1");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string BackupDir => Path.Combine(_dir, "Backups");

    [Fact]
    public void CreatesOneBackupPerDay()
    {
        var first = VaultBackups.CreateDailyBackup(_vault, BackupDir, new DateOnly(2026, 10, 21));
        File.WriteAllText(_vault, "v2");
        var second = VaultBackups.CreateDailyBackup(_vault, BackupDir, new DateOnly(2026, 10, 21));

        Assert.EndsWith("Passwords-2026-10-21.pmvault", first);
        Assert.Null(second);
        Assert.Equal("v1", File.ReadAllText(first!));
    }

    [Fact]
    public void KeepsOnlyTheNewestBackups()
    {
        for (var day = 1; day <= 5; day++)
            VaultBackups.CreateDailyBackup(_vault, BackupDir, new DateOnly(2026, 10, day), keepCount: 3);

        Assert.Equal(
            ["Passwords-2026-10-03.pmvault", "Passwords-2026-10-04.pmvault", "Passwords-2026-10-05.pmvault"],
            Directory.GetFiles(BackupDir).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Restore_ReplacesVaultAndKeepsTheOldFile()
    {
        using var vault = UnlockedVault.Create("pw", new VaultForge.Core.Crypto.KdfParameters(new byte[16], 1024, 1, 1));
        var backup = Path.Combine(_dir, "good.pmvault");
        VaultFileStore.Save(vault.Document, backup);
        var damaged = Path.Combine(_dir, "Damaged");

        VaultBackups.Restore(backup, _vault, damaged);

        Assert.Equal(File.ReadAllText(backup), File.ReadAllText(_vault));
        Assert.Equal("v1", File.ReadAllText(Assert.Single(Directory.GetFiles(damaged))));
    }

    [Fact]
    public void Restore_RefusesSomethingThatIsNotAVault()
    {
        var notAVault = Path.Combine(_dir, "notes.pmvault");
        File.WriteAllText(notAVault, "{ \"hello\": 1 }");

        Assert.ThrowsAny<Exception>(() => VaultBackups.Restore(notAVault, _vault, Path.Combine(_dir, "Damaged")));
        Assert.Equal("v1", File.ReadAllText(_vault));
    }

    [Fact]
    public void PruningLeavesOtherVaultsAlone()
    {
        var other = Path.Combine(_dir, "Work.pmvault");
        File.WriteAllText(other, "w");
        VaultBackups.CreateDailyBackup(other, BackupDir, new DateOnly(2026, 1, 1));

        for (var day = 1; day <= 3; day++)
            VaultBackups.CreateDailyBackup(_vault, BackupDir, new DateOnly(2026, 10, day), keepCount: 1);

        Assert.True(File.Exists(Path.Combine(BackupDir, "Work-2026-01-01.pmvault")));
    }
}
