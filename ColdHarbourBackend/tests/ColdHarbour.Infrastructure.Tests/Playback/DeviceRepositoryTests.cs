using ColdHarbour.Domain.Playback;
using ColdHarbour.Infrastructure.Persistence;
using ColdHarbour.Infrastructure.Playback;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ColdHarbour.Infrastructure.Tests.Playback;

public sealed class DeviceRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private ColdHarbourDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ColdHarbourDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new ColdHarbourDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task AddAndFind_Device_RoundTrips()
    {
        var device = Device.Register(
            Guid.NewGuid(), Guid.NewGuid(), "Chrome", "UA/1.0",
            ["mp3", "flac", "opus"], "opus-128", bitrateCap: null);

        await using var writeCtx = CreateContext();
        var repo = new DeviceRepository(writeCtx);
        await repo.AddAsync(device);
        await repo.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var readRepo = new DeviceRepository(readCtx);
        var found = await readRepo.FindByIdAsync(device.Id);

        found.Should().NotBeNull();
        found!.Name.Should().Be("Chrome");
        found.SupportedCodecs.Should().BeEquivalentTo(["mp3", "flac", "opus"]);
        found.PreferredProfile.Should().Be("opus-128");
        found.BitrateCap.Should().BeNull();
    }

    [Fact]
    public async Task FindByIdAsync_UnknownId_ReturnsNull()
    {
        await using var ctx = CreateContext();
        var repo = new DeviceRepository(ctx);
        var result = await repo.FindByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }
}
