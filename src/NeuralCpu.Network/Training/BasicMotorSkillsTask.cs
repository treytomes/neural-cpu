using NeuralCpu.Core;
using NeuralCpu.Core.Components;
using NeuralCpu.Core.Events;
using Microsoft.Extensions.Logging;

namespace NeuralCpu.Network.Training;

/// <summary>
/// Basic motor skills tasks for Phase 5.5.3: Component Mastery.
/// Each task tests whether the network can control a specific CPU component.
/// Simple binary rewards: 1.0 for success, 0.0 for failure.
/// </summary>
public sealed class BasicMotorSkillsTask
{
    #region Fields

    private readonly IEventBus _eventBus;
    private readonly Clock _clock;
    private readonly RegisterFile _registerFile;
    private readonly Stack _stack;
    private readonly Memory _memory;
    private readonly INetworkController _controller;
    private readonly StateVectorBuilder _stateVectorBuilder;
    private readonly ILogger<BasicMotorSkillsTask> _logger;
    private readonly int _maxCyclesPerEpisode;

    // For device I/O tasks
    private readonly TaskInputDevice? _inputDevice;
    private readonly TaskOutputDevice? _outputDevice;

    #endregion

    #region Constructors

    public BasicMotorSkillsTask(
        IEventBus eventBus,
        Clock clock,
        RegisterFile registerFile,
        Stack stack,
        Memory memory,
        INetworkController controller,
        StateVectorBuilder stateVectorBuilder,
        ILogger<BasicMotorSkillsTask> logger,
        int maxCyclesPerEpisode = 50,
        TaskInputDevice? inputDevice = null,
        TaskOutputDevice? outputDevice = null)
    {
        _eventBus = eventBus;
        _clock = clock;
        _registerFile = registerFile;
        _stack = stack;
        _memory = memory;
        _controller = controller;
        _stateVectorBuilder = stateVectorBuilder;
        _logger = logger;
        _maxCyclesPerEpisode = maxCyclesPerEpisode;
        _inputDevice = inputDevice;
        _outputDevice = outputDevice;
    }

    #endregion

    #region Task Types

    /// <summary>
    /// RegisterActivation: Make any general-purpose register (R0-R3) non-zero.
    /// Success: At least one of R0-R3 != 0.
    /// Tests: Can network discover RegisterWrite event?
    /// </summary>
    public TaskResult RunRegisterActivationTask()
    {
        ResetCpu();

        for (int cycle = 0; cycle < _maxCyclesPerEpisode; cycle++)
        {
            ExecuteCycle();

            // Check if any general-purpose register is non-zero
            if (_registerFile[RegisterId.R0] != 0 ||
                _registerFile[RegisterId.R1] != 0 ||
                _registerFile[RegisterId.R2] != 0 ||
                _registerFile[RegisterId.R3] != 0)
            {
                _logger.LogInformation("RegisterActivation succeeded in {Cycles} cycles", cycle + 1);
                return new TaskResult(
                    TaskName: "RegisterActivation",
                    Success: true,
                    Reward: 1.0,
                    CycleCount: cycle + 1);
            }
        }

        _logger.LogWarning("RegisterActivation failed after {Cycles} cycles", _maxCyclesPerEpisode);
        return new TaskResult("RegisterActivation", Success: false, Reward: 0.0, CycleCount: _maxCyclesPerEpisode);
    }

    /// <summary>
    /// StackGrowth: Increase stack size from 0.
    /// Success: Stack size (SP) > 0.
    /// Tests: Can network discover Push or PushImmediate event?
    /// </summary>
    public TaskResult RunStackGrowthTask()
    {
        ResetCpu();

        for (int cycle = 0; cycle < _maxCyclesPerEpisode; cycle++)
        {
            ExecuteCycle();

            // Check if stack has grown
            if (_registerFile[RegisterId.SP] > 0)
            {
                _logger.LogInformation("StackGrowth succeeded in {Cycles} cycles, SP={SP}", cycle + 1, _registerFile[RegisterId.SP]);
                return new TaskResult(
                    TaskName: "StackGrowth",
                    Success: true,
                    Reward: 1.0,
                    CycleCount: cycle + 1);
            }
        }

        _logger.LogWarning("StackGrowth failed after {Cycles} cycles", _maxCyclesPerEpisode);
        return new TaskResult("StackGrowth", Success: false, Reward: 0.0, CycleCount: _maxCyclesPerEpisode);
    }

