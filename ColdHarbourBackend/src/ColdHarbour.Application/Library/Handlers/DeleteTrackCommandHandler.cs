using ColdHarbour.Application.Library.Commands;
using ColdHarbour.Application.Library.Ports;
using MediatR;

namespace ColdHarbour.Application.Library.Handlers;

public sealed class DeleteTrackCommandHandler(ITrackRepository repo, ITrackIngestService ingestService)
    : IRequestHandler<DeleteTrackCommand>
{
    public async Task Handle(DeleteTrackCommand request, CancellationToken cancellationToken)
    {
        var track = await repo.FindByIdAsync(request.TrackId, cancellationToken);
        if (track is null)
            return;

        var albumId = track.AlbumId;
        var localPath = track.LocalPath;
        var audioSha1 = track.AudioSha1;

        repo.RemoveTrack(track);
        await repo.SaveChangesAsync(cancellationToken);

        await ingestService.RemoveTrackFilesAsync(localPath, audioSha1, cancellationToken);

        var tracksLeft = await repo.CountTracksByAlbumIdAsync(albumId, cancellationToken);
        if (tracksLeft > 0)
            return;

        var album = await repo.FindAlbumByIdAsync(albumId, cancellationToken);
        if (album is null)
            return;

        var artistId = album.ArtistId;
        repo.RemoveAlbum(album);
        await repo.SaveChangesAsync(cancellationToken);

        var albumsLeft = await repo.CountAlbumsByArtistIdAsync(artistId, cancellationToken);
        if (albumsLeft > 0)
            return;

        var artist = await repo.FindArtistByIdAsync(artistId, cancellationToken);
        if (artist is not null)
        {
            repo.RemoveArtist(artist);
            await repo.SaveChangesAsync(cancellationToken);
        }
    }
}
