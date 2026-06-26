using NeuralCpu.Core.Components;

namespace NeuralCpu.Core.Events;

/// <summary>
/// Command to peek at the top stack value (read without popping) to a register on the next clock cycle.
/// </summary>
public sealed record PeekCommand(RegisterId Destination)
{
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
}
