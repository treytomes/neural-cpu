using NeuralCpu.Core.Components;

namespace NeuralCpu.Core.Events;

/// <summary>
/// Command to push a register value onto the stack on the next clock cycle.
/// </summary>
public sealed record PushCommand(RegisterId Source)
{
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
}
