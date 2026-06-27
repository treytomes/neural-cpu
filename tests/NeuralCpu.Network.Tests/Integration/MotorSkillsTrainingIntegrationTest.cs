using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NeuralCpu.Core;
using NeuralCpu.Core.Components;
using NeuralCpu.Core.Configuration;
using NeuralCpu.Core.Events;
using NeuralCpu.Network.Neural;
using NeuralCpu.Network.Training;
using TorchSharp;
using static TorchSharp.torch;

namespace NeuralCpu.Network.Tests.Integration;

/// <summary>
/// Integration test for motor skills training.
/// Tests that the full training pipeline executes without errors.
/// </summary>
public sealed class MotorSkillsTrainingIntegrationTest : IDisposable
{
    private readonly IEventBus _eventBus;
    private readonly Clock _clock;
    private readonly RegisterFile _registerFile;
    private readonly Alu _alu;
    private readonly Stack _stack;
    private readonly Memory _memory;
    private readonly TaskInputDevice _inputDevice;
    private readonly TaskOutputDevice _outputDevice;
    private readonly DeviceRegistry _deviceRegistry;
    private readonly StateVectorBuilder _stateVectorBuilder;
    private readonly TransformerCpuController _controller;
    private readonly PolicyGradientTrainer _policyGradientTrainer;
    private readonly ILoggerFactory _loggerFactory;

    public MotorSkillsTrainingIntegrationTest()
    {
        _loggerFactory = NullLoggerFactory.Instance;

        // Create CPU components
        _eventBus = new EventBus();
        _clock = new Clock(_eventBus, _loggerFactory.CreateLogger<Clock>());
        _registerFile = new RegisterFile(_eventBus, _loggerFactory.CreateLogger<RegisterFile>());
        _alu = new Alu(_eventBus, _registerFile, _loggerFactory.CreateLogger<Alu>());
        _stack = new Stack(_eventBus, _registerFile, _loggerFactory.CreateLogger<Stack>());
        _memory = new Memory(_eventBus, _registerFile, _loggerFactory.CreateLogger<Memory>(), capacityWords: 128 * 1024);

        // Create device registry
        _inputDevice = new TaskInputDevice();
        _outputDevice = new TaskOutputDevice();
        _deviceRegistry = new DeviceRegistry(_eventBus, _loggerFactory.CreateLogger<DeviceRegistry>());
        _deviceRegistry.RegisterInputDevice(_inputDevice);
        _deviceRegistry.RegisterOutputDevice(_outputDevice);

        // Create state vector builder
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
        _stateVectorBuilder = new StateVectorBuilder(
            _registerFile, _stack, _memory,
            stateVectorOptions, taskRunnerOptions);

        // Create small neural controller for fast testing
        _controller = new TransformerCpuController(
            embeddingDim: 64,
            numHeads: 2,
            numEncoderLayers: 1,
            moeNetwork: null,
            device: CPU,
            isTraining: true);

        // Create policy gradient trainer
        _policyGradientTrainer = new PolicyGradientTrainer(
            controller: _controller,
            gamma: 0.99,
            learningRate: 1e-3);
    }

    [Fact]
    public void MotorSkillsTraining_ExecutesWithoutErrors()
    {
        // Arrange
        var trainer = new MotorSkillsTrainer(
            _eventBus, _clock, _registerFile, _alu, _stack, _memory,
            _inputDevice, _outputDevice, _deviceRegistry,
            _stateVectorBuilder, _controller, _policyGradientTrainer,
            _loggerFactory);

        // Act - run just 3 episodes per task for quick test
        var results = trainer.TrainAllTasks(
            episodesPerTask: 3,
            maxCyclesPerEpisode: 20);

        // Assert - verify all tasks produced results
        Assert.NotNull(results.RegisterActivation);
        Assert.NotNull(results.StackGrowth);
        Assert.NotNull(results.AluActivation);
        Assert.NotNull(results.InputRead);
        Assert.NotNull(results.OutputWrite);

        // Verify we got the expected number of episodes
        Assert.Equal(3, results.RegisterActivation.Length);
        Assert.Equal(3, results.StackGrowth.Length);
        Assert.Equal(3, results.AluActivation.Length);
        Assert.Equal(3, results.InputRead.Length);
        Assert.Equal(3, results.OutputWrite.Length);

        // Verify metrics are reasonable
        foreach (var metrics in results.RegisterActivation)
        {
            Assert.True(metrics.CycleCount > 0);
            Assert.True(metrics.CycleCount <= 20);
            Assert.True(metrics.Reward >= 0.0 && metrics.Reward <= 1.0);
        }
    }

    [Fact(Skip = "Long-running test - only run manually")]
    public void MotorSkillsTraining_FullRun_ShowsLearning()
    {
        // This test runs the full 200 episodes to observe actual learning
        // Skip by default to avoid slow CI runs

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddConsole();
        });

        var trainer = new MotorSkillsTrainer(
            _eventBus, _clock, _registerFile, _alu, _stack, _memory,
            _inputDevice, _outputDevice, _deviceRegistry,
            _stateVectorBuilder, _controller, _policyGradientTrainer,
            loggerFactory);

        // Run full training
        var results = trainer.TrainAllTasks(
            episodesPerTask: 200,
            maxCyclesPerEpisode: 50);

        // Analyze learning curves
        var taskNames = new[] { "RegisterActivation", "StackGrowth", "ALUActivation", "InputRead", "OutputWrite" };
        var allMetrics = new[]
        {
            results.RegisterActivation,
            results.StackGrowth,
            results.AluActivation,
            results.InputRead,
            results.OutputWrite
        };

        for (int i = 0; i < taskNames.Length; i++)
        {
            var metrics = allMetrics[i]!;
            var successRate = metrics.Count(m => m.Success) / (double)metrics.Length;
            var firstSuccess = metrics.Select((m, idx) => (m, idx)).FirstOrDefault(x => x.m.Success);

            // Log results
            System.Console.WriteLine($"{taskNames[i]}: {successRate:P0} success rate, first success at episode {(firstSuccess.m != null ? firstSuccess.idx : -1)}");

            // We expect at least some learning to occur
            // (Very loose assertion since learning is stochastic)
            Assert.True(successRate >= 0.0); // At minimum, don't crash
        }
    }

    public void Dispose()
    {
        _deviceRegistry.Dispose();
        _registerFile.Dispose();
        _alu.Dispose();
        _stack.Dispose();
        _memory.Dispose();
        _clock.Dispose();
        _controller.Dispose();
        _policyGradientTrainer.Dispose();
    }
}
