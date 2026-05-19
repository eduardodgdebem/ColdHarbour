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

            using var tagFile = File.Create(tmpPath);
            var tags = tagFile.Tag;

            var artistName = tags.FirstAlbumArtist ?? tags.FirstPerformer ?? "Unknown Artist";
            var albumTitle = tags.Album ?? "Unknown Album";
            var trackTitle = tags.Title ?? Path.GetFileNameWithoutExtension(fileName);
            var year = tags.Year > 0 ? (int?)tags.Year : null;
            var trackNumber = tags.Track > 0 ? (int?)tags.Track : null;
            var durationSec = tagFile.Properties.Duration;
            var bitrate = tagFile.Properties.AudioBitrate > 0 ? tagFile.Properties.AudioBitrate : 128;
            var format = ext.TrimStart('.').ToLowerInvariant();

            var artist = await repo.FindArtistByNameAsync(artistName, ct);
            if (artist is null)
            {
                artist = Artist.Create(artistName);
                await repo.AddArtistAsync(artist, ct);
            }

            var album = await repo.FindAlbumByArtistAndTitleAsync(artist.Id, albumTitle, ct);
            if (album is null)
            {
                album = Album.Create(albumTitle, artist.Id, year);
                await repo.AddAlbumAsync(album, ct);
            }

            var canonicalPath = BuildCanonicalPath(ContentRoot, artistName, albumTitle, trackTitle, ext);
            Directory.CreateDirectory(Path.GetDirectoryName(canonicalPath)!);

            // Extract embedded artwork before moving the file
            var artSha1 = await ExtractArtworkAsync(tagFile, ct);
            if (artSha1 is not null)
                album.UpdateCoverArt(artSha1);

            System.IO.File.Move(tmpPath, canonicalPath, overwrite: false);

            var relativePath = "/" + Path.GetRelativePath(ContentRoot, canonicalPath)
                .Replace(Path.DirectorySeparatorChar, '/');

            var track = Track.Create(
                title: trackTitle,
                albumId: album.Id,
                duration: durationSec,
                provider: "local",
                format: format,
                bitrate: bitrate,
                audioSha1: audioSha1,
                localPath: relativePath,
                trackNumber: trackNumber);

            await repo.AddTrackAsync(track, ct);
            await repo.SaveChangesAsync(ct);

            logger.LogInformation("Ingested track {Title} → {Path}", track.Title, relativePath);
            return new TrackUploadResultDto(track.Id, album.Id, false);
        }
        catch
        {
            if (System.IO.File.Exists(tmpPath))
                System.IO.File.Delete(tmpPath);
            throw;
        }
    }

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
