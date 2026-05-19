using System.Security.Claims;
using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Application.Playback;
using ColdHarbour.Application.Playback.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdHarbour.Api.Controllers;

[ApiController]
[Route("api/stream")]
[Authorize]
public sealed class StreamController(
    ITrackRepository trackRepo,
    IDeviceRepository deviceRepo,
    ITranscodeService transcodeService,
    IConfiguration config) : ControllerBase
{
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"]  = "audio/mpeg",
        [".flac"] = "audio/flac",
        [".m4a"]  = "audio/mp4",
        [".ogg"]  = "audio/ogg",
        [".opus"] = "audio/ogg",
        [".wav"]  = "audio/wav",
    };

    private string ContentRoot => config["COLDHARBOUR_CONTENT_ROOT"]
        ?? Path.Combine(Path.GetTempPath(), "coldharbour");

    // ── GET /api/stream/{trackId}?profile=original|opus-128|aac-192|mp3-192 ───────
    [HttpGet("{trackId:guid}")]
    public async Task<IActionResult> Stream(Guid trackId, [FromQuery] string? profile, CancellationToken ct)
    {
        var track = await trackRepo.FindByIdAsync(trackId, ct);
        if (track?.LocalPath is null)
            return NotFound();

        var fullPath = Path.Combine(ContentRoot, track.LocalPath.TrimStart('/'));
        if (!System.IO.File.Exists(fullPath))
            return NotFound();

        // Resolve profile: explicit param → device lookup → pass-through fallback
        var resolvedProfile = profile;
        if (string.IsNullOrEmpty(resolvedProfile))
        {
            var deviceIdClaim = User.FindFirstValue("deviceId");
            if (Guid.TryParse(deviceIdClaim, out var deviceId))
            {
                var device = await deviceRepo.FindByIdAsync(deviceId, ct);
                if (device is not null)
                {
                    resolvedProfile = ProfileSelector.Select(
                        track.Format,
                        device.SupportedCodecs,
                        device.PreferredProfile,
                        device.BitrateCap);
                }
            }
            resolvedProfile ??= "original";
        }

        string servePath;
        string cacheKey;

        if (resolvedProfile == "original")
        {
            servePath = fullPath;
            cacheKey = track.AudioSha1;
        }
        else
        {
            var transcodedPath = await transcodeService.GetOrTranscodeAsync(fullPath, track.AudioSha1, resolvedProfile, ct);
            if (transcodedPath is null)
                return NotFound();
            servePath = transcodedPath;
            cacheKey = $"{track.AudioSha1}-{resolvedProfile}";
        }

        var ext = Path.GetExtension(servePath);
        var mime = MimeTypes.TryGetValue(ext, out var m) ? m : "application/octet-stream";

        Response.Headers.ETag = $"\"{cacheKey}\"";
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return PhysicalFile(servePath, mime, enableRangeProcessing: true);
    }
}
