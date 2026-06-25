using Microsoft.Extensions.DependencyInjection;
using NeuralCpu.Core.Components;
using NeuralCpu.Core.Events;
using NeuralCpu.Core.Services;

namespace NeuralCpu.Core;

/// <summary>
/// Extension methods for registering Neural CPU core services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers core Neural CPU services including the event bus, clock, register file, and ALU.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddNeuralCpu(this IServiceCollection services)
    {
        // Register event bus as singleton
        services.AddSingleton<IEventBus, EventBus>();

        // Register clock as singleton
        services.AddSingleton<Clock>();

        // Register register file as singleton
        services.AddSingleton<RegisterFile>();

        // Register ALU as singleton
        services.AddSingleton<Alu>();

        // Register hosted services for lifecycle management
        services.AddHostedService<ClockHostedService>();
        services.AddHostedService<RegisterFileHostedService>();
        services.AddHostedService<AluHostedService>();

        return services;
    }
}

