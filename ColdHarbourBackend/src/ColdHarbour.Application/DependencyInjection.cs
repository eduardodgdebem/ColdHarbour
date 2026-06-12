using ColdHarbour.Application.Pipeline;
using ColdHarbour.Application.Playback;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Application.Playback.Services;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace ColdHarbour.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        services.AddScoped<IPlaySessionTimeline, PlaySessionTimeline>();

        // Default limits; the composition root (Program.cs) overrides this from configuration.
        services.AddSingleton(new PlaybackLimits());

        return services;
    }
}
