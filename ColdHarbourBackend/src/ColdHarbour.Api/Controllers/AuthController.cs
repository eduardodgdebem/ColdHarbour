using ColdHarbour.Application.Identity.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ColdHarbour.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IMediator mediator) : ControllerBase
{
    private const string RefreshTokenCookieName = "refreshToken";
    private static readonly CookieOptions RefreshCookieOptions = new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
        MaxAge = TimeSpan.FromDays(14)
    };

    // ── POST /api/auth/register ──────────────────────────────────────────────────
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest req,
        CancellationToken ct)
    {
        try
        {
            await mediator.Send(new RegisterUserCommand(req.Email, req.Name, req.Password), ct);
            return StatusCode(StatusCodes.Status201Created);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    // ── POST /api/auth/login ─────────────────────────────────────────────────────
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest req,
        CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(
                new AuthenticateUserCommand(req.Email, req.Password, req.DeviceId), ct);

            Response.Cookies.Append(RefreshTokenCookieName, result.RefreshTokenPlaintext, RefreshCookieOptions);

            return Ok(new
            {
                result.Dto.AccessToken,
                result.Dto.UserId,
                result.Dto.Email
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }

    // ── POST /api/auth/refresh ───────────────────────────────────────────────────
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshRequest req,
        CancellationToken ct)
    {
        var plaintext = Request.Cookies[RefreshTokenCookieName];
        if (string.IsNullOrEmpty(plaintext))
            return Unauthorized();

        try
        {
            var result = await mediator.Send(
                new RefreshAccessTokenCommand(plaintext, req.DeviceId), ct);

            Response.Cookies.Append(RefreshTokenCookieName, result.RefreshTokenPlaintext, RefreshCookieOptions);

            return Ok(new { result.Dto.AccessToken });
        }
        catch (UnauthorizedAccessException)
        {
            Response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions { Path = "/api/auth" });
            return Unauthorized();
        }
    }

    // ── POST /api/auth/logout ────────────────────────────────────────────────────
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var plaintext = Request.Cookies[RefreshTokenCookieName];
        if (!string.IsNullOrEmpty(plaintext))
            await mediator.Send(new LogoutCommand(plaintext), ct);

        Response.Cookies.Append(RefreshTokenCookieName, "", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            MaxAge = TimeSpan.Zero
        });

        return NoContent();
    }

    // ── request models ───────────────────────────────────────────────────────────

    public sealed record RegisterRequest(string Email, string Name, string Password);

    public sealed record LoginRequest(string Email, string Password, string DeviceId);

    public sealed record RefreshRequest(string DeviceId);
}
