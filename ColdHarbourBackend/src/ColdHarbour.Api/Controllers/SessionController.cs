using System.Security.Claims;
using ColdHarbour.Application.Playback.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdHarbour.Api.Controllers;

[ApiController]
[Route("api/session")]
[Authorize]
public sealed class SessionController(IMediator mediator) : ControllerBase
{
    private Guid UserId => Guid.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var dto = await mediator.Send(new GetActiveSessionQuery(UserId), ct);
        return dto is null ? NoContent() : Ok(dto);
    }
}
