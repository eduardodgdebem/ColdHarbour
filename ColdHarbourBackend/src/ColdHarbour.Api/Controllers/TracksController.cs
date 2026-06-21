using ColdHarbour.Application.Library.Commands;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdHarbour.Api.Controllers;

[ApiController]
[Route("api/tracks")]
[Authorize]
public sealed class TracksController(IMediator mediator) : ControllerBase
{
    public sealed record UpdateTrackBody(string Title, int? TrackNumber);

    // ── PATCH /api/tracks/{id} ───────────────────────────────────────────────────
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTrackBody body, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new UpdateTrackCommand(id, body.Title, body.TrackNumber), ct);
            return NoContent();
        }
        catch (ValidationException ex) { return BadRequest(new { errors = ex.Errors.Select(e => e.ErrorMessage) }); }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}
