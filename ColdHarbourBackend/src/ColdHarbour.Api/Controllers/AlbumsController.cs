using ColdHarbour.Application.Library.Commands;
using ColdHarbour.Application.Library.Queries;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdHarbour.Api.Controllers;

[ApiController]
[Route("api/albums")]
[Authorize]
public sealed class AlbumsController(IMediator mediator) : ControllerBase
{
    public sealed record UpdateAlbumBody(string Title, int? Year);

    // 10 MB matches IArtworkService's source cap.
    private const long MaxCoverBytes = 10 * 1024 * 1024;

    // ── GET /api/albums ──────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await mediator.Send(new GetAlbumsQuery(), ct));

    // ── GET /api/albums/{id} ─────────────────────────────────────────────────────
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var album = await mediator.Send(new GetAlbumQuery(id), ct);
        return album is null ? NotFound() : Ok(album);
    }

    // ── PATCH /api/albums/{id} ───────────────────────────────────────────────────
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAlbumBody body, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new UpdateAlbumCommand(id, body.Title, body.Year), ct);
            return NoContent();
        }
        catch (ValidationException ex) { return BadRequest(new { errors = ex.Errors.Select(e => e.ErrorMessage) }); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    // ── POST /api/albums/{id}/cover ──────────────────────────────────────────────
    [HttpPost("{id:guid}/cover")]
    [RequestSizeLimit(MaxCoverBytes)]
    public async Task<IActionResult> UploadCover(Guid id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No image provided." });

        try
        {
            await using var stream = file.OpenReadStream();
            await mediator.Send(new UpdateAlbumCoverCommand(id, stream, file.ContentType), ct);
            return NoContent();
        }
        catch (ValidationException ex) { return BadRequest(new { errors = ex.Errors.Select(e => e.ErrorMessage) }); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }
}
