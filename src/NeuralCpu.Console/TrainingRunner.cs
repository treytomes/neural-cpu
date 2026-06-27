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
/// Runs Motor Skills training with full metrics and checkpointing.
/// </summary>
public sealed class TrainingRunner
{
    private readonly IHost _host;
    private readonly ILogger<TrainingRunner> _logger;

    public TrainingRunner()
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
                // Core CPU services with configuration
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
            })
            .Build();

        _logger = _host.Services.GetRequiredService<ILogger<TrainingRunner>>();
    }

    public async Task RunTrainingAsync(
        int episodesPerTask = 200,
        int maxCyclesPerEpisode = 50,
        int checkpointInterval = 50,
        bool resume = true)
    {
        _logger.LogInformation("===========================================");
        _logger.LogInformation("Neural CPU - Motor Skills Training Runner");
        _logger.LogInformation("===========================================");
        _logger.LogInformation("");
        _logger.LogInformation("Configuration:");
        _logger.LogInformation("  Episodes per task: {Episodes}", episodesPerTask);
        _logger.LogInformation("  Max cycles per episode: {MaxCycles}", maxCyclesPerEpisode);
        _logger.LogInformation("  Checkpoint interval: {Interval}", checkpointInterval);
        _logger.LogInformation("  Resume from checkpoint: {Resume}", resume);
        _logger.LogInformation("");

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
        var loggerFactory = _host.Services.GetRequiredService<ILoggerFactory>();

        // Create trainer with checkpointing
        using var trainer = new MotorSkillsTrainerWithCheckpoints(
            eventBus, clock, registerFile, alu, stack, memory,
            inputDevice, outputDevice, deviceRegistry,
            stateVectorBuilder, controller, policyGradientTrainer,
            loggerFactory,
            metricsDirectory: "training_metrics",
            checkpointDirectory: "checkpoints",
            checkpointInterval: checkpointInterval);

        // Check for existing checkpoint
        TrainingState? resumeState = null;
        if (resume)
        {
            var checkpoint = new TrainingCheckpoint("checkpoints");
            var latestPath = checkpoint.FindLatestCheckpoint();

            if (latestPath != null)
            {
                _logger.LogInformation("Found checkpoint at: {Path}", latestPath);
                _logger.LogInformation("Resuming training from checkpoint...");
                _logger.LogInformation("");

                var (resumedController, state) = checkpoint.Load(latestPath, policyGradientTrainer);
                resumeState = state;

                _logger.LogInformation("Resumed from episode {Episode} of task {Task}",
                    state.CurrentTaskEpisode, state.CurrentTask);
                _logger.LogInformation("");
            }
            else
            {
                _logger.LogInformation("No checkpoint found, starting fresh training");
                _logger.LogInformation("");
            }
        }

        // Run training
        var startTime = DateTimeOffset.UtcNow;

        var results = trainer.TrainAllTasks(
            episodesPerTask: episodesPerTask,
            maxCyclesPerEpisode: maxCyclesPerEpisode,
            resumeState: resumeState);

        var elapsed = DateTimeOffset.UtcNow - startTime;

        // Print final summary
        _logger.LogInformation("");
        _logger.LogInformation("===========================================");
        _logger.LogInformation("Training Complete!");
        _logger.LogInformation("===========================================");
        _logger.LogInformation("Total time: {Elapsed:F1} minutes", elapsed.TotalMinutes);
        _logger.LogInformation("");
        _logger.LogInformation("Results saved to:");
        _logger.LogInformation("  Metrics: training_metrics/");
        _logger.LogInformation("  Checkpoints: checkpoints/");
        _logger.LogInformation("");
        _logger.LogInformation("To visualize results, run:");
        _logger.LogInformation("  python scripts/plot_training_curves.py training_metrics/metrics_*.csv");
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    public static async Task Main(string[] args)
    {
        var runner = new TrainingRunner();

        try
        {
            await runner.RunTrainingAsync(
                episodesPerTask: 200,
                maxCyclesPerEpisode: 50,
                checkpointInterval: 50,
                resume: true);
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

        System.Console.WriteLine("\nPress any key to exit...");
        System.Console.ReadKey();
    }
}
