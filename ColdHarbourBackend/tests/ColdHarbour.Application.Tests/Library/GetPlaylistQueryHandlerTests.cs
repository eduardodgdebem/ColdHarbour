using ColdHarbour.Application.Library.Handlers;
using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Application.Library.Queries;
using FluentAssertions;

namespace ColdHarbour.Application.Tests.Library;

public sealed class GetPlaylistQueryHandlerTests
{
    private static readonly Guid AlbumId1 = Guid.Parse("22222222-0000-0000-0000-000000000001");
    private static readonly Guid AlbumId2 = Guid.Parse("22222222-0000-0000-0000-000000000002");

    private static readonly IReadOnlyList<TrackReadModel> TwoTracks =
    [
        new TrackReadModel(
            Id: Guid.Parse("33333333-0000-0000-0000-000000000001"),
            AlbumId: AlbumId1,
            Title: "Baby You're Bad",
            ArtistName: "HONNE",
            AlbumTitle: "HONNE",
            Duration: TimeSpan.FromSeconds(210),
            LocalPath: "/assets/music/babyyourebad.mp3",
            Format: "mp3",
            Bitrate: 128),
        new TrackReadModel(
            Id: Guid.Parse("33333333-0000-0000-0000-000000000002"),
            AlbumId: AlbumId2,
            Title: "Liz",
            ArtistName: "Remi Wolf",
            AlbumTitle: "Remi Wolf",
            Duration: TimeSpan.FromSeconds(210),
            LocalPath: "/assets/music/liz.mp3",
            Format: "mp3",
            Bitrate: 128)
    ];

    private static GetPlaylistQueryHandler BuildHandler(IReadOnlyList<TrackReadModel>? tracks = null)
    {
        var repo = new StubLibraryReadRepository(tracks ?? TwoTracks);
        return new GetPlaylistQueryHandler(repo);
    }

    [Fact]
    public async Task Handle_ReturnsPlaylistWithMatchingId()
    {
        var handler = BuildHandler();
        var query = new GetPlaylistQuery(3);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Id.Should().Be(3);
    }

    [Fact]
    public async Task Handle_ReturnsExpectedTracks()
    {
        var handler = BuildHandler();
        var query = new GetPlaylistQuery(1);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Musics.Should().HaveCount(2);
        result.Musics.Select(m => m.Name).Should().Contain("Baby You're Bad").And.Contain("Liz");
    }

    [Fact]
    public async Task Handle_ReturnsMusicWithStreamAudioRef()
    {
        var handler = BuildHandler();
        var query = new GetPlaylistQuery(1);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Musics.Should().Contain(m =>
            m.AudioRef == $"/api/stream/{Guid.Parse("33333333-0000-0000-0000-000000000001")}");
    }

    [Fact]
    public async Task Handle_ReturnsMusicWithArtworkImageRef()
    {
        var handler = BuildHandler();
        var query = new GetPlaylistQuery(1);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Musics.Should().Contain(m =>
            m.ImageRef == $"/api/artwork/{AlbumId1}?size=256");
    }

    [Fact]
    public async Task Handle_ReturnsMusicWithCorrectAuthor()
    {
        var handler = BuildHandler();
        var query = new GetPlaylistQuery(1);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Musics.Should().Contain(m => m.Author == "HONNE");
        result.Musics.Should().Contain(m => m.Author == "Remi Wolf");
    }

    [Fact]
    public async Task Handle_AssignsSequentialIds_ToMusics()
    {
        var handler = BuildHandler();
        var query = new GetPlaylistQuery(1);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Musics[0].Id.Should().Be(1);
        result.Musics[1].Id.Should().Be(2);
    }

    [Fact]
    public async Task Handle_ReturnsPlaylistNameLibrary()
    {
        var handler = BuildHandler();
        var query = new GetPlaylistQuery(1);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Name.Should().Be("Library");
    }

    [Fact]
    public async Task Handle_ExposesTrackIdAndAlbumId()
    {
        var handler = BuildHandler();
        var query = new GetPlaylistQuery(1);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Musics[0].TrackId.Should().Be(Guid.Parse("33333333-0000-0000-0000-000000000001"));
        result.Musics[0].AlbumId.Should().Be(AlbumId1);
    }

    [Fact]
    public async Task Handle_ExposesDurationSeconds()
    {
        var handler = BuildHandler();
        var query = new GetPlaylistQuery(1);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Musics[0].DurationSeconds.Should().BeApproximately(210, 0.001);
    }

    // --- hand-crafted stub (no mocking library) ---

    private sealed class StubLibraryReadRepository(IReadOnlyList<TrackReadModel> tracks)
        : ILibraryReadRepository
    {
        public Task<IReadOnlyList<TrackReadModel>> GetAllTracksAsync(CancellationToken ct = default)
            => Task.FromResult(tracks);

        public Task<IReadOnlyList<AlbumReadModel>> GetAlbumsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AlbumReadModel>>([]);

        public Task<AlbumDetailReadModel?> GetAlbumAsync(Guid albumId, CancellationToken ct = default)
            => Task.FromResult<AlbumDetailReadModel?>(null);

        public Task<IReadOnlyList<ArtistReadModel>> GetArtistsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ArtistReadModel>>([]);

        public Task<ArtistDetailReadModel?> GetArtistAsync(Guid artistId, CancellationToken ct = default)
            => Task.FromResult<ArtistDetailReadModel?>(null);
    }
}
