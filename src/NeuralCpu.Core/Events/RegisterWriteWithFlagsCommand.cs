using NeuralCpu.Core.Components;

namespace NeuralCpu.Core.Events;

/// <summary>
/// Command to write a value to a register and update the flags register based on the value.
/// Used by ALU operations and other operations that should affect CPU flags.
/// </summary>
public sealed record RegisterWriteWithFlagsCommand(
    RegisterId RegisterId,
    ulong Value)
{
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
}
