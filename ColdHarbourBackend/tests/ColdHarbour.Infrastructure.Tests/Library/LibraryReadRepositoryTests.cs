using ColdHarbour.Infrastructure.Library;
using ColdHarbour.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ColdHarbour.Infrastructure.Tests.Library;

public class LibraryReadRepositoryTests : IAsyncLifetime
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
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task GetAllTracksAsync_ReturnsSeedData_AfterMigration()
    {
        await using var context = CreateContext();
        var repo = new LibraryReadRepository(context);

        var tracks = await repo.GetAllTracksAsync();

        tracks.Should().HaveCount(2);
        tracks.Select(t => t.Title).Should().Contain("Baby You're Bad").And.Contain("Liz");
        tracks.Select(t => t.ArtistName).Should().Contain("HONNE").And.Contain("Remi Wolf");
        tracks.Should().Contain(t => t.LocalPath == "/assets/music/babyyourebad.mp3");
        tracks.Should().Contain(t => t.LocalPath == "/assets/music/liz.mp3");
    }
}
