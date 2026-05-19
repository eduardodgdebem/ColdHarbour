using ColdHarbour.Application.Playback.Dtos;
using ColdHarbour.Application.Playback.Ports;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record ListDevicesQuery(Guid UserId) : IRequest<IReadOnlyList<DeviceDto>>;

public sealed class ListDevicesQueryHandler(IDeviceRepository repo) : IRequestHandler<ListDevicesQuery, IReadOnlyList<DeviceDto>>
{
    public async Task<IReadOnlyList<DeviceDto>> Handle(ListDevicesQuery request, CancellationToken cancellationToken)
    {
        var devices = await repo.ListByUserIdAsync(request.UserId, cancellationToken);
        return devices.Select(d => new DeviceDto(d.Id, d.Name, d.LastSeenAt)).ToList();
    }
}
