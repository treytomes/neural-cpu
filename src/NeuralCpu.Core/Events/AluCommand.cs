using NeuralCpu.Core.Components;

namespace NeuralCpu.Core.Events;

/// <summary>
/// Command to perform a binary ALU operation using two register operands.
/// Result is written to destination register with flag updates on the next clock cycle.
/// </summary>
public sealed record AluCommand(
    AluOp Operation,
    RegisterId SourceA,
    RegisterId SourceB,
    RegisterId Destination)
{
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
}
