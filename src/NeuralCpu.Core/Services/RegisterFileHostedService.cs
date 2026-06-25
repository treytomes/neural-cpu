using Microsoft.Extensions.Hosting;
using NeuralCpu.Core.Components;

namespace NeuralCpu.Core.Services;

/// <summary>
/// Hosted service that manages the RegisterFile lifecycle.
/// </summary>
internal sealed class RegisterFileHostedService(RegisterFile registerFile) : IHostedService
{
    private readonly RegisterFile _registerFile = registerFile ?? throw new ArgumentNullException(nameof(registerFile));

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // RegisterFile subscribes to events in its constructor
        // No explicit start action needed
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Dispose the register file
        _registerFile.Dispose();
        return Task.CompletedTask;
    }
}
