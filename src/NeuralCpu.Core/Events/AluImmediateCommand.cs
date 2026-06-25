using NeuralCpu.Core.Components;

namespace NeuralCpu.Core.Events;

/// <summary>
/// Command to perform a binary ALU operation using a register and an immediate value.
/// Result is written to destination register with flag updates on the next clock cycle.
/// </summary>
public sealed record AluImmediateCommand(
    AluOp Operation,
    RegisterId Source,
    ulong Immediate,
    RegisterId Destination)
{
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
}
