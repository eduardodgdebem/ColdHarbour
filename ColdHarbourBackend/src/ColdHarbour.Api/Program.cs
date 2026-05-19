using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using ColdHarbour.Application;
using ColdHarbour.Application.Identity.Commands;
using ColdHarbour.Application.Identity.Ports;
using ColdHarbour.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.IdentityModel.Tokens;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .WriteTo.Console());

    // ── CORS (environment-aware) ─────────────────────────────────────────────────
    if (builder.Environment.IsDevelopment())
    {
        // AllowAnyOrigin() is incompatible with AllowCredentials() (refresh-token cookie).
        // Pin to the ng serve origin so withCredentials requests work in local dev.
        builder.Services.AddCors(options =>
            options.AddPolicy("AllowAll",
                p => p.WithOrigins("http://localhost:4200")
                       .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
    }
    else
    {
        var origin = builder.Configuration["COLDHARBOUR_PUBLIC_ORIGIN"]
            ?? throw new InvalidOperationException("COLDHARBOUR_PUBLIC_ORIGIN must be set in Production.");
        builder.Services.AddCors(options =>
            options.AddPolicy("Production",
                p => p.WithOrigins(origin).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
    }

    // ── JWT authentication ───────────────────────────────────────────────────────
    // JWT options are configured via IConfiguration injection so that test factories
    // (which inject config via ConfigureAppConfiguration) are seen at resolution time.
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer();

    builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
        .Configure<IConfiguration>((opts, config) =>
        {
            var key = config["COLDHARBOUR_JWT_SIGNING_KEY"]
                ?? throw new InvalidOperationException("COLDHARBOUR_JWT_SIGNING_KEY is not configured.");
            opts.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = config["COLDHARBOUR_JWT_ISSUER"] ?? "coldharbour",
                ValidateAudience = true,
                ValidAudience = config["COLDHARBOUR_JWT_AUDIENCE"] ?? "coldharbour-web",
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
            };
            // Browser img/audio elements cannot attach an Authorization header.
            // Fall back to the media_token HttpOnly cookie for /api/stream and /api/artwork requests.
            opts.Events = new JwtBearerEvents
            {
                OnMessageReceived = ctx =>
                {
                    if (string.IsNullOrEmpty(ctx.Token))
                        ctx.Token = ctx.Request.Cookies["media_token"];
                    return Task.CompletedTask;
                }
            };
        });

    builder.Services.AddAuthorization();

    // ── Rate limiting ────────────────────────────────────────────────────────────
    // Non-production environments (Development, Testing) use a very high limit so
    // integration tests don't exhaust the rate-limiter window mid-suite.
    var isProduction = builder.Environment.IsProduction();
    builder.Services.AddRateLimiter(opts =>
    {
        opts.AddFixedWindowLimiter("login", o =>
        {
            o.PermitLimit = isProduction ? 5 : 10_000;
            o.Window = TimeSpan.FromMinutes(1);
            o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            o.QueueLimit = 0;
        });
        opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    });

    // ── Forwarded headers (Caddy/tunnel trust) ───────────────────────────────────
    builder.Services.Configure<ForwardedHeadersOptions>(opts =>
    {
        opts.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // Trust any internal proxy for now; Phase 7 will lock to specific Caddy IP.
        opts.KnownNetworks.Clear();
        opts.KnownProxies.Clear();
    });

    // ── MVC with global [Authorize] ──────────────────────────────────────────────
    builder.Services.AddControllers(opts =>
    {
        opts.Filters.Add(new AuthorizeFilter());
    });

    builder.Services.AddEndpointsApiExplorer();

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddScoped<ColdHarbour.Api.Playback.PlaybackSessionHub>();

    var app = builder.Build();

    // ── Database migration ───────────────────────────────────────────────────────
    await app.Services.MigrateDatabaseAsync();

    // ── Bootstrap owner ──────────────────────────────────────────────────────────
    using (var scope = app.Services.CreateScope())
    {
        var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        if (!await userRepo.AnyUsersExistAsync())
        {
            var email = app.Configuration["COLDHARBOUR_BOOTSTRAP_EMAIL"];
            var password = app.Configuration["COLDHARBOUR_BOOTSTRAP_PASSWORD"];
            if (email != null && password != null)
            {
                var mediator = scope.ServiceProvider.GetRequiredService<MediatR.IMediator>();
                await mediator.Send(new RegisterUserCommand(email, "Owner", password));
                Log.Information("Bootstrap owner created: {Email}", email);
            }
        }
    }

    // ── Pipeline ─────────────────────────────────────────────────────────────────
    app.UseForwardedHeaders();

    app.UseRouting();

    if (app.Environment.IsDevelopment())
        app.UseCors("AllowAll");
    else
        app.UseCors("Production");

    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseStaticFiles();
    app.UseWebSockets();
    app.MapControllers();

    app.Map("/ws/playback", async ctx =>
    {
        if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
        var ws = await ctx.WebSockets.AcceptWebSocketAsync();
        var hub = ctx.RequestServices.GetRequiredService<ColdHarbour.Api.Playback.PlaybackSessionHub>();
        await hub.HandleAsync(ctx, ws);
    });

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
