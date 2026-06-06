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

    [Fact]
    public async Task ExistsForUserAsync_True_WhenDeviceBelongsToUser()
    {
        var userId = Guid.NewGuid();
        var device = Device.Register(Guid.NewGuid(), userId, "Chrome", "UA/1.0", ["mp3"], "opus-128");

        await using (var writeCtx = CreateContext())
        {
            var repo = new DeviceRepository(writeCtx);
            await repo.AddAsync(device);
            await repo.SaveChangesAsync();
        }

        await using var readCtx = CreateContext();
        var exists = await new DeviceRepository(readCtx).ExistsForUserAsync(userId, device.Id);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsForUserAsync_False_WhenDeviceBelongsToAnotherUser()
    {
        var ownerId = Guid.NewGuid();
        var device = Device.Register(Guid.NewGuid(), ownerId, "Chrome", "UA/1.0", ["mp3"], "opus-128");

        await using (var writeCtx = CreateContext())
        {
            var repo = new DeviceRepository(writeCtx);
            await repo.AddAsync(device);
            await repo.SaveChangesAsync();
        }

        await using var readCtx = CreateContext();
        // Real device id, but a different user must not match — this is the claim-spoofing guard.
        var exists = await new DeviceRepository(readCtx).ExistsForUserAsync(Guid.NewGuid(), device.Id);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsForUserAsync_False_ForUnknownDevice()
    {
        await using var ctx = CreateContext();
        var exists = await new DeviceRepository(ctx).ExistsForUserAsync(Guid.NewGuid(), Guid.NewGuid());
        exists.Should().BeFalse();
    }
}
