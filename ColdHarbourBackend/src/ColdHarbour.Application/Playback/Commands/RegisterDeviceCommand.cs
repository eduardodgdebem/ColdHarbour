using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record RegisterDeviceCommand(
    Guid DeviceId,
    Guid UserId,
    string Name,
    string UserAgent,
    IReadOnlyList<string> SupportedCodecs,
    string PreferredProfile,
    int? BitrateCap) : IRequest;

public sealed class RegisterDeviceCommandHandler(IDeviceRepository repo) : IRequestHandler<RegisterDeviceCommand>
{
    public async Task Handle(RegisterDeviceCommand request, CancellationToken cancellationToken)
    {
        var existing = await repo.FindByIdAsync(request.DeviceId, cancellationToken);

        if (existing is null)
        {
            var device = Device.Register(
                request.DeviceId,
                request.UserId,
                request.Name,
                request.UserAgent,
                request.SupportedCodecs,
                request.PreferredProfile,
                request.BitrateCap);
            await repo.AddAsync(device, cancellationToken);
        }
        else
        {
            existing.Heartbeat(request.UserAgent, request.SupportedCodecs, request.PreferredProfile, request.BitrateCap);
        }

        await repo.SaveChangesAsync(cancellationToken);
    }
}
