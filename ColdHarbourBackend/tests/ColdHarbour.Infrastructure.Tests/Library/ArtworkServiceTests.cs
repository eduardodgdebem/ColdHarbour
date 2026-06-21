using ColdHarbour.Infrastructure.Library;
using ColdHarbour.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ColdHarbour.Infrastructure.Tests.Library;

// SaveSourceAsync does not touch the database, so these run without Postgres/Docker.
public sealed class ArtworkServiceTests : IDisposable
{
    private readonly string _contentRoot =
        Path.Combine(Path.GetTempPath(), $"ch-art-{Guid.NewGuid()}");

    private ArtworkService CreateService()
    {
        var options = new DbContextOptionsBuilder<ColdHarbourDbContext>()
            .UseNpgsql("Host=unused;Database=unused")
            .Options;
        var db = new ColdHarbourDbContext(options);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["COLDHARBOUR_CONTENT_ROOT"] = _contentRoot
            })
            .Build();
        return new ArtworkService(db, config, NullLogger<ArtworkService>.Instance);
    }

    private static Stream ValidPng()
    {
        using var img = new Image<Rgba32>(8, 8);
        var ms = new MemoryStream();
        img.SaveAsPng(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task SaveSourceAsync_WithValidPng_ReturnsSha1AndWritesSourceAndThumbnails()
    {
        var svc = CreateService();
        using var png = ValidPng();

        var sha1 = await svc.SaveSourceAsync(png, "image/png");

        sha1.Should().MatchRegex("^[0-9a-f]{40}$");
        var artDir = Path.Combine(_contentRoot, "cache", "art");
        File.Exists(Path.Combine(artDir, $"{sha1}-source.png")).Should().BeTrue();
        File.Exists(Path.Combine(artDir, $"{sha1}-256.webp")).Should().BeTrue();
    }

    [Fact]
    public async Task SaveSourceAsync_RejectsNonImageBytes()
    {
        var svc = CreateService();
        using var notImage = new MemoryStream("this is plainly not an image"u8.ToArray());

        var act = () => svc.SaveSourceAsync(notImage, "image/png");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SaveSourceAsync_RejectsContentMismatchedMagicBytes()
    {
        // Claims JPEG but the bytes are not a real JPEG/PNG/WebP.
        var svc = CreateService();
        using var fake = new MemoryStream([0x00, 0x01, 0x02, 0x03, 0x04, 0x05]);

        var act = () => svc.SaveSourceAsync(fake, "image/jpeg");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SaveSourceAsync_RejectsOversizeContent()
    {
        var svc = CreateService();
        // 11 MB exceeds the 10 MB cap; PNG magic header so detection passes the
        // first gate and the size guard is what fires.
        var big = new byte[11 * 1024 * 1024];
        big[0] = 0x89; big[1] = 0x50; big[2] = 0x4E; big[3] = 0x47;
        big[4] = 0x0D; big[5] = 0x0A; big[6] = 0x1A; big[7] = 0x0A;
        using var stream = new MemoryStream(big);

        var act = () => svc.SaveSourceAsync(stream, "image/png");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
            Directory.Delete(_contentRoot, recursive: true);
    }
}
