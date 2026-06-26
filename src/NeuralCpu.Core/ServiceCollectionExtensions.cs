using Microsoft.Extensions.DependencyInjection;
using NeuralCpu.Core.Components;
using NeuralCpu.Core.Configuration;
using NeuralCpu.Core.Events;
using NeuralCpu.Core.Services;

namespace NeuralCpu.Core;

/// <summary>
/// Extension methods for registering Neural CPU core services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers core Neural CPU services including the event bus, clock, register file, ALU, stack, and memory.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="options">Configuration options (optional, defaults will be used if null)</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddNeuralCpu(this IServiceCollection services, NeuralCpuOptions? options = null)
    {
        options ??= new NeuralCpuOptions();

        // Register event bus as singleton
        services.AddSingleton<IEventBus, EventBus>();

        // Register clock as singleton with configuration
        services.AddSingleton(sp =>
        {
            var eventBus = sp.GetRequiredService<IEventBus>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Clock>>();
            return new Clock(eventBus, logger)
            {
                FrequencyHz = options.Clock.InitialFrequencyHz
            };
        });

        // Register register file as singleton
        services.AddSingleton<RegisterFile>();

        // Register ALU as singleton
        services.AddSingleton<Alu>();

        // Register stack as singleton with configuration
        services.AddSingleton(sp =>
        {
            var eventBus = sp.GetRequiredService<IEventBus>();
            var registerFile = sp.GetRequiredService<RegisterFile>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Components.Stack>>();
            return new Components.Stack(eventBus, registerFile, logger, options.Stack.MaxSize);
        });

        // Register memory as singleton with configuration
        services.AddSingleton(sp =>
        {
            var eventBus = sp.GetRequiredService<IEventBus>();
            var registerFile = sp.GetRequiredService<RegisterFile>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Components.Memory>>();
            return new Components.Memory(eventBus, registerFile, logger, options.Memory.CapacityWords);
        });

        // Register hosted services for lifecycle management
        services.AddHostedService<ClockHostedService>();
        services.AddHostedService<RegisterFileHostedService>();
        services.AddHostedService<AluHostedService>();
        services.AddHostedService<StackHostedService>();
        services.AddHostedService<MemoryHostedService>();

        return services;
    }
}

