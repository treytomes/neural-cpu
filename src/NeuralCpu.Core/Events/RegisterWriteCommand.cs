using NeuralCpu.Core.Components;

namespace NeuralCpu.Core.Events;

/// <summary>
/// Command to write a value to a register on the next clock cycle.
/// </summary>
public sealed record RegisterWriteCommand(
    RegisterId RegisterId,
    ulong Value)
{
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
}
