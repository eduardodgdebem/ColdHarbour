using System.Security.Claims;
using ColdHarbour.Application.Playback.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

// ListDevicesQuery lives in Commands namespace (file: Queries/ListDevicesQuery.cs)
// Fully qualified to avoid ambiguity.

namespace ColdHarbour.Api.Controllers;

[ApiController]
[Route("api/devices")]
[Authorize]
public sealed class DevicesController(IMediator mediator) : ControllerBase
{
    private Guid UserId => Guid.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    // ── GET /api/devices ─────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var devices = await mediator.Send(new ListDevicesQuery(UserId), ct);
        return Ok(devices);
    }

    // ── POST /api/devices ────────────────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Register([FromBody] RegisterDeviceRequest req, CancellationToken ct)
    {
        var userId = UserId;

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
