using ColdHarbour.Application.Library.Commands;
using ColdHarbour.Application.Library.Dtos;
using ColdHarbour.Application.Library.Ports;
using MediatR;

namespace ColdHarbour.Application.Library.Handlers;

public sealed class UploadTrackCommandHandler(ITrackIngestService ingestService)
    : IRequestHandler<UploadTrackCommand, TrackUploadResultDto>
{
    public Task<TrackUploadResultDto> Handle(UploadTrackCommand request, CancellationToken cancellationToken)
        => ingestService.IngestAsync(request.FileStream, request.FileName, cancellationToken);
}
