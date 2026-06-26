using NeuralCpu.Core.Components;

namespace NeuralCpu.Core.Events;

/// <summary>
/// Command to store a register value to memory (direct addressing).
/// </summary>
public sealed record StoreCommand(
    ulong Address,
    RegisterId Source)
{
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
}
