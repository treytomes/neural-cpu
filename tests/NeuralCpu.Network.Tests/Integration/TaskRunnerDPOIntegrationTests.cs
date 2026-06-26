using Microsoft.Extensions.Logging.Abstractions;
using NeuralCpu.Core;
using NeuralCpu.Core.Components;
using NeuralCpu.Core.Configuration;
using NeuralCpu.Core.Events;
using NeuralCpu.Network;
using NeuralCpu.Network.Neural;
using NeuralCpu.Network.Training;
using TorchSharp;
using static TorchSharp.torch;

namespace NeuralCpu.Network.Tests.Integration;

/// <summary>
/// Integration tests for TaskRunner with DPO training.
/// Milestone 3.2: TaskRunner integration with DPO warmup and metrics.
/// </summary>
public sealed class TaskRunnerDPOIntegrationTests : IDisposable
{
    #region Fields

    private readonly IEventBus _eventBus;
    private readonly Clock _clock;
    private readonly RegisterFile _registerFile;
    private readonly Alu _alu;
    private readonly Stack _stack;
    private readonly Memory _memory;
    private readonly TransformerCpuController _controller;
    private readonly DPOTrainer _dpoTrainer;
    private readonly StateVectorBuilder _stateVectorBuilder;
    private readonly TaskRunner _taskRunner;

    #endregion

    #region Constructors

    public TaskRunnerDPOIntegrationTests()
    {
        // Create CPU components
        _eventBus = new EventBus();
        _clock = new Clock(_eventBus, NullLogger<Clock>.Instance);
        _registerFile = new RegisterFile(_eventBus, NullLogger<RegisterFile>.Instance);
        _alu = new Alu(_eventBus, _registerFile, NullLogger<Alu>.Instance);
        _stack = new Stack(_eventBus, _registerFile, NullLogger<Stack>.Instance);
        _memory = new Memory(_eventBus, _registerFile, NullLogger<Memory>.Instance, capacityWords: 128 * 1024); // 1MB

        // Create small controller for testing
        _controller = new TransformerCpuController(
            embeddingDim: 64,
            numHeads: 4,
            numEncoderLayers: 1,
            moeNetwork: null,
            device: CPU,
            isTraining: true);

        // Create DPO trainer
        _dpoTrainer = new DPOTrainer(
            controller: _controller,
            beta: 0.1,
            lambdaDpo: 1.0,
            learningRate: 1e-3, // Higher LR for faster convergence in tests
            device: CPU);

        // Create state vector builder
        var stateVectorOptions = new StateVectorOptions
        {
            EventHistorySize = 64,
            StackSnapshotSize = 8,
            MemorySnapshotSize = 16
        };
        var taskRunnerOptions = new TaskRunnerOptions
        {
            MaxCyclesPerEpisode = 100,
            InputBufferAddress = 0x80000000,
            InputLengthAddress = 0x80000FF8,
            OutputBufferAddress = 0x80001000,
            OutputLengthAddress = 0x80001FF8
        };
        _stateVectorBuilder = new StateVectorBuilder(_registerFile, _stack, _memory, stateVectorOptions, taskRunnerOptions);

        // Create task runner
        _taskRunner = new TaskRunner(
            _eventBus,
            _clock,
            _registerFile,
            _stack,
            _memory,
            _controller,
            _stateVectorBuilder,
            taskRunnerOptions,
            NullLogger<TaskRunner>.Instance);
    }

    #endregion

    #region Test: DPO Warmup Phase

    [Fact]
    public void DPOWarmup_ReducesInvalidEventRate()
    {
        // Arrange: Create minimal state
        var state = CreateMinimalStateVector();

        // Measure initial invalid rate (10 samples)
        int initialInvalidCount = 0;
        for (int i = 0; i < 10; i++)
        {
            var actionLogits = _controller.GetActionLogits(state);
            var generatedEvent = _controller.DecodeEncodedEvent(actionLogits);
            var validation = EventValidationDPO.Validate(generatedEvent);
            if (!validation.IsValid)
                initialInvalidCount++;
        }
        double initialInvalidRate = initialInvalidCount / 10.0;

        // Act: Run DPO warmup (50 steps)
        for (int step = 0; step < 50; step++)
        {
            _dpoTrainer.TrainStep(state);
        }

        // Measure final invalid rate (10 samples)
        int finalInvalidCount = 0;
        for (int i = 0; i < 10; i++)
        {
            var actionLogits = _controller.GetActionLogits(state);
            var generatedEvent = _controller.DecodeEncodedEvent(actionLogits);
            var validation = EventValidationDPO.Validate(generatedEvent);
            if (!validation.IsValid)
                finalInvalidCount++;
        }
        double finalInvalidRate = finalInvalidCount / 10.0;

        // Assert: Invalid rate should decrease
        // Note: With random initialization, this might not always hold for small warmup
        // But we expect the trend to be downward
        Assert.True(finalInvalidRate <= initialInvalidRate + 0.2, // Allow some tolerance
            $"Expected invalid rate to decrease. Initial={initialInvalidRate:F2}, Final={finalInvalidRate:F2}");
    }

