namespace NeuralCpu.Core.Events;

/// <summary>
/// Command to duplicate the top stack value (push a copy) on the next clock cycle.
/// </summary>
public sealed record DupCommand
{
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
}
