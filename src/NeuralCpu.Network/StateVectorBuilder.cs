using NeuralCpu.Core.Components;
using NeuralCpu.Core.Configuration;

namespace NeuralCpu.Network;

/// <summary>
/// Builds a complete state vector snapshot from CPU components.
/// The network observes this state each cycle to decide the next action.
/// </summary>
public sealed class StateVectorBuilder
{
    #region Fields

    private readonly RegisterFile _registerFile;
    private readonly Stack _stack;
    private readonly Memory _memory;
    private readonly Queue<EncodedEvent> _eventHistory;
    private readonly StateVectorOptions _stateVectorOptions;
    private readonly TaskRunnerOptions _taskRunnerOptions;

    #endregion

    #region Constructors

    public StateVectorBuilder(RegisterFile registerFile, Stack stack, Memory memory,
        StateVectorOptions stateVectorOptions, TaskRunnerOptions taskRunnerOptions)
    {
        _registerFile = registerFile ?? throw new ArgumentNullException(nameof(registerFile));
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _stateVectorOptions = stateVectorOptions ?? throw new ArgumentNullException(nameof(stateVectorOptions));
        _taskRunnerOptions = taskRunnerOptions ?? throw new ArgumentNullException(nameof(taskRunnerOptions));
        _eventHistory = new Queue<EncodedEvent>(_stateVectorOptions.EventHistorySize);
    }

    #endregion

    #region Methods

    /// <summary>
    /// Record an event in the history window.
    /// Maintains a sliding window of recent events.
    /// </summary>
    public void RecordEvent(EncodedEvent evt)
    {
        _eventHistory.Enqueue(evt);

        // Maintain fixed window size
        while (_eventHistory.Count > _stateVectorOptions.EventHistorySize)
        {
            _eventHistory.Dequeue();
        }
    }

    /// <summary>
    /// Build a complete state vector snapshot.
    /// </summary>
    public CpuStateVector Build(ulong cycleCount)
    {
        var state = new CpuStateVector
        {
            // Copy all registers
            Registers = new[]
            {
                _registerFile[RegisterId.R0],
                _registerFile[RegisterId.R1],
                _registerFile[RegisterId.R2],
                _registerFile[RegisterId.R3],
                _registerFile[RegisterId.PC],
                _registerFile[RegisterId.SP],
                _registerFile[RegisterId.Flags],
            },

            // Copy event history (pad with empty events if needed)
            EventHistory = BuildEventHistory(),

            // Copy top of stack
            StackSnapshot = BuildStackSnapshot(),

            // Sample memory (hot regions + fixed locations)
            MemoryAddresses = BuildMemoryAddresses(),
            MemoryValues = BuildMemoryValues(),

            CycleCount = cycleCount,
        };

        return state;
    }

    /// <summary>
    /// Clear event history (for episode reset).
    /// </summary>
    public void Reset()
    {
        _eventHistory.Clear();
    }

    #endregion

    #region Private Methods

    private EncodedEvent[] BuildEventHistory()
    {
        var history = new EncodedEvent[_stateVectorOptions.EventHistorySize];
        var events = _eventHistory.ToArray();

        // Copy events to array (older events first)
        for (int i = 0; i < events.Length && i < history.Length; i++)
        {
            history[i] = events[i];
        }

        // Remaining slots are default (empty) events
        return history;
    }

    private ulong[] BuildStackSnapshot()
    {
        var snapshot = new ulong[_stateVectorOptions.StackSnapshotSize];
        var stackData = _stack.GetSnapshot();

        // Copy top N items (stack returns top-to-bottom)
        for (int i = 0; i < Math.Min(stackData.Length, snapshot.Length); i++)
        {
            snapshot[i] = stackData[i];
        }

        return snapshot;
    }

    private ulong[] BuildMemoryAddresses()
    {
        var addresses = new ulong[_stateVectorOptions.MemorySnapshotSize];

        // Get recently written addresses (hot regions)
        var hotAddresses = _memory.GetRecentlyWrittenAddresses()
            .OrderByDescending(addr => _memory.GetLastWriteTime(addr))
            .Take(8)
            .ToArray();

        // Fixed interesting regions (from configuration)
        var fixedAddresses = new ulong[]
        {
            0x00000000,                             // Zero page
            _taskRunnerOptions.InputBufferAddress,  // Input buffer start
            _taskRunnerOptions.InputLengthAddress,  // Input length
            _taskRunnerOptions.OutputBufferAddress, // Output buffer start
            _taskRunnerOptions.OutputLengthAddress, // Output length
        };

        // Combine hot + fixed
        int idx = 0;
        foreach (var addr in hotAddresses)
        {
            if (idx >= addresses.Length) break;
            addresses[idx++] = addr;
        }
        foreach (var addr in fixedAddresses)
        {
            if (idx >= addresses.Length) break;
            addresses[idx++] = addr;
        }

        return addresses;
    }

    private ulong[] BuildMemoryValues()
    {
        var values = new ulong[_stateVectorOptions.MemorySnapshotSize];
        var addresses = BuildMemoryAddresses();

        for (int i = 0; i < addresses.Length; i++)
        {
            values[i] = _memory[addresses[i]];
        }

        return values;
    }

    #endregion
}
