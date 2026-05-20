using ColdHarbour.Infrastructure.Jobs;
using FluentAssertions;

namespace ColdHarbour.Infrastructure.Tests.Jobs;

public sealed class BackupRetentionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public BackupRetentionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private string Touch(string name, DateTimeOffset written)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, name);
        File.SetLastWriteTimeUtc(path, written.UtcDateTime);
        return path;
    }

    [Fact]
    public void SelectToDelete_WhenUnderOrAtLimit_ReturnsEmpty()
    {
        var files = new[]
        {
            new FileInfo(Touch("backup-1.backup", DateTimeOffset.UtcNow.AddDays(-3))),
            new FileInfo(Touch("backup-2.backup", DateTimeOffset.UtcNow.AddDays(-2))),
            new FileInfo(Touch("backup-3.backup", DateTimeOffset.UtcNow.AddDays(-1))),
            new FileInfo(Touch("backup-4.backup", DateTimeOffset.UtcNow)),
        };

        var toDelete = BackupJob.SelectToDelete(files, keep: 4);

        toDelete.Should().BeEmpty();
    }

    [Fact]
    public void SelectToDelete_WhenOverLimit_ReturnsOldest()
    {
        var oldest = new FileInfo(Touch("backup-oldest.backup", DateTimeOffset.UtcNow.AddDays(-4)));
        var files = new[]
        {
            oldest,
            new FileInfo(Touch("backup-2.backup", DateTimeOffset.UtcNow.AddDays(-3))),
            new FileInfo(Touch("backup-3.backup", DateTimeOffset.UtcNow.AddDays(-2))),
            new FileInfo(Touch("backup-4.backup", DateTimeOffset.UtcNow.AddDays(-1))),
            new FileInfo(Touch("backup-5.backup", DateTimeOffset.UtcNow)),
        };

        var toDelete = BackupJob.SelectToDelete(files, keep: 4);

        toDelete.Should().ContainSingle().Which.Name.Should().Be("backup-oldest.backup");
    }

    [Fact]
    public void SelectToDelete_KeepsNewestFiles()
    {
        var files = Enumerable.Range(1, 7)
            .Select(i => new FileInfo(Touch($"backup-{i}.backup", DateTimeOffset.UtcNow.AddDays(-i))))
            .ToArray();

        var toDelete = BackupJob.SelectToDelete(files, keep: 4);

        toDelete.Should().HaveCount(3);
        // The 3 oldest (days -5, -6, -7) should be deleted
        toDelete.Select(f => f.Name).Should().Contain(["backup-5.backup", "backup-6.backup", "backup-7.backup"]);
    }
}
