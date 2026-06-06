using ColdHarbour.Infrastructure.Library;
using ColdHarbour.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace ColdHarbour.Infrastructure.Tests.Library;

public class LibraryReconcilerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private string _contentRoot = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        _contentRoot = Path.Combine(Path.GetTempPath(), "coldharbour-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_contentRoot);
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        if (Directory.Exists(_contentRoot))
            Directory.Delete(_contentRoot, recursive: true);
    }

    private ColdHarbourDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ColdHarbourDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new ColdHarbourDbContext(options);
    }

    private LibraryReconciler CreateReconciler(ColdHarbourDbContext db)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["COLDHARBOUR_CONTENT_ROOT"] = _contentRoot,
            })
            .Build();

        var ingest = new TrackIngestService(
            new TrackRepository(db), config, NullLogger<TrackIngestService>.Instance);

        return new LibraryReconciler(db, ingest, config, NullLogger<LibraryReconciler>.Instance);
    }

    [Fact]
    public async Task ApplyAsync_RegistersFileAlreadyOnDisk_WithoutMovingOrDuplicatingIt()
    {
        // A file dropped directly onto the mount, sitting where ingest would
        // otherwise place it (no tags → "Unknown Artist"/"Unknown Album").
        var albumDir = Path.Combine(_contentRoot, "library", "Unknown Artist", "Unknown Album");
        Directory.CreateDirectory(albumDir);
        var trackPath = Path.Combine(albumDir, "song.wav");
        await File.WriteAllBytesAsync(trackPath, BuildWav(seconds: 1));

        await using (var db = CreateContext())
        {
            await CreateReconciler(db).ApplyAsync();
        }

        await using (var verify = CreateContext())
        {
            var tracks = await verify.Tracks.Where(t => t.Provider == "local").ToListAsync();
            tracks.Should().HaveCount(1, "the dropped file should be registered exactly once");
            tracks[0].LocalPath.Should().Be("/library/Unknown Artist/Unknown Album/song.wav");
        }

        File.Exists(trackPath).Should().BeTrue("the original file must not be moved or deleted");
        Directory.EnumerateFiles(Path.Combine(_contentRoot, "library"), "*.wav", SearchOption.AllDirectories)
            .Should().ContainSingle("the reconciler must not duplicate the file into a canonical path");
    }

    [Fact]
    public async Task ApplyAsync_IsIdempotent_WhenRunTwice()
    {
        var albumDir = Path.Combine(_contentRoot, "library", "Unknown Artist", "Unknown Album");
        Directory.CreateDirectory(albumDir);
        await File.WriteAllBytesAsync(Path.Combine(albumDir, "song.wav"), BuildWav(seconds: 1));

        await using (var db = CreateContext()) await CreateReconciler(db).ApplyAsync();
        await using (var db = CreateContext()) await CreateReconciler(db).ApplyAsync();

        await using var verify = CreateContext();
        var count = await verify.Tracks.CountAsync(t => t.Provider == "local");
        count.Should().Be(1, "re-running sync on an unchanged library is a no-op");
    }

    // Minimal valid 8-bit mono PCM WAV so TagLib can read duration without an external fixture.
    private static byte[] BuildWav(int seconds)
    {
        const int sampleRate = 8000;
        const short channels = 1;
        const short bitsPerSample = 8;
        var dataSize = sampleRate * seconds;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataSize);
        w.Write("WAVE"u8.ToArray());

        w.Write("fmt "u8.ToArray());
        w.Write(16);                          // PCM fmt chunk size
        w.Write((short)1);                    // audio format = PCM
        w.Write(channels);
        w.Write(sampleRate);
        w.Write(sampleRate * channels * bitsPerSample / 8); // byte rate
        w.Write((short)(channels * bitsPerSample / 8));     // block align
        w.Write(bitsPerSample);

        w.Write("data"u8.ToArray());
        w.Write(dataSize);
        w.Write(Enumerable.Repeat((byte)128, dataSize).ToArray()); // 8-bit silence

        w.Flush();
        return ms.ToArray();
    }
}
