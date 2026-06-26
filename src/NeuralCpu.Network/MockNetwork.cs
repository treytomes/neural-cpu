using Microsoft.Extensions.Logging;
using NeuralCpu.Core.Components;
using NeuralCpu.Core.Configuration;
using NeuralCpu.Core.Events;

namespace NeuralCpu.Network;

/// <summary>
/// Rule-based controller that implements simple tasks to validate the CPU-Network interface.
/// This is a deterministic "network" that follows hard-coded rules rather than learning.
/// Useful for testing the infrastructure before integrating real neural networks.
/// </summary>
public sealed class MockNetwork : INetworkController
{
    #region Fields

    private readonly ILogger<MockNetwork> _logger;
    private readonly TaskRunnerOptions _options;
    private int _step;
    private MockNetworkTask _currentTask;

    #endregion

    #region Constructors

    public MockNetwork(ILogger<MockNetwork> logger, TaskRunnerOptions options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _currentTask = MockNetworkTask.Echo;
        _step = 0;
    }

    #endregion

    #region Properties

    /// <summary>
    /// Current task the mock network is executing.
    /// </summary>
    public MockNetworkTask CurrentTask
    {
        get => _currentTask;
        set
        {
            _currentTask = value;
            _logger.LogInformation("Mock network task changed to: {Task}", value);
        }
    }

    #endregion

    #region INetworkController Implementation

    public object DecideNextAction(CpuStateVector state)
    {
        return _currentTask switch
        {
            MockNetworkTask.Echo => ExecuteEchoTask(state),
            MockNetworkTask.Increment => ExecuteIncrementTask(state),
            MockNetworkTask.Reverse => ExecuteReverseTask(state),
            _ => new HardwareInterrupt(Clock.INTERRUPT_HALT)
        };
    }

    public void OnEpisodeEnd(bool success, double reward)
    {
        _logger.LogInformation("Episode ended: Success={Success}, Reward={Reward:F2}, Steps={Steps}",
            success, reward, _step);
    }

    public void Reset()
    {
        _step = 0;
        _logger.LogDebug("Mock network reset");
    }

    #endregion

    #region Echo Task

    /// <summary>
    /// Echo task: Read from _options.InputBufferAddress, copy to _options.OutputBufferAddress.
    /// Tests basic LOAD/STORE operations and memory-mapped I/O.
    ///
    /// Algorithm:
    /// 1. LOAD _options.InputLengthAddress → R0 (how many items to copy)
    /// 2. PUSH 0 (loop counter i = 0)
    /// 3. Loop:
    ///    a. PEEK → R1 (get current i)
    ///    b. If R1 >= R0, goto Done
    ///    c. LOAD [_options.InputBufferAddress + R1] → R2 (read input[i])
    ///    d. STORE R2 → [_options.OutputBufferAddress + R1] (write output[i])
    ///    e. INC R1
    ///    f. POP (discard old i)
    ///    g. PUSH R1 (save new i)
    ///    h. goto Loop
    /// 4. Done:
    ///    a. STORE R0 → _options.OutputLengthAddress
    ///    b. HALT
    /// </summary>
    private object ExecuteEchoTask(CpuStateVector state)
    {
        var r0 = state.Registers[(int)RegisterId.R0];
        var r1 = state.Registers[(int)RegisterId.R1];
        var sp = state.Registers[(int)RegisterId.SP];

        object evt = _step switch
        {
            // Step 0: Load input length
            0 => new LoadCommand(_options.InputLengthAddress, RegisterId.R0),

            // Step 1: Initialize loop counter (push 0)
            1 => new PushImmediateCommand(0),

            // Step 2: Peek counter
            2 => new PeekCommand(RegisterId.R1),

            // Step 3: Check if done (compare R1 vs R0)
            // If R1 >= R0, jump to cleanup (step 10)
            3 => r1 >= r0 ? new LoadCommand(0, RegisterId.R3) : new LoadCommand(0, RegisterId.R3),

            // Step 4: Add R1 to _options.InputBufferAddress base address → R3
            4 => new AluCommand(AluOp.Add, RegisterId.R1, RegisterId.R1, RegisterId.R3),

            // Step 5: Load indirect [R3] → R2 (read input[i])
            5 => new LoadCommand(_options.InputBufferAddress + r1, RegisterId.R2),

            // Step 6: Store R2 → [_options.OutputBufferAddress + R1]
            6 => new StoreCommand(_options.OutputBufferAddress + r1, RegisterId.R2),

            // Step 7: Increment R1
            7 => new AluUnaryCommand(AluOp.Inc, RegisterId.R1, RegisterId.R1),

            // Step 8: Pop old counter
            8 => new PopCommand(RegisterId.R3),

            // Step 9: Push new counter, loop back to step 2
            9 when _step++ >= 0 => new PushCommand(RegisterId.R1),

