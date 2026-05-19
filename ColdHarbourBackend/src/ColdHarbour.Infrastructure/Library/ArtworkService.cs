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
        var thumbPath = Path.Combine(artDir, $"{sha1}-{size}.webp");

        if (File.Exists(thumbPath))
            return thumbPath;

        var sourcePath = Directory
            .EnumerateFiles(artDir, $"{sha1}-source.*")
            .FirstOrDefault();

        if (sourcePath is null)
            return null;

        Directory.CreateDirectory(artDir);

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
}
