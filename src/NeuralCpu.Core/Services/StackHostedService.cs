using Microsoft.Extensions.Hosting;

namespace NeuralCpu.Core.Services;

/// <summary>
/// Hosted service for Stack lifecycle management.
/// </summary>
internal sealed class StackHostedService(Components.Stack stack) : IHostedService
{
    private readonly Components.Stack _stack = stack ?? throw new ArgumentNullException(nameof(stack));

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Stack is event-driven, no background task needed
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stack.Dispose();
        return Task.CompletedTask;
    }
}
