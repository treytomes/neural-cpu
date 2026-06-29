using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NeuralCpu.Core;
using NeuralCpu.Core.Components;
using NeuralCpu.Core.Configuration;
using NeuralCpu.Core.Events;
using NeuralCpu.Network;
using NeuralCpu.Network.Neural;
using NeuralCpu.Network.Training;
using TorchSharp;

namespace NeuralCpu.Console;

/// <summary>
/// Runs hybrid training: Behavior Cloning (Phase 1) + REINFORCE Fine-tuning (Phase 2).
/// </summary>
public sealed class HybridTrainingRunner
{
    private readonly IHost _host;
    private readonly ILogger<HybridTrainingRunner> _logger;

    public HybridTrainingRunner()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices(services =>
            {
                // Core CPU services
                services.AddNeuralCpu(new NeuralCpuOptions
                {
                    Clock = new ClockOptions { InitialFrequencyHz = 1000 },
                    Memory = new MemoryOptions { CapacityWords = 1024 * 1024 },
                    Stack = new StackOptions { MaxSize = 65536 }
                });

                // I/O Devices
                services.AddSingleton<TaskInputDevice>();
                services.AddSingleton<TaskOutputDevice>();
                services.AddSingleton<DeviceRegistry>();

                // Network components
                services.AddSingleton<StateVectorBuilder>(sp =>
                {
                    var registerFile = sp.GetRequiredService<RegisterFile>();
                    var stack = sp.GetRequiredService<Stack>();
                    var memory = sp.GetRequiredService<Memory>();

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

                    return new StateVectorBuilder(registerFile, stack, memory,
                        stateVectorOptions, taskRunnerOptions);
                });

                services.AddSingleton<TransformerCpuController>(sp =>
                {
                    return new TransformerCpuController(
                        embeddingDim: 128,
                        numHeads: 4,
                        numEncoderLayers: 2,
                        moeNetwork: null,
                        device: torch.CPU,
                        isTraining: true);
                });

                services.AddSingleton<PolicyGradientTrainer>(sp =>
                {
                    var controller = sp.GetRequiredService<TransformerCpuController>();
                    return new PolicyGradientTrainer(controller, learningRate: 1e-4, gamma: 0.99);
                });

                // Mock network for demonstrations
                services.AddSingleton<CycleMockNetwork>(sp =>
                {
                    var logger = sp.GetRequiredService<ILogger<CycleMockNetwork>>();
                    var options = new TaskRunnerOptions
                    {
                        InputBufferAddress = 0x80000000,
                        OutputBufferAddress = 0x80001000
                    };
                    return new CycleMockNetwork(logger, options);
                });
            })
            .Build();

