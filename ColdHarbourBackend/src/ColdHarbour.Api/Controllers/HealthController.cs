using ColdHarbour.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace ColdHarbour.Api.Controllers;

[ApiController]
public sealed class HealthController(
    IConfiguration config,
    IServiceProvider serviceProvider) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("/api/health")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var dbStatus = await CheckDbAsync(ct);
        var cacheSize = GetCacheSizes();
        var overallStatus = dbStatus == "ok" ? "ok" : "degraded";

        return Ok(new
        {
            status = overallStatus,
            db = dbStatus,
            cacheSize
        });
    }

    private async Task<string> CheckDbAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = HttpContext.RequestServices.CreateAsyncScope();
            var db = scope.ServiceProvider.GetService<ColdHarbourDbContext>();
            if (db is null) return "unconfigured";
            return await db.Database.CanConnectAsync(ct) ? "ok" : "error";
        }
        catch
        {
            return "error";
        }
    }

    private object GetCacheSizes()
    {
        var contentRoot = config["COLDHARBOUR_CONTENT_ROOT"]
            ?? Path.Combine(Path.GetTempPath(), "coldharbour");

        return new
        {
            transcodesMb = DirectorySizeMb(Path.Combine(contentRoot, "cache", "transcodes")),
            artMb = DirectorySizeMb(Path.Combine(contentRoot, "cache", "art"))
        };
    }

    private static long DirectorySizeMb(string path)
    {
        if (!Directory.Exists(path)) return 0;
        return new DirectoryInfo(path)
            .GetFiles("*", SearchOption.AllDirectories)
            .Sum(f => f.Length) / 1_048_576;
    }
}
