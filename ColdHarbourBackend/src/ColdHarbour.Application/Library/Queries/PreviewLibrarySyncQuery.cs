using ColdHarbour.Application.Library.Dtos;
using MediatR;

namespace ColdHarbour.Application.Library.Queries;

public sealed record PreviewLibrarySyncQuery : IRequest<LibrarySyncDiffDto>;
