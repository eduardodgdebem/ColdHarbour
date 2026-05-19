using ColdHarbour.Application.Library.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdHarbour.Api.Controllers;

[ApiController]
[Route("api/stream")]
[Authorize]
public sealed class StreamController(ITrackRepository trackRepo, IConfiguration config) : ControllerBase
{
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "audio/mpeg",
        [".flac"] = "audio/flac",
        [".m4a"] = "audio/mp4",
        [".ogg"] = "audio/ogg",
        [".opus"] = "audio/ogg",
        [".wav"] = "audio/wav",
    };

    private string ContentRoot => config["COLDHARBOUR_CONTENT_ROOT"]
        ?? Path.Combine(Path.GetTempPath(), "coldharbour");

    // ── GET /api/stream/{trackId} ────────────────────────────────────────────────
    [HttpGet("{trackId:guid}")]
    public async Task<IActionResult> Stream(Guid trackId, CancellationToken ct)
    {
        var track = await trackRepo.FindByIdAsync(trackId, ct);
        if (track?.LocalPath is null)
            return NotFound();

        var fullPath = Path.Combine(ContentRoot, track.LocalPath.TrimStart('/'));
        if (!System.IO.File.Exists(fullPath))
            return NotFound();

        var ext = Path.GetExtension(fullPath);
        var mime = MimeTypes.TryGetValue(ext, out var m) ? m : "application/octet-stream";

        Response.Headers.ETag = $"\"{track.AudioSha1}\"";
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return PhysicalFile(fullPath, mime, enableRangeProcessing: true);
    }
}
