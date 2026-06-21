using ColdHarbour.Application.Library.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdHarbour.Api.Controllers;

[ApiController]
[Route("api/artists")]
[Authorize]
public sealed class ArtistsController(IMediator mediator) : ControllerBase
{
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
}
