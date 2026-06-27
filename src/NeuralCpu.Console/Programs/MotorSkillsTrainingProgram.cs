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
/// Runs basic motor skills training (Milestone 5.5.3).
/// Trains network to control individual CPU components.
/// </summary>
public sealed class MotorSkillsTrainingProgram
{
    public static void Run(ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<MotorSkillsTrainingProgram>();
        logger.LogInformation("=== Motor Skills Training Program ===");

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
        logger.LogInformation("Initializing TransformerCpuController...");
        var controller = new TransformerCpuController(
            embeddingDim: 128,
            numHeads: 4,
            numEncoderLayers: 2,
            moeNetwork: null,
            device: TorchSharp.torch.CPU,
            isTraining: true);

        // Create policy gradient trainer
        var policyGradientTrainer = new PolicyGradientTrainer(
            controller: controller,
            gamma: 0.99,
            learningRate: 1e-4);

        // Create motor skills trainer
        var trainer = new MotorSkillsTrainer(
            eventBus, clock, registerFile, alu, stack, memory,
            inputDevice, outputDevice, deviceRegistry,
            stateVectorBuilder, controller, policyGradientTrainer,
            loggerFactory);

        // Run training
        logger.LogInformation("Starting training with 200 episodes per task...");
        var results = trainer.TrainAllTasks(
            episodesPerTask: 200,
            maxCyclesPerEpisode: 50);

        // Save trained model
        logger.LogInformation("Saving trained model...");
        var modelPath = "models/motor_skills_controller.pt";
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        controller.Save(modelPath);
        logger.LogInformation("Model saved to {Path}", modelPath);

        // Cleanup
        deviceRegistry.Dispose();
        registerFile.Dispose();
        alu.Dispose();
        stack.Dispose();
        memory.Dispose();
        clock.Dispose();
        controller.Dispose();
        policyGradientTrainer.Dispose();

        logger.LogInformation("Training complete!");
    }
}
