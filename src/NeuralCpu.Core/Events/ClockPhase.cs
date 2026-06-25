namespace NeuralCpu.Core.Events;

/// <summary>
/// Represents the phase of the clock signal.
/// </summary>
public enum ClockPhase
{
    /// <summary>
    /// Low voltage state (falling edge completed).
    /// </summary>
    Low,

    /// <summary>
    /// High voltage state (rising edge completed).
    /// </summary>
    High
}
