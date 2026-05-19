using ColdHarbour.Application.Library.Commands;
using ColdHarbour.Application.Library.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdHarbour.Api.Controllers;

[ApiController]
[Route("api/library")]
[Authorize]
public sealed class LibraryController(IMediator mediator) : ControllerBase
{
    // ── POST /api/library/tracks ─────────────────────────────────────────────────
    [HttpPost("tracks")]
    [RequestSizeLimit(200 * 1024 * 1024)] // 200 MB
    public async Task<IActionResult> UploadTrack(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file provided." });

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await mediator.Send(new UploadTrackCommand(stream, file.FileName), ct);
            return result.AlreadyExisted
                ? Ok(new { result.TrackId, result.AlbumId, result.AlreadyExisted })
                : StatusCode(StatusCodes.Status201Created, new { result.TrackId, result.AlbumId, result.AlreadyExisted });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── DELETE /api/library/tracks/{id} ─────────────────────────────────────────
    [HttpDelete("tracks/{id:guid}")]
    public async Task<IActionResult> DeleteTrack(Guid id, CancellationToken ct)
    {
        await mediator.Send(new DeleteTrackCommand(id), ct);
        return NoContent();
    }

    // ── GET /api/library/sync/preview ───────────────────────────────────────────
    [HttpGet("sync/preview")]
    public async Task<IActionResult> PreviewSync(CancellationToken ct)
    {
        var diff = await mediator.Send(new PreviewLibrarySyncQuery(), ct);
        return Ok(diff);
    }

    // ── POST /api/library/sync ───────────────────────────────────────────────────
    [HttpPost("sync")]
    public async Task<IActionResult> ApplySync(CancellationToken ct)
    {
        await mediator.Send(new SyncLibraryCommand(), ct);
        return NoContent();
    }
}
