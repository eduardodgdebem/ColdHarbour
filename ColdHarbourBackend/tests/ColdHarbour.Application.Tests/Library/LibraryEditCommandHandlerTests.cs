using ColdHarbour.Application.Library.Commands;
using ColdHarbour.Application.Library.Handlers;
using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Domain.Library;
using FluentAssertions;

namespace ColdHarbour.Application.Tests.Library;

public sealed class LibraryEditCommandHandlerTests
{
    private const string ValidSha1 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    // ── UpdateTrack ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateTrack_UpdatesMetadataAndSaves()
    {
        var track = Track.Create("Old", Guid.NewGuid(), TimeSpan.FromSeconds(10), "local", "flac", 320, ValidSha1);
        var repo = new FakeRepo { Track = track };
        var handler = new UpdateTrackCommandHandler(repo);

        await handler.Handle(new UpdateTrackCommand(track.Id, "New Title", 4), CancellationToken.None);

        track.Title.Should().Be("New Title");
        track.TrackNumber.Should().Be(4);
        repo.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task UpdateTrack_ThrowsKeyNotFound_WhenMissing()
    {
        var handler = new UpdateTrackCommandHandler(new FakeRepo { Track = null });

        var act = () => handler.Handle(new UpdateTrackCommand(Guid.NewGuid(), "X", null), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── UpdateAlbum ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAlbum_UpdatesMetadataAndSaves()
    {
        var album = Album.Create("Old", Guid.NewGuid(), 1990);
        var repo = new FakeRepo { Album = album };
        var handler = new UpdateAlbumCommandHandler(repo);

        await handler.Handle(new UpdateAlbumCommand(album.Id, "The Wall", 1979), CancellationToken.None);

        album.Title.Should().Be("The Wall");
        album.Year.Should().Be(1979);
        repo.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task UpdateAlbum_ThrowsKeyNotFound_WhenMissing()
    {
        var handler = new UpdateAlbumCommandHandler(new FakeRepo { Album = null });

        var act = () => handler.Handle(new UpdateAlbumCommand(Guid.NewGuid(), "X", null), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── RenameArtist ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RenameArtist_RenamesAndSaves()
    {
        var artist = Artist.Create("Pink Floid");
        var repo = new FakeRepo { Artist = artist };
        var handler = new RenameArtistCommandHandler(repo);

        await handler.Handle(new RenameArtistCommand(artist.Id, "Pink Floyd"), CancellationToken.None);

        artist.Name.Should().Be("Pink Floyd");
        repo.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task RenameArtist_ThrowsKeyNotFound_WhenMissing()
    {
        var handler = new RenameArtistCommandHandler(new FakeRepo { Artist = null });

        var act = () => handler.Handle(new RenameArtistCommand(Guid.NewGuid(), "X"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── UpdateAlbumCover ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAlbumCover_SavesSourceAndPointsAlbumAtSha1()
    {
        var album = Album.Create("The Wall", Guid.NewGuid());
        var repo = new FakeRepo { Album = album };
        var artwork = new FakeArtwork { Sha1ToReturn = ValidSha1 };
        var handler = new UpdateAlbumCoverCommandHandler(repo, artwork);
        using var stream = new MemoryStream([1, 2, 3]);

        await handler.Handle(new UpdateAlbumCoverCommand(album.Id, stream, "image/jpeg"), CancellationToken.None);

        artwork.SaveCalledWithContentType.Should().Be("image/jpeg");
        album.CoverArtSha1.Should().Be(ValidSha1);
        repo.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task UpdateAlbumCover_ThrowsKeyNotFound_WhenAlbumMissing()
    {
        var handler = new UpdateAlbumCoverCommandHandler(new FakeRepo { Album = null }, new FakeArtwork());
        using var stream = new MemoryStream([1]);

        var act = () => handler.Handle(new UpdateAlbumCoverCommand(Guid.NewGuid(), stream, "image/png"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UpdateAlbumCover_PropagatesInvalidImage()
    {
        var album = Album.Create("The Wall", Guid.NewGuid());
        var artwork = new FakeArtwork { Throw = new InvalidOperationException("Unsupported image type.") };
        var handler = new UpdateAlbumCoverCommandHandler(new FakeRepo { Album = album }, artwork);
        using var stream = new MemoryStream([0]);

        var act = () => handler.Handle(new UpdateAlbumCoverCommand(album.Id, stream, "text/plain"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── fakes ────────────────────────────────────────────────────────────────────

    private sealed class FakeRepo : ITrackRepository
    {
        public Track? Track { get; init; }
        public Album? Album { get; init; }
        public Artist? Artist { get; init; }
        public int SaveCount { get; private set; }

        public Task<Track?> FindByIdAsync(Guid trackId, CancellationToken ct = default) => Task.FromResult(Track);
        public Task<Album?> FindAlbumByIdAsync(Guid albumId, CancellationToken ct = default) => Task.FromResult(Album);
        public Task<Artist?> FindArtistByIdAsync(Guid artistId, CancellationToken ct = default) => Task.FromResult(Artist);
        public Task SaveChangesAsync(CancellationToken ct = default) { SaveCount++; return Task.CompletedTask; }

        public Task<Track?> FindByAudioSha1Async(string audioSha1, CancellationToken ct = default) => Task.FromResult<Track?>(null);
        public Task<Artist?> FindArtistByNameAsync(string name, CancellationToken ct = default) => Task.FromResult<Artist?>(null);
        public Task<Album?> FindAlbumByArtistAndTitleAsync(Guid artistId, string title, CancellationToken ct = default) => Task.FromResult<Album?>(null);
        public Task<int> CountTracksByAlbumIdAsync(Guid albumId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> CountAlbumsByArtistIdAsync(Guid artistId, CancellationToken ct = default) => Task.FromResult(0);
        public Task AddArtistAsync(Artist artist, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddAlbumAsync(Album album, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddTrackAsync(Track track, CancellationToken ct = default) => Task.CompletedTask;
        public void RemoveTrack(Track track) { }
        public void RemoveAlbum(Album album) { }
        public void RemoveArtist(Artist artist) { }
        public Task<List<Track>> GetLocalTrackSampleAsync(int maxCount, CancellationToken ct = default) => Task.FromResult(new List<Track>());
    }

    private sealed class FakeArtwork : IArtworkService
    {
        public string Sha1ToReturn { get; init; } = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        public string? SaveCalledWithContentType { get; private set; }
        public Exception? Throw { get; init; }

        public Task<string> SaveSourceAsync(Stream content, string contentType, CancellationToken ct = default)
        {
            if (Throw is not null) throw Throw;
            SaveCalledWithContentType = contentType;
            return Task.FromResult(Sha1ToReturn);
        }

        public Task<string?> GetThumbnailPathAsync(Guid albumId, int size, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> GetCoverArtSha1Async(Guid albumId, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }
}
