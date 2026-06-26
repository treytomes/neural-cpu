using NeuralCpu.Core.Components;
using NeuralCpu.Core.Events;

namespace NeuralCpu.Core.Serialization;

/// <summary>
/// Complete snapshot of CPU state for serialization/deserialization.
/// Captures all CPU component state at a specific point in time.
/// </summary>
public sealed class CpuState
{
    /// <summary>
    /// Current cycle count from the clock.
    /// </summary>
    public required ulong CycleCount { get; init; }

    /// <summary>
    /// Current clock phase (High/Low).
    /// </summary>
    public required ClockPhase Phase { get; init; }

    /// <summary>
    /// Clock frequency in Hz.
    /// </summary>
    public required int FrequencyHz { get; init; }

    /// <summary>
    /// Register values (R0-R3, PC, SP, Flags).
    /// Dictionary maps RegisterId enum to ulong value.
    /// </summary>
    public required Dictionary<RegisterId, ulong> Registers { get; init; }

    /// <summary>
    /// Stack contents (top to bottom).
    /// Empty array if stack is empty.
    /// </summary>
    public required ulong[] Stack { get; init; }

    /// <summary>
    /// Memory contents (sparse representation).
    /// Only non-zero words are stored to save space.
    /// Dictionary maps address to value.
    /// </summary>
    public required Dictionary<ulong, ulong> Memory { get; init; }

    /// <summary>
    /// Total memory capacity in words.
    /// Needed to recreate Memory component with correct size.
    /// </summary>
    public required int MemoryCapacityWords { get; init; }
}
