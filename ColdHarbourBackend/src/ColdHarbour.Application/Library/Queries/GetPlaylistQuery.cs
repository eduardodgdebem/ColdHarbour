using ColdHarbour.Application.Library.Dtos;
using MediatR;

namespace ColdHarbour.Application.Library.Queries;

public record GetPlaylistQuery(int Id) : IRequest<PlaylistDto>;
