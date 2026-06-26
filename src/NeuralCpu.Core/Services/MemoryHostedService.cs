using Microsoft.Extensions.Hosting;

namespace NeuralCpu.Core.Services;

/// <summary>
/// Hosted service that manages the Memory component lifecycle.
/// </summary>
internal sealed class MemoryHostedService(Components.Memory memory) : IHostedService
{
    private readonly Components.Memory _memory = memory ?? throw new ArgumentNullException(nameof(memory));

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Memory component is passive - no background tasks to start
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _memory.Dispose();
        return Task.CompletedTask;
    }
}
