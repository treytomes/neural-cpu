using Microsoft.Extensions.Logging;
using NeuralCpu.Core.Components;
using NeuralCpu.Core.Configuration;
using NeuralCpu.Core.Events;

namespace NeuralCpu.Network;

/// <summary>
/// Cycle-based mock network that uses absolute cycle count to determine actions.
/// Simplest possible implementation that accounts for one-cycle delay.
/// </summary>
public sealed class CycleMockNetwork : INetworkController
{
    private readonly ILogger<CycleMockNetwork> _logger;
    private readonly TaskRunnerOptions _options;
    private MockNetworkTask _currentTask;
    private int _cycle;

    public CycleMockNetwork(ILogger<CycleMockNetwork> logger, TaskRunnerOptions options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _currentTask = MockNetworkTask.Echo;
    }

    public MockNetworkTask CurrentTask
    {
        get => _currentTask;
        set
        {
            _currentTask = value;
            _logger.LogInformation("Task changed to: {Task}", value);
        }
    }

    public object DecideNextAction(CpuStateVector state)
    {
        var r0 = state.Registers[(int)RegisterId.R0];
        var r1 = state.Registers[(int)RegisterId.R1];
        var sp = state.Registers[(int)RegisterId.SP];

        _logger.LogTrace("Cycle {Cycle}: R0={R0}, R1={R1}, SP={SP}", _cycle, r0, r1, sp);

        var action = _currentTask switch
        {
            MockNetworkTask.Echo => ExecuteEcho(state),
            MockNetworkTask.Increment => ExecuteIncrement(state),
            MockNetworkTask.Reverse => ExecuteReverse(state),
            _ => new HardwareInterrupt(Clock.INTERRUPT_HALT)
        };

        _logger.LogTrace("Cycle {Cycle}: Action={Action}", _cycle, action.GetType().Name);
        _cycle++;
        return action;
    }

    public void OnEpisodeEnd(bool success, double reward)
    {
        _logger.LogInformation("Episode ended: Success={Success}, Reward={Reward:F2}, Cycles={Cycles}",
            success, reward, _cycle);
    }

    public void Reset()
    {
        _cycle = 0;
        _logger.LogDebug("Network reset");
    }

    #region Echo Task

    /// <summary>
    /// Echo: Copy input to output.
    /// Cycle 0: LOAD InputLength → R0
    /// Cycle 1: R0 now has length, WRITE 0 → R1
    /// Cycle 2+: Loop - LOAD input[i], STORE output[i], INC i
    /// </summary>
    private object ExecuteEcho(CpuStateVector state)
    {
        var r0 = state.Registers[(int)RegisterId.R0];
        var r1 = state.Registers[(int)RegisterId.R1];

        return _cycle switch
        {
            0 => new LoadCommand(_options.InputLengthAddress, RegisterId.R0),
            1 => new RegisterWriteCommand(RegisterId.R1, 0),
            _ => ExecuteEchoLoop(r0, r1)
        };
    }

    private object ExecuteEchoLoop(ulong length, ulong counter)
    {
        // Cycle 2: Start of first iteration - length should now be loaded from cycle 0's LOAD
        // But we need to check if we've actually completed the loop
        // The loop executes: Load, Store, Inc, then checks counter again

        // Check if done (counter has been incremented past length)
        if (counter >= length && length > 0) // length > 0 ensures we've actually loaded it
        {
            // First time seeing counter >= length: store output length
            if (_cycle == 2 || (_cycle - 2) % 3 == 0) // On a "check" cycle
            {
                return new StoreCommand(_options.OutputLengthAddress, RegisterId.R0);
            }
            // Next cycle: halt
            return new HardwareInterrupt(Clock.INTERRUPT_HALT);
        }

        // Special case: if length is 0 (empty input), store length and halt immediately
        if (length == 0 && _cycle == 2)
        {
            return new StoreCommand(_options.OutputLengthAddress, RegisterId.R0);
        }
        if (length == 0 && _cycle == 3)
        {
            return new HardwareInterrupt(Clock.INTERRUPT_HALT);
        }

        // Loop body: Load, Store, Inc (3 cycles per iteration)
        var loopCycle = (_cycle - 2) % 3;
        return loopCycle switch
        {
            0 => new LoadCommand(_options.InputBufferAddress + counter, RegisterId.R2),
            1 => new StoreCommand(_options.OutputBufferAddress + counter, RegisterId.R2),
            _ => new AluUnaryCommand(AluOp.Inc, RegisterId.R1, RegisterId.R1)
        };
    }

