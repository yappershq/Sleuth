using Microsoft.Extensions.DependencyInjection;
using Sleuth.Configuration;
using Sleuth.Modules;

namespace Sleuth;

internal static class ModuleDependencyInjection
{
    internal static IServiceCollection AddModules(this IServiceCollection services)
    {
        services.AddSingleton<ISleuthConfig, SleuthConfig>();
        services.AddSingleton<SleuthModule>();
        services.AddSingleton<IModule>(sp => sp.GetRequiredService<SleuthModule>());
        return services;
    }
}
