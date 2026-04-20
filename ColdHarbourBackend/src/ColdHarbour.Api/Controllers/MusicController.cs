using ColdHarbour.Application.Library.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ColdHarbour.Api.Controllers;

[ApiController]
[Route("api/music")]
public class MusicController : ControllerBase
{
    private readonly IMediator _mediator;

    public MusicController(IMediator mediator) => _mediator = mediator;

    [HttpGet("playlist/{id}")]
    public async Task<IActionResult> GetPlaylist(int id)
    {
        var result = await _mediator.Send(new GetPlaylistQuery(id));
        return Ok(result);
    }
}
