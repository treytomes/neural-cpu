namespace NeuralCpu.Core.Events;

/// <summary>
/// Command to push an immediate value onto the stack on the next clock cycle.
/// </summary>
public sealed record PushImmediateCommand(ulong Value)
{
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
}
