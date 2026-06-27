using Microsoft.Extensions.Logging;
using NeuralCpu.Core;
using NeuralCpu.Core.Components;
using NeuralCpu.Core.Configuration;
using NeuralCpu.Core.Events;
using NeuralCpu.Network;
using NeuralCpu.Network.Neural;
using NeuralCpu.Network.Training;

namespace NeuralCpu.Console.Programs;

/// <summary>
/// Quick test of motor skills training with just 10 episodes per task.
/// Validates integration without long training time.
/// </summary>
public sealed class QuickMotorSkillsTest
{
    public static void Run(ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<QuickMotorSkillsTest>();
        logger.LogInformation("=== Quick Motor Skills Test (10 episodes per task) ===");

        // Create CPU components
        var eventBus = new EventBus();
        var clock = new Clock(eventBus, loggerFactory.CreateLogger<Clock>());
        var registerFile = new RegisterFile(eventBus, loggerFactory.CreateLogger<RegisterFile>());
        var alu = new Alu(eventBus, registerFile, loggerFactory.CreateLogger<Alu>());
        var stack = new Stack(eventBus, registerFile, loggerFactory.CreateLogger<Stack>());
        var memory = new Memory(eventBus, registerFile, loggerFactory.CreateLogger<Memory>(), capacityWords: 128 * 1024);

        // Create device registry
        var inputDevice = new TaskInputDevice();
        var outputDevice = new TaskOutputDevice();
        var deviceRegistry = new DeviceRegistry(eventBus, loggerFactory.CreateLogger<DeviceRegistry>());
        deviceRegistry.RegisterInputDevice(inputDevice);
        deviceRegistry.RegisterOutputDevice(outputDevice);

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
        var stateVectorBuilder = new StateVectorBuilder(
            registerFile, stack, memory,
            stateVectorOptions, taskRunnerOptions);

        // Create neural controller
        logger.LogInformation("Initializing TransformerCpuController (small network for testing)...");
        var controller = new TransformerCpuController(
            embeddingDim: 64,      // Smaller for faster testing
            numHeads: 2,
            numEncoderLayers: 1,   // Just 1 layer for quick test
            moeNetwork: null,
            device: TorchSharp.torch.CPU,
            isTraining: true);

        // Create policy gradient trainer
        var policyGradientTrainer = new PolicyGradientTrainer(
            controller: controller,
            gamma: 0.99,
            learningRate: 1e-3);   // Higher LR for faster learning in test

        // Create motor skills trainer
        var trainer = new MotorSkillsTrainer(
            eventBus, clock, registerFile, alu, stack, memory,
            inputDevice, outputDevice, deviceRegistry,
            stateVectorBuilder, controller, policyGradientTrainer,
            loggerFactory);

        // Run quick test - just 10 episodes per task
        logger.LogInformation("Running 10 episodes per task (quick validation)...");
        var startTime = DateTimeOffset.Now;

        var results = trainer.TrainAllTasks(
            episodesPerTask: 10,
            maxCyclesPerEpisode: 50);

        var elapsed = DateTimeOffset.Now - startTime;
        logger.LogInformation("Quick test complete in {Elapsed:F1} seconds", elapsed.TotalSeconds);

        // Print results summary
        logger.LogInformation("=== Quick Test Results ===");
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
            if (allMetrics[i] == null) continue;

            var metrics = allMetrics[i]!;
            var successCount = metrics.Count(m => m.Success);
            var successRate = successCount / (double)metrics.Length;
            var avgCycles = metrics.Where(m => m.Success).Select(m => m.CycleCount).DefaultIfEmpty(0).Average();
            var firstSuccess = metrics.Select((m, idx) => (m, idx)).FirstOrDefault(x => x.m.Success);

            logger.LogInformation("{TaskName}: {SuccessCount}/{Total} success ({Rate:P0}), avg cycles: {AvgCycles:F1}, first success: episode {FirstSuccess}",
                taskNames[i], successCount, metrics.Length, successRate, avgCycles,
                firstSuccess.m != null ? firstSuccess.idx : -1);
        }

        // Cleanup
        deviceRegistry.Dispose();
        registerFile.Dispose();
        alu.Dispose();
        stack.Dispose();
        memory.Dispose();
        clock.Dispose();
        controller.Dispose();
        policyGradientTrainer.Dispose();

        logger.LogInformation("Test complete!");
        logger.LogInformation("For full training (200 episodes), use MotorSkillsTrainingProgram.Run()");
    }
}