    #endregion

    #region Increment Task

    /// <summary>
    /// Increment: Add 1 to each value.
    /// Cycle 0: LOAD InputLength → R0
    /// Cycle 1: WRITE 0 → R1
    /// Cycle 2+: Loop - LOAD input[i], INC R2, STORE output[i], INC i (4 cycles per iteration)
    /// </summary>
    private object ExecuteIncrement(CpuStateVector state)
    {
        var r0 = state.Registers[(int)RegisterId.R0];
        var r1 = state.Registers[(int)RegisterId.R1];

        return _cycle switch
        {
            0 => new LoadCommand(_options.InputLengthAddress, RegisterId.R0),
            1 => new RegisterWriteCommand(RegisterId.R1, 0),
            _ => ExecuteIncrementLoop(r0, r1)
        };
    }

    private object ExecuteIncrementLoop(ulong length, ulong counter)
    {
        if (counter >= length)
        {
            if (_cycle == 2 || (_cycle - 2) % 4 == 0)
            {
                return new StoreCommand(_options.OutputLengthAddress, RegisterId.R0);
            }
            return new HardwareInterrupt(Clock.INTERRUPT_HALT);
        }

        var loopCycle = (_cycle - 2) % 4;
        return loopCycle switch
        {
            0 => new LoadCommand(_options.InputBufferAddress + counter, RegisterId.R2),
            1 => new AluUnaryCommand(AluOp.Inc, RegisterId.R2, RegisterId.R2),
            2 => new StoreCommand(_options.OutputBufferAddress + counter, RegisterId.R2),
            _ => new AluUnaryCommand(AluOp.Inc, RegisterId.R1, RegisterId.R1)
        };
    }

    #endregion

    #region Reverse Task

    /// <summary>
    /// Reverse: Use stack to reverse sequence.
    /// Phase 1 - Push all: LOAD input[i], PUSH, INC i (3 cycles per item)
    /// Phase 2 - Pop all: POP, STORE output[i], INC i (3 cycles per item)
    /// </summary>
    private object ExecuteReverse(CpuStateVector state)
    {
        var r0 = state.Registers[(int)RegisterId.R0];
        var r1 = state.Registers[(int)RegisterId.R1];
        var sp = state.Registers[(int)RegisterId.SP];

        return _cycle switch
        {
            0 => new LoadCommand(_options.InputLengthAddress, RegisterId.R0),
            1 => new RegisterWriteCommand(RegisterId.R1, 0),
            _ => ExecuteReversePhases(r0, r1, sp)
        };
    }

    private object ExecuteReversePhases(ulong length, ulong counter, ulong stackPointer)
    {
        // Determine which phase we're in based on cycle count
        // Phase 1: cycles 2 to (1 + 3*length)
        // Transition: cycle (2 + 3*length)
        // Phase 2: cycle (3 + 3*length) onwards

        int phase1End = 1 + (int)(3 * length);
        int transitionCycle = 2 + (int)(3 * length);
        int phase2Start = 3 + (int)(3 * length);

        // Phase 1: Push all items
        if (_cycle <= phase1End)
        {
            var pushCycle = (_cycle - 2) % 3;
            return pushCycle switch
            {
                0 => new LoadCommand(_options.InputBufferAddress + counter, RegisterId.R2),
                1 => new PushCommand(RegisterId.R2),
                _ => new AluUnaryCommand(AluOp.Inc, RegisterId.R1, RegisterId.R1)
            };
        }

        // Transition: Reset counter for phase 2
        if (_cycle == transitionCycle)
        {
            return new RegisterWriteCommand(RegisterId.R1, 0);
        }

        // Phase 2: Pop all items
        if (_cycle >= phase2Start && counter < length)
        {
            var popCycle = (_cycle - phase2Start) % 3;
            return popCycle switch
            {
                0 => new PopCommand(RegisterId.R2),
                1 => new StoreCommand(_options.OutputBufferAddress + counter, RegisterId.R2),
                _ => new AluUnaryCommand(AluOp.Inc, RegisterId.R1, RegisterId.R1)
            };
        }

        // Done: Store length, then halt
        int phase2End = phase2Start + (int)(3 * length);
        if (_cycle == phase2End)
        {
            return new StoreCommand(_options.OutputLengthAddress, RegisterId.R0);
        }
        return new HardwareInterrupt(Clock.INTERRUPT_HALT);
    }

    #endregion
}
