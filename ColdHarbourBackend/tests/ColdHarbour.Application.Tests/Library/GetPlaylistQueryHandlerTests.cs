using ColdHarbour.Application.Library.Handlers;
using ColdHarbour.Application.Library.Queries;
using FluentAssertions;

namespace ColdHarbour.Application.Tests.Library;

public sealed class GetPlaylistQueryHandlerTests
{
    private readonly GetPlaylistQueryHandler _handler = new();

    [Fact]
    public async Task Handle_ReturnsPlaylistWithMatchingId()
    {
        var query = new GetPlaylistQuery(3);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.Id.Should().Be(3);
    }

    [Fact]
    public async Task Handle_ReturnsExpectedTracks()
    {
        var query = new GetPlaylistQuery(1);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.Musics.Should().HaveCount(2);
        result.Musics.Select(m => m.Name).Should().Contain("Baby You're Bad").And.Contain("Liz");
    }

    [Fact]
    public async Task Handle_ReturnsMusicWithCorrectAudioRef()
    {
        var query = new GetPlaylistQuery(1);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.Musics.Should().Contain(m => m.AudioRef == "/assets/music/babyyourebad.mp3");
        result.Musics.Should().Contain(m => m.AudioRef == "/assets/music/liz.mp3");
    }
}
