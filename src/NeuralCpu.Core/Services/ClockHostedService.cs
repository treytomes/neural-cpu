using Microsoft.Extensions.Hosting;
using NeuralCpu.Core.Components;

namespace NeuralCpu.Core.Services;

/// <summary>
/// Hosted service that manages the Clock component lifecycle.
/// The clock does not auto-start; it must be started explicitly via Start() method.
/// </summary>
public sealed class ClockHostedService : IHostedService
{
    private readonly Clock _clock;

    public ClockHostedService(Clock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// Gets the managed Clock instance.
    /// </summary>
    public Clock Clock => _clock;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Clock is ready but not started automatically
        // The TUI or other components will start it explicitly
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Ensure clock is stopped on application shutdown
        _clock.Stop();
        _clock.Dispose();
        return Task.CompletedTask;
    }
}
