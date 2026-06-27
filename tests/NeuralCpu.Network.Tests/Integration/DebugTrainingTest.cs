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
using Xunit.Abstractions;

namespace NeuralCpu.Network.Tests.Integration;

/// <summary>
/// Debug test to trace training progress in detail.
/// </summary>
public sealed class DebugTrainingTest : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly IEventBus _eventBus;
    private readonly Clock _clock;
    private readonly RegisterFile _registerFile;
    private readonly Alu _alu;
    private readonly Stack _stack;
    private readonly Memory _memory;
    private readonly TransformerCpuController _controller;
    private readonly StateVectorBuilder _stateVectorBuilder;
    private readonly TaskRunner _taskRunner;
    private readonly TrainingOrchestrator _orchestrator;

    public DebugTrainingTest(ITestOutputHelper output)
    {
        _output = output;

        // Create CPU components
        _eventBus = new EventBus();
        _clock = new Clock(_eventBus, NullLogger<Clock>.Instance);
        _registerFile = new RegisterFile(_eventBus, NullLogger<RegisterFile>.Instance);
        _alu = new Alu(_eventBus, _registerFile, NullLogger<Alu>.Instance);
        _stack = new Stack(_eventBus, _registerFile, NullLogger<Stack>.Instance);
        _memory = new Memory(_eventBus, _registerFile, NullLogger<Memory>.Instance, capacityWords: 128 * 1024);

        // Create controller (small for fast testing)
        _controller = new TransformerCpuController(
            embeddingDim: 64,
            numHeads: 4,
            numEncoderLayers: 1,
            moeNetwork: null,
            device: CPU,
            isTraining: true);

        // Create DPO trainer
        var dpoTrainer = new DPOTrainer(
            controller: _controller,
            beta: 0.1,
            lambdaDpo: 1.0,
            learningRate: 1e-3,
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
            MaxCyclesPerEpisode = 200,
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

        // Create training orchestrator
        _orchestrator = new TrainingOrchestrator(
            _controller,
            dpoTrainer,
            _taskRunner,
            NullLogger<TrainingOrchestrator>.Instance);
    }

    [Fact]
    public void DebugEchoTaskTraining()
    {
        // Simplest possible task: Echo single value
        var task = new TaskDefinition
        {
            Name = "Echo",
            Input = new ulong[] { 42 },
            ExpectedOutput = new ulong[] { 42 }
        };

        _output.WriteLine("=== BASELINE ===");
        var baselineResult = _taskRunner.RunEpisode(task, maxCycles: 50);
        _output.WriteLine($"Baseline Reward: {baselineResult.Reward:F4}");
        _output.WriteLine($"Baseline Cycles: {baselineResult.CycleCount}");
        _output.WriteLine($"Baseline Output Length: {baselineResult.Output.Length}");
        if (baselineResult.Output.Length > 0)
        {
            _output.WriteLine($"Baseline Output[0]: {baselineResult.Output[0]} (expected 42)");
        }

        _output.WriteLine("\n=== DPO WARMUP ===");
        var dpoMetrics = _orchestrator.RunDPOWarmup(steps: 20);
        _output.WriteLine($"DPO Loss (first): {dpoMetrics[0].Loss:F4}");
        _output.WriteLine($"DPO Loss (last): {dpoMetrics[^1].Loss:F4}");

        _output.WriteLine("\n=== TASK TRAINING ===");
        var trainingMetrics = _orchestrator.TrainOnTask(task, episodes: 10, maxCyclesPerEpisode: 50);
        for (int i = 0; i < trainingMetrics.Length; i++)
        {
            _output.WriteLine($"Episode {i}: Reward={trainingMetrics[i].Reward:F4}, PolicyLoss={trainingMetrics[i].PolicyLoss:F4}, Cycles={trainingMetrics[i].CycleCount}");
        }

        _output.WriteLine("\n=== FINAL ===");
        var finalResult = _taskRunner.RunEpisode(task, maxCycles: 50);
        _output.WriteLine($"Final Reward: {finalResult.Reward:F4}");
        _output.WriteLine($"Final Cycles: {finalResult.CycleCount}");
        _output.WriteLine($"Final Output Length: {finalResult.Output.Length}");
        if (finalResult.Output.Length > 0)
        {
            _output.WriteLine($"Final Output[0]: {finalResult.Output[0]} (expected 42)");
        }

        // No assertions - just observe the output
    }

    public void Dispose()
    {
        _controller.Dispose();
        _clock.Dispose();
        _registerFile.Dispose();
        _alu.Dispose();
        _stack.Dispose();
        _memory.Dispose();
    }
}
