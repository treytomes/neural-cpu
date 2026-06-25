using NeuralCpu.Core.Components;

namespace NeuralCpu.Core.Events;

/// <summary>
/// Command to perform a unary ALU operation (currently only NOT).
/// Result is written to destination register with flag updates on the next clock cycle.
/// </summary>
public sealed record AluUnaryCommand(
    AluOp Operation,
    RegisterId Source,
    RegisterId Destination)
{
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
}
