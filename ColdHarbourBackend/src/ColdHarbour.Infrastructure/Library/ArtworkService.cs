using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace ColdHarbour.Infrastructure.Library;

public sealed class ArtworkService(
    ColdHarbourDbContext db,
    IConfiguration config,
    ILogger<ArtworkService> logger) : IArtworkService
{
    private static readonly int[] AllowedSizes = [64, 256, 1024];

    private string ContentRoot => config["COLDHARBOUR_CONTENT_ROOT"]
        ?? Path.Combine(Path.GetTempPath(), "coldharbour");

    public async Task<string?> GetThumbnailPathAsync(Guid albumId, int size, CancellationToken ct = default)
    {
        if (!AllowedSizes.Contains(size))
            size = 256;

        var album = await db.Albums.FirstOrDefaultAsync(a => a.Id == albumId, ct);
        if (album?.CoverArtSha1 is null)
            return null;

        var sha1 = album.CoverArtSha1;
        var artDir = Path.Combine(ContentRoot, "cache", "art");
        Directory.CreateDirectory(artDir);

        var thumbPath = Path.Combine(artDir, $"{sha1}-{size}.webp");

        if (File.Exists(thumbPath))
            return thumbPath;

        var sourcePath = Directory
            .EnumerateFiles(artDir, $"{sha1}-source.*")
            .FirstOrDefault();

        if (sourcePath is null)
            return null;

        try
        {
            using var image = await Image.LoadAsync(sourcePath, ct);
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(size, size),
                Mode = ResizeMode.Max
            }));
            await image.SaveAsWebpAsync(thumbPath, new WebpEncoder { Quality = 85 }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate {Size}px thumbnail for album {AlbumId}", size, albumId);
            return null;
        }

        return thumbPath;
    }

    public async Task<string?> GetCoverArtSha1Async(Guid albumId, CancellationToken ct = default)
    {
        return await db.Albums
            .Where(a => a.Id == albumId)
            .Select(a => a.CoverArtSha1)
            .FirstOrDefaultAsync(ct);
    }

    // Upload covers are capped well below the WS frame budget; covers are small.
    private const int MaxSourceBytes = 10 * 1024 * 1024; // 10 MB

    public async Task<string> SaveSourceAsync(Stream content, string contentType, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        // Buffer the upload (capped) so we can sniff magic bytes and hash it. Never
        // trust the client Content-Type — the byte signature decides the format.
        using var buffer = new MemoryStream();
        var pool = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(pool, ct)) > 0)
        {
            if (buffer.Length + read > MaxSourceBytes)
                throw new InvalidOperationException("Image exceeds the maximum allowed size.");
            buffer.Write(pool, 0, read);
        }

        var bytes = buffer.ToArray();
        var ext = DetectImageExtension(bytes)
            ?? throw new InvalidOperationException("Unsupported or invalid image. Expected JPEG, PNG, or WebP.");

        var sha1 = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes)).ToLowerInvariant();

        var artDir = Path.Combine(ContentRoot, "cache", "art");
        Directory.CreateDirectory(artDir);

        var sourcePath = Path.Combine(artDir, $"{sha1}-source.{ext}");
        if (!File.Exists(sourcePath))
            await File.WriteAllBytesAsync(sourcePath, bytes, ct);

        // Decode to validate the bytes are a real image, then materialize the
        // standard thumbnail sizes (also done lazily by GetThumbnailPathAsync).
        try
        {
            foreach (var size in AllowedSizes)
            {
                var thumbPath = Path.Combine(artDir, $"{sha1}-{size}.webp");
                if (File.Exists(thumbPath))
                    continue;
                using var image = Image.Load(bytes);
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(size, size),
                    Mode = ResizeMode.Max
                }));
                await image.SaveAsWebpAsync(thumbPath, new WebpEncoder { Quality = 85 }, ct);
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException("Image could not be decoded.", ex);
        }

        return sha1;
    }

    private static string? DetectImageExtension(ReadOnlySpan<byte> bytes)
    {
        // JPEG: FF D8 FF
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "jpg";
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E &&
            bytes[3] == 0x47 && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            return "png";
        // WebP: "RIFF"...."WEBP"
        if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            return "webp";
        return null;
    }
}
