using NeuralCpu.Core.Components;

namespace NeuralCpu.Core.Events;

/// <summary>
/// Published after a register value has been successfully written.
/// Allows observers to track register state changes.
/// </summary>
public sealed record RegisterChanged(
    RegisterId RegisterId,
    ulong OldValue,
    ulong NewValue,
    ulong CycleCount)
{
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
}
