namespace NeuralCpu.Core.Events;

/// <summary>
/// Event published when the clock signal changes phase.
/// Components can subscribe to react on rising or falling edges.
/// </summary>
public sealed record ClockPhaseChanged(
    ClockPhase Phase,
    ulong CycleCount)
{
    /// <summary>
    /// Gets the timestamp when this phase change occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// True if this represents a rising edge (LOW → HIGH).
    /// </summary>
    public bool IsRisingEdge => Phase == ClockPhase.High;

    /// <summary>
    /// True if this represents a falling edge (HIGH → LOW).
    /// </summary>
    public bool IsFallingEdge => Phase == ClockPhase.Low;

    public override string ToString() =>
        $"Clock {Phase} (Cycle {CycleCount}) @ {Timestamp:HH:mm:ss.fff}";
}
