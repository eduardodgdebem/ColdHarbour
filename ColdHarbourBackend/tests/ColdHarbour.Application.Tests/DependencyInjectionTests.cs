using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace ColdHarbour.Application.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddApplication_CanResolveIMediator()
    {
        var services = new ServiceCollection();
        services.AddApplication();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetService<IMediator>();

        mediator.Should().NotBeNull();
    }
}
