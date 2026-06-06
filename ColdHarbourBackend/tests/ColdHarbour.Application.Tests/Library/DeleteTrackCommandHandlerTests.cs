using ColdHarbour.Application.Library.Commands;
using ColdHarbour.Application.Library.Dtos;
using ColdHarbour.Application.Library.Handlers;
using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Domain.Library;
using FluentAssertions;

namespace ColdHarbour.Application.Tests.Library;

public sealed class DeleteTrackCommandHandlerTests
{
    private const string ValidSha1 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly Guid ArtistId = Guid.NewGuid();
    private static readonly Guid AlbumId = Guid.NewGuid();
    private static readonly Guid TrackId = Guid.NewGuid();

    private static Track MakeTrack()
        => Track.Create("Title", AlbumId, TimeSpan.FromSeconds(180), "local", "mp3", 128, ValidSha1,
            localPath: "/content/library/Artist/Album/title.mp3");

    private static Album MakeAlbum()
        => Album.Create("Album", ArtistId);

    private static Artist MakeArtist()
        => Artist.Create("Artist");

    [Fact]
    public async Task Handle_NonExistentTrack_DoesNotThrow()
    {
        var repo = new SpyTrackRepository();
        var ingest = new StubIngestService();

        var handler = new DeleteTrackCommandHandler(repo, ingest);

        var act = () => handler.Handle(new DeleteTrackCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_RemovesTrack_AndCallsFileCleanup()
    {
        var track = MakeTrack();
        var album = MakeAlbum();
        var repo = new SpyTrackRepository(track: track, album: album, trackCount: 1);
        var ingest = new StubIngestService();

        var handler = new DeleteTrackCommandHandler(repo, ingest);
        await handler.Handle(new DeleteTrackCommand(track.Id), CancellationToken.None);

        repo.RemovedTracks.Should().Contain(t => t.Id == track.Id);
        ingest.RemoveCalledWithSha1.Should().Be(ValidSha1);
    }

    [Fact]
    public async Task Handle_WhenAlbumBecomesEmpty_RemovesAlbum()
    {
        var track = MakeTrack();
        var album = MakeAlbum();
        var artist = MakeArtist();

        // After removing the track, 0 tracks remain in album; 0 albums remain for artist.
        var repo = new SpyTrackRepository(track: track, album: album, artist: artist,
            trackCount: 0, albumCount: 0);
        var ingest = new StubIngestService();

        var handler = new DeleteTrackCommandHandler(repo, ingest);
        await handler.Handle(new DeleteTrackCommand(track.Id), CancellationToken.None);

        repo.RemovedAlbums.Should().NotBeEmpty();
        repo.RemovedArtists.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenAlbumStillHasTracks_DoesNotRemoveAlbum()
    {
        var track = MakeTrack();
        var album = MakeAlbum();

        var repo = new SpyTrackRepository(track: track, album: album, trackCount: 1, albumCount: 1);
        var ingest = new StubIngestService();

        var handler = new DeleteTrackCommandHandler(repo, ingest);
        await handler.Handle(new DeleteTrackCommand(track.Id), CancellationToken.None);

        repo.RemovedAlbums.Should().BeEmpty();
        repo.RemovedArtists.Should().BeEmpty();
    }

    // ── stubs ────────────────────────────────────────────────────────────────────

    private sealed class StubIngestService : ITrackIngestService
    {
        public string? RemoveCalledWithSha1 { get; private set; }

        public Task<TrackUploadResultDto> IngestAsync(Stream fileStream, string fileName, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<TrackUploadResultDto> IngestExistingFileAsync(string relativePath, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task RemoveTrackFilesAsync(string? localPath, string audioSha1, CancellationToken ct = default)
        {
            RemoveCalledWithSha1 = audioSha1;
            return Task.CompletedTask;
        }
    }

    private sealed class SpyTrackRepository(
        Track? track = null,
        Album? album = null,
        Artist? artist = null,
        int trackCount = 0,
        int albumCount = 0) : ITrackRepository
    {
        public List<Track> RemovedTracks { get; } = [];
        public List<Album> RemovedAlbums { get; } = [];
        public List<Artist> RemovedArtists { get; } = [];

        public Task<Track?> FindByIdAsync(Guid trackId, CancellationToken ct = default)
            => Task.FromResult(track?.Id == trackId ? track : null);

        public Task<Track?> FindByAudioSha1Async(string audioSha1, CancellationToken ct = default)
            => Task.FromResult<Track?>(null);

        public Task<Artist?> FindArtistByIdAsync(Guid artistId, CancellationToken ct = default)
            => Task.FromResult(artist);

        public Task<Artist?> FindArtistByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult<Artist?>(null);

        public Task<Album?> FindAlbumByArtistAndTitleAsync(Guid artistId, string title, CancellationToken ct = default)
            => Task.FromResult<Album?>(null);

        public Task<Album?> FindAlbumByIdAsync(Guid albumId, CancellationToken ct = default)
            => Task.FromResult(album);

        public Task<int> CountTracksByAlbumIdAsync(Guid albumId, CancellationToken ct = default)
            => Task.FromResult(trackCount);

        public Task<int> CountAlbumsByArtistIdAsync(Guid artistId, CancellationToken ct = default)
            => Task.FromResult(albumCount);

        public Task AddArtistAsync(Artist a, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddAlbumAsync(Album a, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddTrackAsync(Track t, CancellationToken ct = default) => Task.CompletedTask;

        public void RemoveTrack(Track t) => RemovedTracks.Add(t);
        public void RemoveAlbum(Album a) => RemovedAlbums.Add(a);
        public void RemoveArtist(Artist a) => RemovedArtists.Add(a);

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<Track>> GetLocalTrackSampleAsync(int maxCount, CancellationToken ct = default) => Task.FromResult(new List<Track>());
    }
}
