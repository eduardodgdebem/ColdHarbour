using ColdHarbour.Domain.Playback;

namespace ColdHarbour.Application.Playback.Ports;

public interface IPlaybackSessionStore
{
    PlaybackSession GetOrCreate(Guid userId);
    IReadOnlyList<PlaybackSession> GetAllForUser(Guid userId);
}
