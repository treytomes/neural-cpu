using Microsoft.Extensions.Logging.Abstractions;
using NeuralCpu.Core;
using NeuralCpu.Core.Components;
using NeuralCpu.Core.Configuration;
using NeuralCpu.Core.Events;
using NeuralCpu.Network.Training;

namespace NeuralCpu.Network.Tests.Training;

/// <summary>
/// Unit tests for BasicMotorSkillsTask.
/// Tests task success conditions with deterministic mock network.
/// </summary>
public sealed class BasicMotorSkillsTaskTests : IDisposable
{
    #region Fields

    private readonly IEventBus _eventBus;
    private readonly Clock _clock;
    private readonly RegisterFile _registerFile;
    private readonly Alu _alu;
    private readonly Stack _stack;
    private readonly Memory _memory;
    private readonly StateVectorBuilder _stateVectorBuilder;
    private readonly TaskInputDevice _inputDevice;
    private readonly TaskOutputDevice _outputDevice;
    private readonly DeviceRegistry _deviceRegistry;

    #endregion

    #region Constructors

    public BasicMotorSkillsTaskTests()
    {
        _eventBus = new EventBus();
        _clock = new Clock(_eventBus, NullLogger<Clock>.Instance);
        _registerFile = new RegisterFile(_eventBus, NullLogger<RegisterFile>.Instance);
        _alu = new Alu(_eventBus, _registerFile, NullLogger<Alu>.Instance);
        _stack = new Stack(_eventBus, _registerFile, NullLogger<Stack>.Instance);
        _memory = new Memory(_eventBus, _registerFile, NullLogger<Memory>.Instance, capacityWords: 1024);

        // Create devices and registry
        _inputDevice = new TaskInputDevice();
        _outputDevice = new TaskOutputDevice();
        _deviceRegistry = new DeviceRegistry(_eventBus, NullLogger<DeviceRegistry>.Instance);
        _deviceRegistry.RegisterInputDevice(_inputDevice);
        _deviceRegistry.RegisterOutputDevice(_outputDevice);

        var stateVectorOptions = new StateVectorOptions
        {
            EventHistorySize = 64,
            StackSnapshotSize = 8,
            MemorySnapshotSize = 16
        };
        var taskRunnerOptions = new TaskRunnerOptions
        {
            MaxCyclesPerEpisode = 50,
            InputBufferAddress = 0x80000000,
            InputLengthAddress = 0x80000FF8,
            OutputBufferAddress = 0x80001000,
            OutputLengthAddress = 0x80001FF8
        };

        _stateVectorBuilder = new StateVectorBuilder(_registerFile, _stack, _memory, stateVectorOptions, taskRunnerOptions);
    }

    #endregion

    #region RegisterActivation Tests

