using ColdHarbour.Application.Library.Dtos;
using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ColdHarbour.Infrastructure.Library;

public sealed class LibraryReconciler(
    ColdHarbourDbContext db,
    ITrackIngestService ingestService,
    IConfiguration config,
    ILogger<LibraryReconciler> logger) : ILibraryReconciler
{
    private static readonly HashSet<string> AudioExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".flac", ".m4a", ".ogg", ".opus", ".wav" };

    private string ContentRoot => config["COLDHARBOUR_CONTENT_ROOT"]
        ?? Path.Combine(Path.GetTempPath(), "coldharbour");

    public async Task<LibrarySyncDiffDto> PreviewAsync(CancellationToken ct = default)
    {
        var libraryDir = Path.Combine(ContentRoot, "library");
        if (!Directory.Exists(libraryDir))
            return new LibrarySyncDiffDto([], [], []);

        var diskFiles = Directory
            .EnumerateFiles(libraryDir, "*", SearchOption.AllDirectories)
            .Where(f => AudioExtensions.Contains(Path.GetExtension(f)) &&
                        !f.Contains(Path.Combine(libraryDir, ".tmp")))
            .Select(f => "/" + Path.GetRelativePath(ContentRoot, f).Replace(Path.DirectorySeparatorChar, '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var dbTracks = await db.Tracks
            .Where(t => t.Provider == "local")
            .Select(t => new { t.LocalPath, t.AudioSha1 })
            .ToListAsync(ct);

        var dbPaths = dbTracks
            .Where(t => t.LocalPath is not null)
            .ToDictionary(t => t.LocalPath!, t => t.AudioSha1, StringComparer.OrdinalIgnoreCase);

        var added = diskFiles.Except(dbPaths.Keys)
            .Select(p => new LibrarySyncItemDto(p, Path.GetFileNameWithoutExtension(p), null))
            .ToList();

        var missing = dbPaths.Keys.Except(diskFiles)
            .Select(p => new LibrarySyncItemDto(p, Path.GetFileNameWithoutExtension(p), null))
            .ToList();

        return new LibrarySyncDiffDto(added, missing, Renamed: []);
    }

    public async Task ApplyAsync(CancellationToken ct = default)
    {
        // Acquire a session-level advisory lock so concurrent sync runs don't collide.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(20260420)", ct);
        try
        {
            var diff = await PreviewAsync(ct);

            foreach (var item in diff.Added)
            {
                // The file is already under library/ — register it in place; do not move it.
                await ingestService.IngestExistingFileAsync(item.Path, ct);
            }

            foreach (var item in diff.Missing)
            {
                var track = await db.Tracks.FirstOrDefaultAsync(t => t.LocalPath == item.Path, ct);
                if (track is not null)
                    db.Tracks.Remove(track);
            }

            await db.SaveChangesAsync(ct);
            logger.LogInformation("Library sync applied: +{Added} -{Missing}", diff.Added.Count, diff.Missing.Count);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(20260420)", ct);
        }
    }
}
