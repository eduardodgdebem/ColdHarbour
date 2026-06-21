using ColdHarbour.Application.Library.Handlers;
using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Application.Library.Queries;
using FluentAssertions;

namespace ColdHarbour.Application.Tests.Library;

public sealed class AlbumArtistQueryHandlerTests
{
    private static readonly Guid ArtistId = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid AlbumId = Guid.Parse("22222222-0000-0000-0000-000000000001");
    private static readonly Guid TrackId = Guid.Parse("33333333-0000-0000-0000-000000000001");
    private const string Sha1 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static AlbumReadModel SampleAlbum => new(
        Id: AlbumId,
        Title: "The Wall",
        ArtistId: ArtistId,
        ArtistName: "Pink Floyd",
        Year: 1979,
        CoverArtSha1: Sha1,
        TrackCount: 1);

    private static AlbumDetailReadModel SampleAlbumDetail => new(
        Id: AlbumId,
        Title: "The Wall",
        ArtistId: ArtistId,
        ArtistName: "Pink Floyd",
        Year: 1979,
        CoverArtSha1: Sha1,
        Tracks:
        [
            new TrackReadModel(TrackId, AlbumId, "Comfortably Numb", "Pink Floyd", "The Wall",
                TimeSpan.FromSeconds(382), "/content/x.flac", "flac", 900)
        ]);

    private static ArtistReadModel SampleArtist => new(ArtistId, "Pink Floyd", AlbumCount: 1);

    private static ArtistDetailReadModel SampleArtistDetail => new(ArtistId, "Pink Floyd", Albums: [SampleAlbum]);

    // ── GetAlbumsQuery ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAlbums_MapsSummaries()
    {
        var handler = new GetAlbumsQueryHandler(new StubRepo { Albums = [SampleAlbum] });

        var result = await handler.Handle(new GetAlbumsQuery(), CancellationToken.None);

        result.Should().ContainSingle();
        var a = result[0];
        a.Id.Should().Be(AlbumId);
        a.Title.Should().Be("The Wall");
        a.Artist.Should().Be("Pink Floyd");
        a.ArtistId.Should().Be(ArtistId);
        a.Year.Should().Be(1979);
        a.TrackCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAlbums_ImageRef_IncludesSha1VersionParam()
    {
        var handler = new GetAlbumsQueryHandler(new StubRepo { Albums = [SampleAlbum] });

        var result = await handler.Handle(new GetAlbumsQuery(), CancellationToken.None);

        result[0].ImageRef.Should().Be($"/api/artwork/{AlbumId}?size=256&v={Sha1}");
    }

    [Fact]
    public async Task GetAlbums_ImageRef_OmitsVersion_WhenNoCover()
    {
        var noCover = SampleAlbum with { CoverArtSha1 = null };
        var handler = new GetAlbumsQueryHandler(new StubRepo { Albums = [noCover] });

        var result = await handler.Handle(new GetAlbumsQuery(), CancellationToken.None);

        result[0].ImageRef.Should().Be($"/api/artwork/{AlbumId}?size=256");
    }

    [Fact]
    public async Task GetAlbums_Empty_ReturnsEmpty()
    {
        var handler = new GetAlbumsQueryHandler(new StubRepo());

        var result = await handler.Handle(new GetAlbumsQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }

    // ── GetAlbumQuery ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAlbum_MapsDetailWithTracks()
    {
        var handler = new GetAlbumQueryHandler(new StubRepo { AlbumDetail = SampleAlbumDetail });

        var result = await handler.Handle(new GetAlbumQuery(AlbumId), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be(AlbumId);
        result.Artist.Should().Be("Pink Floyd");
        result.Tracks.Should().ContainSingle();
        result.Tracks[0].TrackId.Should().Be(TrackId);
        result.Tracks[0].AudioRef.Should().Be($"/api/stream/{TrackId}");
        result.Tracks[0].ImageRef.Should().Be($"/api/artwork/{AlbumId}?size=256&v={Sha1}");
    }

    [Fact]
    public async Task GetAlbum_ReturnsNull_WhenMissing()
    {
        var handler = new GetAlbumQueryHandler(new StubRepo { AlbumDetail = null });

        var result = await handler.Handle(new GetAlbumQuery(AlbumId), CancellationToken.None);

        result.Should().BeNull();
    }

    // ── GetArtistsQuery ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetArtists_MapsSummaries()
    {
        var handler = new GetArtistsQueryHandler(new StubRepo { Artists = [SampleArtist] });

        var result = await handler.Handle(new GetArtistsQuery(), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Id.Should().Be(ArtistId);
        result[0].Name.Should().Be("Pink Floyd");
        result[0].AlbumCount.Should().Be(1);
    }

    // ── GetArtistQuery ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetArtist_MapsDetailWithAlbums()
    {
        var handler = new GetArtistQueryHandler(new StubRepo { ArtistDetail = SampleArtistDetail });

        var result = await handler.Handle(new GetArtistQuery(ArtistId), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be(ArtistId);
        result.Name.Should().Be("Pink Floyd");
        result.Albums.Should().ContainSingle();
        result.Albums[0].Id.Should().Be(AlbumId);
        result.Albums[0].ImageRef.Should().Be($"/api/artwork/{AlbumId}?size=256&v={Sha1}");
    }

    [Fact]
    public async Task GetArtist_ReturnsNull_WhenMissing()
    {
        var handler = new GetArtistQueryHandler(new StubRepo { ArtistDetail = null });

        var result = await handler.Handle(new GetArtistQuery(ArtistId), CancellationToken.None);

        result.Should().BeNull();
    }

    // ── stub ─────────────────────────────────────────────────────────────────────

    private sealed class StubRepo : ILibraryReadRepository
    {
        public IReadOnlyList<AlbumReadModel> Albums { get; init; } = [];
        public AlbumDetailReadModel? AlbumDetail { get; init; }
        public IReadOnlyList<ArtistReadModel> Artists { get; init; } = [];
        public ArtistDetailReadModel? ArtistDetail { get; init; }

        public Task<IReadOnlyList<TrackReadModel>> GetAllTracksAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TrackReadModel>>([]);

        public Task<IReadOnlyList<AlbumReadModel>> GetAlbumsAsync(CancellationToken ct = default)
            => Task.FromResult(Albums);

        public Task<AlbumDetailReadModel?> GetAlbumAsync(Guid albumId, CancellationToken ct = default)
            => Task.FromResult(AlbumDetail);

        public Task<IReadOnlyList<ArtistReadModel>> GetArtistsAsync(CancellationToken ct = default)
            => Task.FromResult(Artists);

        public Task<ArtistDetailReadModel?> GetArtistAsync(Guid artistId, CancellationToken ct = default)
            => Task.FromResult(ArtistDetail);
    }
}
