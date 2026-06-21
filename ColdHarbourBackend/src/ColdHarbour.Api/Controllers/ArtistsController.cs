using ColdHarbour.Application.Library.Commands;
using ColdHarbour.Application.Library.Queries;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdHarbour.Api.Controllers;

[ApiController]
[Route("api/artists")]
[Authorize]
public sealed class ArtistsController(IMediator mediator) : ControllerBase
{
    public sealed record RenameArtistBody(string Name);

    // ── GET /api/artists ─────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await mediator.Send(new GetArtistsQuery(), ct));

    // ── GET /api/artists/{id} ────────────────────────────────────────────────────
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var artist = await mediator.Send(new GetArtistQuery(id), ct);
        return artist is null ? NotFound() : Ok(artist);
    }

    // ── PATCH /api/artists/{id} ──────────────────────────────────────────────────
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Rename(Guid id, [FromBody] RenameArtistBody body, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new RenameArtistCommand(id, body.Name), ct);
            return NoContent();
        }
        catch (ValidationException ex) { return BadRequest(new { errors = ex.Errors.Select(e => e.ErrorMessage) }); }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}
