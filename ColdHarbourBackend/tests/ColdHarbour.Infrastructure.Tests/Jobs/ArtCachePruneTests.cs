using ColdHarbour.Infrastructure.Jobs;
using FluentAssertions;

namespace ColdHarbour.Infrastructure.Tests.Jobs;

public sealed class ArtCachePruneTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public ArtCachePruneTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private FileInfo Touch(string name, int sizeBytes, DateTimeOffset lastAccess)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        File.SetLastAccessTimeUtc(path, lastAccess.UtcDateTime);
        return new FileInfo(path);
    }

    [Fact]
    public void SelectToEvict_WhenUnderLimit_ReturnsEmpty()
    {
        var files = new[]
        {
            Touch("a.webp", 100, DateTimeOffset.UtcNow.AddHours(-2)),
            Touch("b.webp", 100, DateTimeOffset.UtcNow.AddHours(-1)),
        };
        var total = files.Sum(f => f.Length);

        var toEvict = ArtCachePruneJob.SelectToEvict(files, totalBytes: total, limitBytes: 1000);

        toEvict.Should().BeEmpty();
    }

    [Fact]
    public void SelectToEvict_WhenOverLimit_EvictsLruFirst()
    {
        var oldest = Touch("lru.webp", 200, DateTimeOffset.UtcNow.AddDays(-5));
        var newer = Touch("mru.webp", 200, DateTimeOffset.UtcNow);
        var files = new[] { oldest, newer };
        var total = files.Sum(f => f.Length);   // 400 bytes

        // limit is 250 — need to evict at least 150 bytes
        var toEvict = ArtCachePruneJob.SelectToEvict(files, totalBytes: total, limitBytes: 250);

        toEvict.Should().ContainSingle().Which.Name.Should().Be("lru.webp");
    }

    [Fact]
    public void SelectToEvict_EvictsEnoughToSatisfyLimit()
    {
        var files = new[]
        {
            Touch("old-1.webp", 300, DateTimeOffset.UtcNow.AddDays(-3)),
            Touch("old-2.webp", 300, DateTimeOffset.UtcNow.AddDays(-2)),
            Touch("new.webp",   300, DateTimeOffset.UtcNow),
        };
        var total = files.Sum(f => f.Length);   // 900 bytes

        // limit = 400, so we need to evict 500 bytes (first two files)
        var toEvict = ArtCachePruneJob.SelectToEvict(files, totalBytes: total, limitBytes: 400);

        toEvict.Should().HaveCount(2);
        toEvict.Select(f => f.Name).Should().BeEquivalentTo(["old-1.webp", "old-2.webp"]);
    }
}