            // Step 10: Store output length
            10 => new StoreCommand(_options.OutputLengthAddress, RegisterId.R0),

            // Step 11: Halt
            _ => new HardwareInterrupt(Clock.INTERRUPT_HALT)
        };

        // Loop step 9 back to step 2
        if (_step == 10 && r1 < r0)
        {
            _step = 2;
        }
        else
        {
            _step++;
        }

        _logger.LogTrace("Echo step {Step}: {Event}", _step - 1, evt.GetType().Name);
        return evt;
    }

    #endregion

    #region Increment Task

    /// <summary>
    /// Increment task: Read numbers from _options.InputBufferAddress, write N+1 to _options.OutputBufferAddress.
    /// Tests ALU operations.
    /// </summary>
    private object ExecuteIncrementTask(CpuStateVector state)
    {
        var r0 = state.Registers[(int)RegisterId.R0];
        var r1 = state.Registers[(int)RegisterId.R1];

        object evt = _step switch
        {
            0 => new LoadCommand(_options.InputLengthAddress, RegisterId.R0),
            1 => new PushImmediateCommand(0),
            2 => new PeekCommand(RegisterId.R1),
            3 => r1 >= r0 ? new LoadCommand(0, RegisterId.R3) : new LoadCommand(_options.InputBufferAddress + r1, RegisterId.R2),
            4 => new AluUnaryCommand(AluOp.Inc, RegisterId.R2, RegisterId.R2), // R2 = R2 + 1
            5 => new StoreCommand(_options.OutputBufferAddress + r1, RegisterId.R2),
            6 => new AluUnaryCommand(AluOp.Inc, RegisterId.R1, RegisterId.R1),
            7 => new PopCommand(RegisterId.R3),
            8 when _step++ >= 0 => new PushCommand(RegisterId.R1),
            9 => new StoreCommand(_options.OutputLengthAddress, RegisterId.R0),
            _ => new HardwareInterrupt(Clock.INTERRUPT_HALT)
        };

        if (_step == 9 && r1 < r0)
        {
            _step = 2;
        }
        else
        {
            _step++;
        }

        return evt;
    }

    #endregion

    #region Reverse Task

    /// <summary>
    /// Reverse task: Read _options.InputBufferAddress, write reversed sequence to _options.OutputBufferAddress.
    /// Tests stack operations (LIFO behavior).
    ///
    /// Algorithm:
    /// 1. Load all items onto stack (push loop)
    /// 2. Pop all items to output (pop loop)
    /// </summary>
    private object ExecuteReverseTask(CpuStateVector state)
    {
        var r0 = state.Registers[(int)RegisterId.R0];
        var r1 = state.Registers[(int)RegisterId.R1];

        object evt = _step switch
        {
            // Load input length
            0 => new LoadCommand(_options.InputLengthAddress, RegisterId.R0),

            // Phase 1: Push all items (use R1 as counter)
            1 => new RegisterWriteCommand(RegisterId.R1, 0),
            2 => r1 >= r0 ? new RegisterWriteCommand(RegisterId.R1, 0) : new LoadCommand(_options.InputBufferAddress + r1, RegisterId.R2),
            3 => new PushCommand(RegisterId.R2),
            4 => new AluUnaryCommand(AluOp.Inc, RegisterId.R1, RegisterId.R1),
            5 when r1 < r0 => new LoadCommand(_options.InputBufferAddress + r1, RegisterId.R2), // Loop back

            // Phase 2: Pop all items (R1 = 0 again)
            6 => new RegisterWriteCommand(RegisterId.R1, 0),
            7 => r1 >= r0 ? new StoreCommand(_options.OutputLengthAddress, RegisterId.R0) : new PopCommand(RegisterId.R2),
            8 => new StoreCommand(_options.OutputBufferAddress + r1, RegisterId.R2),
            9 => new AluUnaryCommand(AluOp.Inc, RegisterId.R1, RegisterId.R1),
            10 when r1 < r0 => new PopCommand(RegisterId.R2), // Loop back

            // Done
            11 => new StoreCommand(_options.OutputLengthAddress, RegisterId.R0),
            _ => new HardwareInterrupt(Clock.INTERRUPT_HALT)
        };

        // Manage loop transitions
        if (_step == 5 && r1 < r0)
        {
            _step = 2; // Continue pushing
        }
        else if (_step == 10 && r1 < r0)
        {
            _step = 7; // Continue popping
        }
        else
        {
            _step++;
        }

        return evt;
    }

    #endregion
}

/// <summary>
/// Available tasks for the mock network.
/// </summary>
public enum MockNetworkTask
{
    /// <summary>Copy input to output (tests LOAD/STORE)</summary>
    Echo,

    /// <summary>Add 1 to each input value (tests ALU)</summary>
    Increment,

    /// <summary>Reverse the input sequence (tests Stack)</summary>
    Reverse,
}
