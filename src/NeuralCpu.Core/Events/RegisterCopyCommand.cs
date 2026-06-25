using NeuralCpu.Core.Components;

namespace NeuralCpu.Core.Events;

/// <summary>
/// Command to copy a value from one register to another on the next clock cycle.
/// Similar to enabling read on source and write on destination in a physical CPU.
/// </summary>
public sealed record RegisterCopyCommand(
    RegisterId SourceRegister,
    RegisterId DestinationRegister)
{
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
}
