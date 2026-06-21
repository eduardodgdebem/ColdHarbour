using ColdHarbour.Application.Library.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdHarbour.Api.Controllers;

[ApiController]
[Route("api/artwork")]
[Authorize]
public sealed class ArtworkController(IArtworkService artworkService) : ControllerBase
{
    private static readonly int[] AllowedSizes = [64, 256, 1024];

    // ── GET /api/artwork/{albumId}?size=64|256|1024 ──────────────────────────────
    [HttpGet("{albumId:guid}")]
    [ResponseCache(Duration = 31_536_000)] // 1 year — immutable content-addressed
    public async Task<IActionResult> GetArtwork(Guid albumId, [FromQuery] int size = 256, CancellationToken ct = default)
    {
        if (!AllowedSizes.Contains(size))
            size = 256;

        var path = await artworkService.GetThumbnailPathAsync(albumId, size, ct);
        if (path is null)
            return NotFound();

        // ETag is versioned by the cover sha1 so a re-uploaded cover invalidates the
        // otherwise-immutable cached response (clients also pass ?v={sha1} on the URL).
        var sha1 = await artworkService.GetCoverArtSha1Async(albumId, ct);
        Response.Headers.ETag = $"\"{albumId}-{size}-{sha1}\"";
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return PhysicalFile(path, "image/webp");
    }
}