    /// <summary>
    /// ALUActivation: Execute any ALU operation that changes a register.
    /// Success: Flags register changes (ALU operations set flags).
    /// Tests: Can network discover ALU commands?
    /// </summary>
    public TaskResult RunAluActivationTask()
    {
        ResetCpu();
        var initialFlags = _registerFile[RegisterId.Flags];

        for (int cycle = 0; cycle < _maxCyclesPerEpisode; cycle++)
        {
            ExecuteCycle();

            // Check if Flags register changed (ALU ops set flags)
            var currentFlags = _registerFile[RegisterId.Flags];
            if (currentFlags != initialFlags)
            {
                _logger.LogInformation("ALUActivation succeeded in {Cycles} cycles, Flags changed: {OldFlags:X} → {NewFlags:X}",
                    cycle + 1, initialFlags, currentFlags);
                return new TaskResult(
                    TaskName: "ALUActivation",
                    Success: true,
                    Reward: 1.0,
                    CycleCount: cycle + 1);
            }
        }

        _logger.LogWarning("ALUActivation failed after {Cycles} cycles", _maxCyclesPerEpisode);
        return new TaskResult("ALUActivation", Success: false, Reward: 0.0, CycleCount: _maxCyclesPerEpisode);
    }

    /// <summary>
    /// InputRead: Read a value from input device 0.
    /// Success: InputReadRequest event issued to device 0.
    /// Tests: Can network discover device I/O operations?
    /// </summary>
    public TaskResult RunInputReadTask()
    {
        if (_inputDevice == null)
            throw new InvalidOperationException("InputReadTask requires TaskInputDevice to be provided");

        ResetCpu();
        _inputDevice.LoadInput(new[] { 42UL, 123UL }); // Load test data

        bool inputReadIssued = false;
        IDisposable? subscription = null;

        try
        {
            // Subscribe to InputReadRequest events
            subscription = _eventBus.Subscribe<InputReadRequest>(new InputReadObserver(() => inputReadIssued = true));

            for (int cycle = 0; cycle < _maxCyclesPerEpisode; cycle++)
            {
                ExecuteCycle();

                if (inputReadIssued)
                {
                    _logger.LogInformation("InputRead succeeded in {Cycles} cycles", cycle + 1);
                    return new TaskResult(
                        TaskName: "InputRead",
                        Success: true,
                        Reward: 1.0,
                        CycleCount: cycle + 1);
                }
            }

            _logger.LogWarning("InputRead failed after {Cycles} cycles", _maxCyclesPerEpisode);
            return new TaskResult("InputRead", Success: false, Reward: 0.0, CycleCount: _maxCyclesPerEpisode);
        }
        finally
        {
            subscription?.Dispose();
        }
    }

    /// <summary>
    /// OutputWrite: Write any value to output device 0.
    /// Success: OutputWriteCommand event issued to device 0.
    /// Tests: Can network discover output operations?
    /// </summary>
    public TaskResult RunOutputWriteTask()
    {
        if (_outputDevice == null)
            throw new InvalidOperationException("OutputWriteTask requires TaskOutputDevice to be provided");

        ResetCpu();
        _outputDevice.Clear();

        bool outputWriteIssued = false;
        IDisposable? subscription = null;

        try
        {
            // Subscribe to OutputWriteCommand events
            subscription = _eventBus.Subscribe<OutputWriteCommand>(new OutputWriteObserver(() => outputWriteIssued = true));

            for (int cycle = 0; cycle < _maxCyclesPerEpisode; cycle++)
            {
                ExecuteCycle();

                if (outputWriteIssued)
                {
                    _logger.LogInformation("OutputWrite succeeded in {Cycles} cycles", cycle + 1);
                    return new TaskResult(
                        TaskName: "OutputWrite",
                        Success: true,
                        Reward: 1.0,
                        CycleCount: cycle + 1);
                }
            }

            _logger.LogWarning("OutputWrite failed after {Cycles} cycles", _maxCyclesPerEpisode);
            return new TaskResult("OutputWrite", Success: false, Reward: 0.0, CycleCount: _maxCyclesPerEpisode);
        }
        finally
        {
            subscription?.Dispose();
        }
    }

    #endregion

    #region Helper Methods

    private void ResetCpu()
    {
        _eventBus.Publish(new SystemReset());
        _controller.Reset();
    }

    private void ExecuteCycle()
    {
        var state = _stateVectorBuilder.Build(_registerFile[RegisterId.PC]);
        var action = _controller.DecideNextAction(state);

        if (action != null)
        {
            _eventBus.Publish(action);
        }

        _clock.Step();
    }

    #endregion

    #region Observer Classes

    private sealed class InputReadObserver : IObserver<InputReadRequest>
    {
        private readonly Action _onNext;
        public InputReadObserver(Action onNext) => _onNext = onNext;
        public void OnNext(InputReadRequest value) => _onNext();
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }

    private sealed class OutputWriteObserver : IObserver<OutputWriteCommand>
    {
        private readonly Action _onNext;
        public OutputWriteObserver(Action onNext) => _onNext = onNext;
        public void OnNext(OutputWriteCommand value) => _onNext();
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }

    #endregion
}

/// <summary>
/// Result of a basic motor skills task.
/// </summary>
public record TaskResult(
    string TaskName,
    bool Success,
    double Reward,
    int CycleCount);
