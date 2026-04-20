using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Infrastructure.Library;
using ColdHarbour.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ColdHarbour.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<ColdHarbourDbContext>(opts =>
            opts.UseNpgsql(config.GetConnectionString("DefaultConnection")));

        services.AddScoped<ILibraryReadRepository, LibraryReadRepository>();

        return services;
    }
}
