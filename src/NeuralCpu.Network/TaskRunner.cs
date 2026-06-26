using Microsoft.Extensions.Logging;
using NeuralCpu.Core;
using NeuralCpu.Core.Components;
using NeuralCpu.Core.Configuration;
using NeuralCpu.Core.Events;

namespace NeuralCpu.Network;

/// <summary>
/// Orchestrates the execution of tasks by a network controller.
/// Manages the loop: observe CPU state → network decides → publish event → tick clock.
/// </summary>
public sealed class TaskRunner
{
    #region Fields

    private readonly IEventBus _eventBus;
    private readonly Clock _clock;
    private readonly RegisterFile _registerFile;
    private readonly Stack _stack;
    private readonly Memory _memory;
    private readonly INetworkController _controller;
    private readonly StateVectorBuilder _stateVectorBuilder;
    private readonly TaskRunnerOptions _options;
    private readonly ILogger<TaskRunner> _logger;

    #endregion

    #region Constructors

    public TaskRunner(
        IEventBus eventBus,
        Clock clock,
        RegisterFile registerFile,
        Stack stack,
        Memory memory,
        INetworkController controller,
        StateVectorBuilder stateVectorBuilder,
        TaskRunnerOptions options,
        ILogger<TaskRunner> logger)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _registerFile = registerFile ?? throw new ArgumentNullException(nameof(registerFile));
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _stateVectorBuilder = stateVectorBuilder ?? throw new ArgumentNullException(nameof(stateVectorBuilder));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #endregion

    #region Methods

    /// <summary>
    /// Run a single episode: reset CPU, load task, execute until halt or max cycles, evaluate result.
    /// </summary>
    public TaskResult RunEpisode(TaskDefinition task, int? maxCycles = null)
    {
        var maxCyclesValue = maxCycles ?? _options.MaxCyclesPerEpisode;
        _logger.LogInformation("Starting episode: {TaskName}, MaxCycles={MaxCycles}", task.Name, maxCyclesValue);

        // Reset everything
        _eventBus.Publish(new HardwareInterrupt(Clock.INTERRUPT_RESET));
        _controller.Reset();
        _stateVectorBuilder.Reset();

        // Load task into memory (input buffer)
        LoadTaskInput(task);

        // Main execution loop
        int cycleCount = 0;
        bool halted = false;

        while (cycleCount < maxCyclesValue && !halted)
        {
            // Build state vector
            var state = _stateVectorBuilder.Build((ulong)cycleCount);

            // Network decides next action
            var evt = _controller.DecideNextAction(state);

            // Publish control signal
            _eventBus.Publish(evt);

            // Record event in history
            _stateVectorBuilder.RecordEvent(EncodeEvent(evt));

            // Check for HALT
            if (evt is HardwareInterrupt interrupt && interrupt.Vector == Clock.INTERRUPT_HALT)
            {
                halted = true;
                _logger.LogInformation("Network issued HALT at cycle {Cycle}", cycleCount);
            }

            // Tick clock (process queued operations)
            _clock.Step();

            cycleCount++;
        }

        // Evaluate result
        var result = EvaluateTaskOutput(task, cycleCount, halted);

        _controller.OnEpisodeEnd(result.Success, result.Reward);

        _logger.LogInformation("Episode complete: Success={Success}, Reward={Reward:F2}, Cycles={Cycles}",
            result.Success, result.Reward, cycleCount);

        return result;
    }

    #endregion

    #region Private Methods

    private void LoadTaskInput(TaskDefinition task)
    {
        // Write input length
        _memory[_options.InputLengthAddress] = (ulong)task.Input.Length;

        // Write input data
        for (int i = 0; i < task.Input.Length; i++)
        {
            _memory[_options.InputBufferAddress + (ulong)i] = task.Input[i];
        }

        _logger.LogDebug("Loaded task input: {Length} items", task.Input.Length);
    }

    private TaskResult EvaluateTaskOutput(TaskDefinition task, int cycleCount, bool halted)
    {
        // Read output length
        var outputLength = (int)_memory[_options.OutputLengthAddress];

        // Read output data
        var output = new ulong[outputLength];
        for (int i = 0; i < outputLength; i++)
        {
            output[i] = _memory[_options.OutputBufferAddress + (ulong)i];
        }

        // Check correctness
        bool success = halted && outputLength == task.ExpectedOutput.Length
            && output.SequenceEqual(task.ExpectedOutput);

        // Compute reward
        double reward = success ? 1.0 : 0.0;

        // Partial credit for correct length
        if (!success && outputLength == task.ExpectedOutput.Length)
        {
            reward = 0.5;
        }

        // Partial credit for correct values (even if wrong length)
        if (!success)
        {
            int correctCount = 0;
            for (int i = 0; i < Math.Min(output.Length, task.ExpectedOutput.Length); i++)
            {
                if (output[i] == task.ExpectedOutput[i])
                    correctCount++;
            }
            var partialReward = (double)correctCount / task.ExpectedOutput.Length;
            reward = Math.Max(reward, partialReward);
        }

        return new TaskResult
        {
            Success = success,
            Reward = reward,
            CycleCount = cycleCount,
            Output = output,
            Halted = halted
        };
    }

    private EncodedEvent EncodeEvent(object evt)
    {
        // Simple encoding for now - real implementation would be more sophisticated
        return evt switch
        {
            LoadCommand load => new EncodedEvent(EventType.LoadDirect, load.Destination, value: load.Address),
            StoreCommand store => new EncodedEvent(EventType.StoreDirect, store.Source, value: store.Address),
            AluCommand alu => new EncodedEvent(MapAluOp(alu.Operation), alu.Destination, alu.SourceA, alu.SourceB),
            AluUnaryCommand aluUnary => new EncodedEvent(MapAluOp(aluUnary.Operation), aluUnary.Destination, aluUnary.Source),
            PushCommand push => new EncodedEvent(EventType.Push, push.Source),
            PopCommand pop => new EncodedEvent(EventType.Pop, pop.Destination),
            HardwareInterrupt interrupt => new EncodedEvent(EventType.HardwareInterrupt, value: interrupt.Vector),
            _ => new EncodedEvent(EventType.None)
        };
    }

    private EventType MapAluOp(AluOp op)
    {
        return op switch
        {
            AluOp.Add => EventType.AluAdd,
            AluOp.Sub => EventType.AluSub,
            AluOp.Inc => EventType.AluInc,
            AluOp.Dec => EventType.AluDec,
            _ => EventType.None
        };
    }

    #endregion
}

/// <summary>
/// Defines a task with input data and expected output.
/// </summary>
public sealed class TaskDefinition
{
    public required string Name { get; init; }
    public required ulong[] Input { get; init; }
    public required ulong[] ExpectedOutput { get; init; }
}

/// <summary>
/// Result of running a task episode.
/// </summary>
public sealed class TaskResult
{
    public required bool Success { get; init; }
    public required double Reward { get; init; }
    public required int CycleCount { get; init; }
    public required ulong[] Output { get; init; }
    public required bool Halted { get; init; }
}
