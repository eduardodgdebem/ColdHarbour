using System.Security.Claims;
using ColdHarbour.Application.Playback.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdHarbour.Api.Controllers;

[ApiController]
[Route("api/devices")]
[Authorize]
public sealed class DevicesController(IMediator mediator) : ControllerBase
{
    // ── POST /api/devices ────────────────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Register([FromBody] RegisterDeviceRequest req, CancellationToken ct)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        await mediator.Send(new RegisterDeviceCommand(
            req.DeviceId,
            userId,
            req.Name,
            Request.Headers.UserAgent.ToString(),
            req.SupportedCodecs,
            req.PreferredProfile,
            req.BitrateCap), ct);

        return NoContent();
    }

    public sealed record RegisterDeviceRequest(
        Guid DeviceId,
        string Name,
        IReadOnlyList<string> SupportedCodecs,
        string PreferredProfile,
        int? BitrateCap = null);
}
