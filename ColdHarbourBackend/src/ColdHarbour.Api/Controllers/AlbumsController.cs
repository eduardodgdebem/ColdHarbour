using ColdHarbour.Application.Library.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdHarbour.Api.Controllers;

[ApiController]
[Route("api/albums")]
[Authorize]
public sealed class AlbumsController(IMediator mediator) : ControllerBase
{
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
}
