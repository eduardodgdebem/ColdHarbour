using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ColdHarbour.Application.Library.Dtos;
using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Domain.Library;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TagLib;
using File = TagLib.File;

namespace ColdHarbour.Infrastructure.Library;

public sealed class TrackIngestService(
    ITrackRepository repo,
    IConfiguration config,
    ILogger<TrackIngestService> logger) : ITrackIngestService
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".flac", ".m4a", ".ogg", ".opus", ".wav" };

    // Serializes the artist/album find-or-create across concurrent uploads. The client
    // fires every selected file in parallel; without this, each request's find-or-create
    // misses the others' uncommitted rows and the same album splits into N copies.
    private static readonly SemaphoreSlim CatalogLock = new(1, 1);

    private static readonly Regex SafeNameRegex = new(@"[^\w\s\-\(\)\[\]\.']", RegexOptions.Compiled);

    private string ContentRoot => config["COLDHARBOUR_CONTENT_ROOT"]
        ?? Path.Combine(Path.GetTempPath(), "coldharbour");

    public async Task<TrackUploadResultDto> IngestAsync(Stream fileStream, string fileName, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName);
        if (!SupportedExtensions.Contains(ext))
            throw new InvalidOperationException($"Unsupported audio format: {ext}");

        var tmpDir = Path.Combine(ContentRoot, "library", ".tmp");
        Directory.CreateDirectory(tmpDir);

        var tmpPath = Path.Combine(tmpDir, $"{Guid.NewGuid()}{ext}");

        await using (var tmpFile = System.IO.File.Create(tmpPath))
            await fileStream.CopyToAsync(tmpFile, ct);

        try
        {
            var audioSha1 = await ComputeSha1Async(tmpPath, ct);

            var existing = await repo.FindByAudioSha1Async(audioSha1, ct);
            if (existing is not null)
            {
                System.IO.File.Delete(tmpPath);
                return new TrackUploadResultDto(existing.Id, existing.AlbumId, true);
            }

            // Derive the canonical destination from tags, then move the upload into place.
            string canonicalPath;
            using (var tagFile = File.Create(tmpPath))
            {
                var tags = tagFile.Tag;
                var artistName = AlbumArtistNormalizer.Normalize(tags.FirstAlbumArtist ?? tags.FirstPerformer);
                var albumTitle = tags.Album ?? "Unknown Album";
                var trackTitle = tags.Title ?? Path.GetFileNameWithoutExtension(fileName);
                canonicalPath = BuildCanonicalPath(ContentRoot, artistName, albumTitle, trackTitle, ext);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(canonicalPath)!);
            System.IO.File.Move(tmpPath, canonicalPath, overwrite: false);

            return await RegisterTrackAsync(canonicalPath, ToRelativePath(canonicalPath), audioSha1, fileName, ext, ct);
        }
        catch
        {
            if (System.IO.File.Exists(tmpPath))
                System.IO.File.Delete(tmpPath);
            throw;
        }
    }

    public async Task<TrackUploadResultDto> IngestExistingFileAsync(string relativePath, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(relativePath);
        if (!SupportedExtensions.Contains(ext))
            throw new InvalidOperationException($"Unsupported audio format: {ext}");

        var normalizedRelative = "/" + relativePath.TrimStart('/');
        var fullPath = Path.Combine(ContentRoot, normalizedRelative.TrimStart('/'));
        if (!System.IO.File.Exists(fullPath))
            throw new FileNotFoundException("Library file not found.", fullPath);

        var audioSha1 = await ComputeSha1Async(fullPath, ct);

        var existing = await repo.FindByAudioSha1Async(audioSha1, ct);
        if (existing is not null)
            return new TrackUploadResultDto(existing.Id, existing.AlbumId, true);

        // The file already lives at its destination — register it in place, never move it.
        return await RegisterTrackAsync(fullPath, normalizedRelative, audioSha1, fullPath, ext, ct);
    }

    /// <summary>
    /// Reads tags from a file already on disk and creates the Artist/Album/Track rows,
    /// recording <paramref name="relativePath"/> as the track's location. Does not move files.
    /// </summary>
    private async Task<TrackUploadResultDto> RegisterTrackAsync(
        string fileOnDisk, string relativePath, string audioSha1, string fallbackTitleName, string ext, CancellationToken ct)
    {
        using var tagFile = File.Create(fileOnDisk);
        var tags = tagFile.Tag;

        // Group by the *album artist*, collapsing "feat." performers so every track on
        // an album lands on the same album rather than splitting per featured guest.
        // The raw performer is kept on the track so the feature credit stays visible.
        var performer = tags.FirstPerformer;
        var artistName = AlbumArtistNormalizer.Normalize(tags.FirstAlbumArtist ?? performer);
        var albumTitle = tags.Album ?? "Unknown Album";
        var trackTitle = tags.Title ?? Path.GetFileNameWithoutExtension(fallbackTitleName);
        var year = tags.Year > 0 ? (int?)tags.Year : null;
        var trackNumber = tags.Track > 0 ? (int?)tags.Track : null;
        var durationSec = tagFile.Properties.Duration;
        var bitrate = tagFile.Properties.AudioBitrate > 0 ? tagFile.Properties.AudioBitrate : 128;
        var format = ext.TrimStart('.').ToLowerInvariant();

        var artSha1 = await ExtractArtworkAsync(tagFile, ct);
        var album = await EnsureArtistAndAlbumAsync(artistName, albumTitle, year, artSha1, ct);

        var track = Track.Create(
            title: trackTitle,
            albumId: album.Id,
            duration: durationSec,
            provider: "local",
            format: format,
            bitrate: bitrate,
            audioSha1: audioSha1,
            localPath: relativePath,
            trackNumber: trackNumber,
            performer: performer);

        await repo.AddTrackAsync(track, ct);
        await repo.SaveChangesAsync(ct);

        logger.LogInformation("Ingested track {Title} → {Path}", track.Title, relativePath);
        return new TrackUploadResultDto(track.Id, album.Id, false);
    }

    /// <summary>
    /// Find-or-create the artist and album under a process-wide lock so concurrent
    /// uploads of the same album converge on one artist + one album row instead of
    /// racing and each creating their own. Each create is committed inside the lock,
    /// so the next caller's lookup sees it. Sets the cover on first art seen.
    /// </summary>
    internal async Task<Album> EnsureArtistAndAlbumAsync(
        string artistName, string albumTitle, int? year, string? artSha1, CancellationToken ct)
    {
        await CatalogLock.WaitAsync(ct);
        try
        {
            var artist = await repo.FindArtistByNameAsync(artistName, ct);
            if (artist is null)
            {
                artist = Artist.Create(artistName);
                await repo.AddArtistAsync(artist, ct);
                await repo.SaveChangesAsync(ct);
            }

            var album = await repo.FindAlbumByArtistAndTitleAsync(artist.Id, albumTitle, ct);
            if (album is null)
            {
                album = Album.Create(albumTitle, artist.Id, year);
                await repo.AddAlbumAsync(album, ct);
                await repo.SaveChangesAsync(ct);
            }

            if (artSha1 is not null && album.CoverArtSha1 is null)
            {
                album.UpdateCoverArt(artSha1);
                await repo.SaveChangesAsync(ct);
            }

            return album;
        }
        finally
        {
            CatalogLock.Release();
        }
    }

    private string ToRelativePath(string fullPath) =>
        "/" + Path.GetRelativePath(ContentRoot, fullPath).Replace(Path.DirectorySeparatorChar, '/');

    public Task RemoveTrackFilesAsync(string? localPath, string audioSha1, CancellationToken ct = default)
    {
        if (localPath is not null)
        {
            var fullPath = Path.Combine(ContentRoot, localPath.TrimStart('/'));
            if (System.IO.File.Exists(fullPath))
            {
                try { System.IO.File.Delete(fullPath); }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not delete audio file {Path}", fullPath);
                }
            }
        }

        // Delete any cached transcodes for this AudioSha1
        var transcodeDir = Path.Combine(ContentRoot, "cache", "transcodes");
        if (Directory.Exists(transcodeDir))
        {
            foreach (var f in Directory.GetFiles(transcodeDir, $"*{audioSha1}*"))
            {
                try { System.IO.File.Delete(f); }
                catch (Exception ex) { logger.LogWarning(ex, "Could not delete transcode {Path}", f); }
            }
        }

        return Task.CompletedTask;
    }

    private static async Task<string> ComputeSha1Async(string path, CancellationToken ct)
    {
        await using var stream = System.IO.File.OpenRead(path);
        var hash = await SHA1.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<string?> ExtractArtworkAsync(File tagFile, CancellationToken ct)
    {
        var pic = tagFile.Tag.Pictures.FirstOrDefault();
        if (pic is null)
            return null;

        var artDir = Path.Combine(ContentRoot, "cache", "art");
        Directory.CreateDirectory(artDir);

        var artBytes = pic.Data.Data;
        using var sha1 = SHA1.Create();
        var artHash = Convert.ToHexString(sha1.ComputeHash(artBytes)).ToLowerInvariant();

        var artExt = pic.MimeType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };

        var artPath = Path.Combine(artDir, $"{artHash}-source{artExt}");
        if (!System.IO.File.Exists(artPath))
            await System.IO.File.WriteAllBytesAsync(artPath, artBytes, ct);

        return artHash;
    }

    private static string BuildCanonicalPath(string contentRoot, string artist, string album, string title, string ext)
    {
        var safe = (string s) => SafeNameRegex.Replace(s, "_").Trim().TrimEnd('.');
        return Path.Combine(contentRoot, "library", safe(artist), safe(album), safe(title) + ext);
    }
}
