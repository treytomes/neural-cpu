namespace NeuralCpu.Core.Events;

/// <summary>
/// Event representing a hardware interrupt signal.
/// The clock queues these and processes them at cycle boundaries.
/// </summary>
public sealed record HardwareInterrupt(byte Vector, object? Data = null)
{
    /// <summary>
    /// Gets the timestamp when the interrupt was raised.
    /// </summary>
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;

    public override string ToString() =>
        $"Interrupt Vector={Vector:X2} @ {Timestamp:HH:mm:ss.fff}";
}
