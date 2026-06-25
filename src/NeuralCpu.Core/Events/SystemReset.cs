namespace NeuralCpu.Core.Events;

/// <summary>
/// System-wide reset signal that resets all components to their initial state.
/// Typically triggered by hardware interrupt vector 0x01.
/// </summary>
public sealed record SystemReset
{
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
}