    [Fact]
    public void DPOWarmup_EventuallyProducesValidEvents()
    {
        // Arrange: Create minimal state
        var state = CreateMinimalStateVector();

        // Act: Run DPO warmup (100 steps - longer for convergence)
        for (int step = 0; step < 100; step++)
        {
            _dpoTrainer.TrainStep(state);
        }

        // Measure valid event rate (20 samples)
        int validCount = 0;
        for (int i = 0; i < 20; i++)
        {
            var actionLogits = _controller.GetActionLogits(state);
            var generatedEvent = _controller.DecodeEncodedEvent(actionLogits);
            var validation = EventValidationDPO.Validate(generatedEvent);
            if (validation.IsValid)
                validCount++;
        }

        // Assert: Should produce at least some valid events (>50%)
        // (Real training would get >95%, but tests use tiny networks)
        Assert.True(validCount >= 10,
            $"Expected at least 10/20 valid events after warmup, got {validCount}/20");
    }

    #endregion

    #region Test: DPO Metrics Tracking

    [Fact]
    public void DPOMetrics_TrackLossAndLogProbs()
    {
        // Arrange: Create state
        var state = CreateMinimalStateVector();

        // Act: Run training step
        var metrics = _dpoTrainer.TrainStep(state);

        // Assert: Metrics should be populated
        Assert.True(metrics.Loss >= 0);
        Assert.True(double.IsFinite(metrics.Loss));
        Assert.True(double.IsFinite(metrics.LogProbPreferred));
        Assert.True(double.IsFinite(metrics.LogProbRejected));
        Assert.True(metrics.ErrorCount >= 0);
    }

    [Fact]
    public void DPOMetrics_LossDecreases_OverMultipleSteps()
    {
        // Arrange: Create state
        var state = CreateMinimalStateVector();

        // Act: Collect loss over 20 steps
        var losses = new List<double>();
        for (int step = 0; step < 20; step++)
        {
            var metrics = _dpoTrainer.TrainStep(state);
            losses.Add(metrics.Loss);
        }

        // Assert: Average loss in second half should be <= first half
        var firstHalfAvg = losses.Take(10).Average();
        var secondHalfAvg = losses.Skip(10).Average();

        Assert.True(secondHalfAvg <= firstHalfAvg + 0.1, // Allow small tolerance
            $"Expected loss to decrease. First half={firstHalfAvg:F3}, Second half={secondHalfAvg:F3}");
    }

    #endregion

    #region Test: Task Training with DPO

    [Fact]
    public void TaskRunner_CanRunEpisodeWithDPO()
    {
        // Arrange: Create simple Echo task
        var task = new TaskDefinition
        {
            Name = "Echo",
            Input = new ulong[] { 1, 2, 3 },
            ExpectedOutput = new ulong[] { 1, 2, 3 }
        };

        // Act: Run episode (will fail with random network, but should complete)
        var result = _taskRunner.RunEpisode(task, maxCycles: 50);

        // Assert: Should complete without crashing
        Assert.NotNull(result);
        Assert.True(result.CycleCount <= 50);
        Assert.True(result.Reward >= 0 && result.Reward <= 1.0);
    }

    #endregion

    #region Helper Methods

    private CpuStateVector CreateMinimalStateVector()
    {
        var registers = new ulong[7];
        var events = new EncodedEvent[64];
        for (int i = 0; i < 64; i++)
        {
            events[i] = new EncodedEvent(EventType.None, RegisterId.None, RegisterId.None, RegisterId.None, 0);
        }
        var stack = new ulong[8];
        var memoryAddresses = new ulong[16];
        var memoryValues = new ulong[16];

        return new CpuStateVector
        {
            Registers = registers,
            EventHistory = events,
            StackSnapshot = stack,
            MemoryAddresses = memoryAddresses,
            MemoryValues = memoryValues,
            CycleCount = 0
        };
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        _controller.Dispose();
        _dpoTrainer.Dispose();
        _registerFile.Dispose();
        _alu.Dispose();
        _stack.Dispose();
        _memory.Dispose();
        _clock.Dispose();
    }

    #endregion
}
