using ColdHarbour.Application.Library.Dtos;
using MediatR;

namespace ColdHarbour.Application.Library.Commands;

public sealed record UploadTrackCommand(Stream FileStream, string FileName) : IRequest<TrackUploadResultDto>;