        _logger = _host.Services.GetRequiredService<ILogger<HybridTrainingRunner>>();
    }

    public async Task RunHybridTrainingAsync()
    {
        _logger.LogInformation("============================================");
        _logger.LogInformation("Neural CPU - Hybrid Training (BC + REINFORCE)");
        _logger.LogInformation("============================================");
        _logger.LogInformation("");

        var overallStart = DateTimeOffset.UtcNow;

        // Get services
        var eventBus = _host.Services.GetRequiredService<IEventBus>();
        var clock = _host.Services.GetRequiredService<Clock>();
        var registerFile = _host.Services.GetRequiredService<RegisterFile>();
        var alu = _host.Services.GetRequiredService<Alu>();
        var stack = _host.Services.GetRequiredService<Stack>();
        var memory = _host.Services.GetRequiredService<Memory>();
        var inputDevice = _host.Services.GetRequiredService<TaskInputDevice>();
        var outputDevice = _host.Services.GetRequiredService<TaskOutputDevice>();
        var deviceRegistry = _host.Services.GetRequiredService<DeviceRegistry>();
        var stateVectorBuilder = _host.Services.GetRequiredService<StateVectorBuilder>();
        var controller = _host.Services.GetRequiredService<TransformerCpuController>();
        var policyGradientTrainer = _host.Services.GetRequiredService<PolicyGradientTrainer>();
        var mockNetwork = _host.Services.GetRequiredService<CycleMockNetwork>();
        var loggerFactory = _host.Services.GetRequiredService<ILoggerFactory>();

        // ===================================================================
        // PHASE 1: Behavior Cloning (Imitation Learning)
        // ===================================================================

        _logger.LogInformation("=== PHASE 1: Behavior Cloning ===");
        _logger.LogInformation("Collecting demonstrations from expert (CycleMockNetwork)...");
        _logger.LogInformation("");

        var collectorLogger = loggerFactory.CreateLogger<DemonstrationCollectorSimple>();
        var collector = new DemonstrationCollectorSimple(collectorLogger);

        // Check if demonstrations already exist
        const string DEMO_DIR = "demonstrations";
        Dictionary<string, List<Demonstration>> allDemonstrations;

        _logger.LogInformation("Collecting new demonstrations (100 per task, 5 tasks)...");
        allDemonstrations = collector.CollectAllTasks(
            eventBus,
            clock,
            registerFile,
            stack,
            memory,
            mockNetwork,
            controller,
            stateVectorBuilder,
            inputDevice,
            outputDevice,
            deviceRegistry,
            loggerFactory,
            episodesPerTask: 100);

        // Save for reuse (metadata only, tensors not serialized)
        collector.SaveToDisk(allDemonstrations, DEMO_DIR);

        var totalDemos = allDemonstrations.Values.Sum(demos => demos.Count);
        _logger.LogInformation("Total demonstrations collected: {Count}", totalDemos);
        _logger.LogInformation("");

        // Train with behavior cloning
        _logger.LogInformation("Training with behavior cloning (100 epochs)...");
        var bcLogger = loggerFactory.CreateLogger<BehaviorCloningTrainer>();
        var bcTrainer = new BehaviorCloningTrainer(controller, learningRate: 1e-3, logger: bcLogger);

        var bcMetrics = bcTrainer.TrainOnAllTasks(
            allDemonstrations,
            epochs: 100,
            batchSize: 32);

        var bcElapsed = DateTimeOffset.UtcNow - overallStart;
        _logger.LogInformation("");
        _logger.LogInformation("Phase 1 complete in {Elapsed:F1} minutes", bcElapsed.TotalMinutes);
        _logger.LogInformation("Final validation accuracy: {Acc:F2}%",
            bcMetrics["Combined"].FinalValidationAccuracy * 100);
        _logger.LogInformation("");

        // ===================================================================
        // VALIDATION: Test on actual tasks
        // ===================================================================

        _logger.LogInformation("=== Validation: Testing on actual tasks ===");
        var validationResults = await ValidateOnTasksAsync(
            eventBus, clock, registerFile, alu, stack, memory,
            inputDevice, outputDevice, deviceRegistry,
            stateVectorBuilder, controller, loggerFactory,
            numEpisodes: 20);

        _logger.LogInformation("");
        _logger.LogInformation("Validation Results (20 episodes each):");
        foreach (var (taskName, successRate) in validationResults)
        {
            _logger.LogInformation("  {Task}: {Rate:F1}% success", taskName, successRate * 100);
        }
        _logger.LogInformation("");

        // Check if we should proceed to Phase 2
        var avgSuccessRate = validationResults.Values.Average();
        if (avgSuccessRate < 0.80)
        {
            _logger.LogWarning("Average success rate ({Rate:F1}%) below threshold (80%)", avgSuccessRate * 100);
            _logger.LogWarning("Consider collecting more demonstrations or training longer");
            _logger.LogInformation("");
        }
        else
        {
            _logger.LogInformation("Average success rate: {Rate:F1}% - Ready for Phase 2!", avgSuccessRate * 100);
            _logger.LogInformation("");
        }

        // ===================================================================
        // PHASE 2: REINFORCE Fine-tuning (Optional)
        // ===================================================================

        if (avgSuccessRate >= 0.80)
        {
            _logger.LogInformation("=== PHASE 2: REINFORCE Fine-tuning ===");
            _logger.LogInformation("Fine-tuning with policy gradient (200 episodes)...");
            _logger.LogInformation("");

            var reinforceStart = DateTimeOffset.UtcNow;

            using var trainer = new MotorSkillsTrainerWithCheckpoints(
                eventBus, clock, registerFile, alu, stack, memory,
                inputDevice, outputDevice, deviceRegistry,
                stateVectorBuilder, controller, policyGradientTrainer,
                loggerFactory,
                metricsDirectory: "training_metrics_phase2",
                checkpointDirectory: "checkpoints_phase2",
                checkpointInterval: 50,
                runId: "phase2_" + DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss"));

            var reinforceResults = trainer.TrainAllTasks(
                episodesPerTask: 200,
                maxCyclesPerEpisode: 50);

            var reinforceElapsed = DateTimeOffset.UtcNow - reinforceStart;
            _logger.LogInformation("");
            _logger.LogInformation("Phase 2 complete in {Elapsed:F1} minutes", reinforceElapsed.TotalMinutes);
            _logger.LogInformation("");
        }

        // ===================================================================
        // Summary
        // ===================================================================

        var totalElapsed = DateTimeOffset.UtcNow - overallStart;
        _logger.LogInformation("============================================");
        _logger.LogInformation("Hybrid Training Complete!");
        _logger.LogInformation("============================================");
        _logger.LogInformation("Total time: {Elapsed:F1} minutes", totalElapsed.TotalMinutes);
        _logger.LogInformation("Phase 1 (BC): {BC:F1} min", bcElapsed.TotalMinutes);
        if (avgSuccessRate >= 0.80)
        {
            _logger.LogInformation("Phase 2 (REINFORCE): {RL:F1} min", (totalElapsed - bcElapsed).TotalMinutes);
        }
        _logger.LogInformation("");
        _logger.LogInformation("Results:");
        _logger.LogInformation("  Demonstrations collected: {Count}", totalDemos);
        _logger.LogInformation("  BC validation accuracy: {Acc:F2}%",
            bcMetrics["Combined"].FinalValidationAccuracy * 100);
        _logger.LogInformation("  Task success rate: {Rate:F1}%", avgSuccessRate * 100);
        _logger.LogInformation("");
    }

    private async Task<Dictionary<string, double>> ValidateOnTasksAsync(
        IEventBus eventBus,
        Clock clock,
        RegisterFile registerFile,
        Alu alu,
        Stack stack,
        Memory memory,
        TaskInputDevice inputDevice,
        TaskOutputDevice outputDevice,
        DeviceRegistry deviceRegistry,
        StateVectorBuilder stateVectorBuilder,
        TransformerCpuController controller,
        ILoggerFactory loggerFactory,
        int numEpisodes = 20)
    {
        var results = new Dictionary<string, double>();
        var taskLogger = loggerFactory.CreateLogger<BasicMotorSkillsTask>();

        var task = new BasicMotorSkillsTask(
            eventBus, clock, registerFile, stack, memory,
            controller, stateVectorBuilder,
            taskLogger,
            maxCyclesPerEpisode: 50,
            inputDevice: inputDevice,
            outputDevice: outputDevice);

        var taskNames = new[] { "RegisterActivation", "StackGrowth", "ALUActivation", "InputRead", "OutputWrite" };

        foreach (var taskName in taskNames)
        {
            int successCount = 0;

            for (int ep = 0; ep < numEpisodes; ep++)
            {
                NeuralCpu.Network.Training.TaskResult result = taskName switch
                {
                    "RegisterActivation" => task.RunRegisterActivationTask(),
                    "StackGrowth" => task.RunStackGrowthTask(),
                    "ALUActivation" => task.RunAluActivationTask(),
                    "InputRead" => task.RunInputReadTask(),
                    "OutputWrite" => task.RunOutputWriteTask(),
                    _ => throw new ArgumentException($"Unknown task: {taskName}")
                };

                if (result.Success)
                    successCount++;

                controller.Reset();
            }

            results[taskName] = (double)successCount / numEpisodes;
        }

        return results;
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    public static async Task Main(string[] args)
    {
        var runner = new HybridTrainingRunner();

        try
        {
            await runner.RunHybridTrainingAsync();
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"Training failed: {ex.Message}");
            System.Console.WriteLine(ex.StackTrace);
            return;
        }
        finally
        {
            await runner.DisposeAsync();
        }

        System.Console.WriteLine("\nTraining complete!");
    }
}
