using ColdHarbour.Application.Playback.Commands;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using FluentAssertions;
using NSubstitute;

namespace ColdHarbour.Application.Tests.Playback;

public sealed class RegisterDeviceCommandHandlerTests
{
    private readonly IDeviceRepository _repo = Substitute.For<IDeviceRepository>();

    private RegisterDeviceCommandHandler CreateHandler() => new(_repo);

    [Fact]
    public async Task Handle_NewDevice_AddsAndSaves()
    {
        var deviceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _repo.FindByIdAsync(deviceId, Arg.Any<CancellationToken>()).Returns((Device?)null);

        await CreateHandler().Handle(
            new RegisterDeviceCommand(deviceId, userId, "Chrome", "UA", ["mp3", "flac"], "opus-128", null),
            CancellationToken.None);

        await _repo.Received(1).AddAsync(Arg.Is<Device>(d => d.Id == deviceId && d.UserId == userId), Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingDevice_HeartbeatsAndSaves()
    {
        var deviceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var existing = Device.Register(deviceId, userId, "OldName", "OldUA", ["mp3"], "mp3-192");
        _repo.FindByIdAsync(deviceId, Arg.Any<CancellationToken>()).Returns(existing);

        await CreateHandler().Handle(
            new RegisterDeviceCommand(deviceId, userId, "Chrome", "NewUA", ["mp3", "flac"], "opus-128", null),
            CancellationToken.None);

        await _repo.DidNotReceive().AddAsync(Arg.Any<Device>(), Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        existing.UserAgent.Should().Be("NewUA");
        existing.PreferredProfile.Should().Be("opus-128");
    }
}