    [Fact]
    public void RegisterActivationTask_WithRegisterWriteController_Succeeds()
    {
        // Arrange: Controller that writes to R0
        var controller = new DeterministicController(
            action: new RegisterWriteCommand(RegisterId.R0, 42));

        var task = new BasicMotorSkillsTask(
            _eventBus, _clock, _registerFile, _stack, _memory,
            controller, _stateVectorBuilder,
            NullLogger<BasicMotorSkillsTask>.Instance,
            maxCyclesPerEpisode: 10);

        // Act
        var result = task.RunRegisterActivationTask();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1.0, result.Reward);
        Assert.Equal("RegisterActivation", result.TaskName);
        Assert.True(result.CycleCount <= 10);
    }

    [Fact]
    public void RegisterActivationTask_WithNoOpController_Fails()
    {
        // Arrange: Controller that does nothing
        var controller = new DeterministicController(action: null);

        var task = new BasicMotorSkillsTask(
            _eventBus, _clock, _registerFile, _stack, _memory,
            controller, _stateVectorBuilder,
            NullLogger<BasicMotorSkillsTask>.Instance,
            maxCyclesPerEpisode: 5);

        // Act
        var result = task.RunRegisterActivationTask();

        // Assert
        Assert.False(result.Success);
        Assert.Equal(0.0, result.Reward);
        Assert.Equal(5, result.CycleCount); // Should run full episode
    }

    #endregion

    #region StackGrowth Tests

    [Fact]
    public void StackGrowthTask_WithPushController_Succeeds()
    {
        // Arrange: Controller that pushes R0
        var controller = new DeterministicController(
            action: new PushCommand(RegisterId.R0));

        var task = new BasicMotorSkillsTask(
            _eventBus, _clock, _registerFile, _stack, _memory,
            controller, _stateVectorBuilder,
            NullLogger<BasicMotorSkillsTask>.Instance,
            maxCyclesPerEpisode: 10);

        // Act
        var result = task.RunStackGrowthTask();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1.0, result.Reward);
        Assert.Equal("StackGrowth", result.TaskName);
    }

    [Fact]
    public void StackGrowthTask_WithPushImmediateController_Succeeds()
    {
        // Arrange: Controller that pushes immediate value
        var controller = new DeterministicController(
            action: new PushImmediateCommand(123));

        var task = new BasicMotorSkillsTask(
            _eventBus, _clock, _registerFile, _stack, _memory,
            controller, _stateVectorBuilder,
            NullLogger<BasicMotorSkillsTask>.Instance,
            maxCyclesPerEpisode: 10);

        // Act
        var result = task.RunStackGrowthTask();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1.0, result.Reward);
    }

    #endregion

    #region ALUActivation Tests

    [Fact]
    public void AluActivationTask_WithAluCommand_Succeeds()
    {
        // Arrange: Controller that performs ALU Add (sets flags)
        // First write values to R0 and R1
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R0, 10));
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R1, 20));
        _clock.Step();

        var controller = new DeterministicController(
            action: new AluCommand(AluOp.Add, RegisterId.R0, RegisterId.R1, RegisterId.R2));

        var task = new BasicMotorSkillsTask(
            _eventBus, _clock, _registerFile, _stack, _memory,
            controller, _stateVectorBuilder,
            NullLogger<BasicMotorSkillsTask>.Instance,
            maxCyclesPerEpisode: 10);

        // Act
        var result = task.RunAluActivationTask();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1.0, result.Reward);
        Assert.Equal("ALUActivation", result.TaskName);
    }

    #endregion

    #region InputRead Tests

    [Fact]
    public void InputReadTask_WithInputReadController_Succeeds()
    {
        // Arrange: Controller that reads from device 0
        var controller = new DeterministicController(
            action: new InputReadRequest(DeviceId: 0, Timestamp: DateTimeOffset.Now));

        var task = new BasicMotorSkillsTask(
            _eventBus, _clock, _registerFile, _stack, _memory,
            controller, _stateVectorBuilder,
            NullLogger<BasicMotorSkillsTask>.Instance,
            maxCyclesPerEpisode: 10,
            inputDevice: _inputDevice);

        // Act
        var result = task.RunInputReadTask();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1.0, result.Reward);
        Assert.Equal("InputRead", result.TaskName);
    }

    [Fact]
    public void InputReadTask_WithoutInputDevice_ThrowsException()
    {
        // Arrange: No input device provided
        var controller = new DeterministicController(action: null);

        var task = new BasicMotorSkillsTask(
            _eventBus, _clock, _registerFile, _stack, _memory,
            controller, _stateVectorBuilder,
            NullLogger<BasicMotorSkillsTask>.Instance,
            maxCyclesPerEpisode: 10,
            inputDevice: null); // No device

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => task.RunInputReadTask());
    }

    #endregion

    #region OutputWrite Tests

    [Fact]
    public void OutputWriteTask_WithOutputWriteController_Succeeds()
    {
        // Arrange: Controller that writes to device 0
        var controller = new DeterministicController(
            action: new OutputWriteCommand(DeviceId: 0, Value: 42, Timestamp: DateTimeOffset.Now));

        var task = new BasicMotorSkillsTask(
            _eventBus, _clock, _registerFile, _stack, _memory,
            controller, _stateVectorBuilder,
            NullLogger<BasicMotorSkillsTask>.Instance,
            maxCyclesPerEpisode: 10,
            outputDevice: _outputDevice);

        // Act
        var result = task.RunOutputWriteTask();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1.0, result.Reward);
        Assert.Equal("OutputWrite", result.TaskName);
    }

    [Fact]
    public void OutputWriteTask_WithoutOutputDevice_ThrowsException()
    {
        // Arrange: No output device provided
        var controller = new DeterministicController(action: null);

        var task = new BasicMotorSkillsTask(
            _eventBus, _clock, _registerFile, _stack, _memory,
            controller, _stateVectorBuilder,
            NullLogger<BasicMotorSkillsTask>.Instance,
            maxCyclesPerEpisode: 10,
            outputDevice: null); // No device

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => task.RunOutputWriteTask());
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        _deviceRegistry.Dispose();
        _registerFile.Dispose();
        _alu.Dispose();
        _stack.Dispose();
        _memory.Dispose();
        _clock.Dispose();
    }

    #endregion

    #region Helper: Deterministic Controller

    /// <summary>
    /// Test controller that always returns the same action.
    /// </summary>
    private sealed class DeterministicController : INetworkController
    {
        private readonly object? _action;

        public DeterministicController(object? action)
        {
            _action = action;
        }

        public object DecideNextAction(CpuStateVector state) => _action!;

        public void Reset() { }

        public void OnEpisodeEnd(bool success, double reward) { }
    }

    #endregion
}
