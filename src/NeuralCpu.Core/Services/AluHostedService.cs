using Microsoft.Extensions.Hosting;
using NeuralCpu.Core.Components;

namespace NeuralCpu.Core.Services;

/// <summary>
/// Hosted service for ALU lifecycle management.
/// </summary>
internal sealed class AluHostedService(Alu alu) : IHostedService
{
    private readonly Alu _alu = alu ?? throw new ArgumentNullException(nameof(alu));

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // ALU is event-driven, no background task needed
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _alu.Dispose();
        return Task.CompletedTask;
    }
}
