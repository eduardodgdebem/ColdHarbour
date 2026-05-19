using ColdHarbour.Application.Library.Commands;
using ColdHarbour.Application.Library.Dtos;
using ColdHarbour.Application.Library.Handlers;
using ColdHarbour.Application.Library.Ports;
using FluentAssertions;

namespace ColdHarbour.Application.Tests.Library;

public sealed class UploadTrackCommandHandlerTests
{
    private static readonly Guid StubTrackId = Guid.NewGuid();
    private static readonly Guid StubAlbumId = Guid.NewGuid();

    [Fact]
    public async Task Handle_DelegatesToIngestService_AndReturnsResult()
    {
        var ingest = new StubTrackIngestService(new TrackUploadResultDto(StubTrackId, StubAlbumId, false));
        var handler = new UploadTrackCommandHandler(ingest);
        var stream = new MemoryStream([1, 2, 3]);

        var result = await handler.Handle(new UploadTrackCommand(stream, "track.mp3"), CancellationToken.None);

        result.TrackId.Should().Be(StubTrackId);
        result.AlbumId.Should().Be(StubAlbumId);
        result.AlreadyExisted.Should().BeFalse();
        ingest.IngestCalledWith.Should().Be("track.mp3");
    }

    [Fact]
    public async Task Handle_WhenTrackAlreadyExists_ReturnsAlreadyExistedTrue()
    {
        var ingest = new StubTrackIngestService(new TrackUploadResultDto(StubTrackId, StubAlbumId, true));
        var handler = new UploadTrackCommandHandler(ingest);

        var result = await handler.Handle(new UploadTrackCommand(Stream.Null, "dup.mp3"), CancellationToken.None);

        result.AlreadyExisted.Should().BeTrue();
    }

    private sealed class StubTrackIngestService(TrackUploadResultDto result) : ITrackIngestService
    {
        public string? IngestCalledWith { get; private set; }

        public Task<TrackUploadResultDto> IngestAsync(Stream fileStream, string fileName, CancellationToken ct = default)
        {
            IngestCalledWith = fileName;
            return Task.FromResult(result);
        }

        public Task RemoveTrackFilesAsync(string? localPath, string audioSha1, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
