using NeuralCpu.Core.Components;

namespace NeuralCpu.Network;

/// <summary>
/// Complete snapshot of CPU state visible to the network each cycle.
/// The network observes this state and decides what control signal to emit.
/// Sizes are configurable via NeuralCpuOptions.Network.StateVector.
/// </summary>
public sealed class CpuStateVector
{
    #region Constants

    // Default sizes (can be overridden via configuration)
    public const int DefaultEventHistorySize = 64;
    public const int DefaultStackSnapshotSize = 8;
    public const int DefaultMemorySnapshotSize = 16;

    #endregion

    #region Properties

    /// <summary>All CPU registers (R0-R3, PC, SP, Flags)</summary>
    public ulong[] Registers { get; init; } = new ulong[7];

    /// <summary>Recent event history window (oldest to newest)</summary>
    public EncodedEvent[] EventHistory { get; init; } = new EncodedEvent[DefaultEventHistorySize];

    /// <summary>Top N items on the stack</summary>
    public ulong[] StackSnapshot { get; init; } = new ulong[DefaultStackSnapshotSize];

    /// <summary>Sampled memory addresses (hot regions + fixed locations)</summary>
    public ulong[] MemoryAddresses { get; init; } = new ulong[DefaultMemorySnapshotSize];

    /// <summary>Values at the sampled memory addresses</summary>
    public ulong[] MemoryValues { get; init; } = new ulong[DefaultMemorySnapshotSize];

    /// <summary>Current cycle count (temporal awareness)</summary>
    public ulong CycleCount { get; init; }

    #endregion

    #region Methods

    public override string ToString()
    {
        return $"Cycle={CycleCount}, R0=0x{Registers[0]:X}, PC={Registers[4]}, SP={Registers[5]}, Flags={Registers[6]:X2}";
    }

    #endregion
}
