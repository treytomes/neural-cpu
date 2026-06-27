using Microsoft.Extensions.Logging;
using NeuralCpu.Core;
using NeuralCpu.Core.Components;
using NeuralCpu.Core.Configuration;
using NeuralCpu.Core.Events;
using NeuralCpu.Network.Neural;

namespace NeuralCpu.Network.Training;

/// <summary>
/// Enhanced motor skills trainer with metrics logging and checkpointing.
/// Supports resuming training from checkpoints.
/// </summary>
public sealed class MotorSkillsTrainerWithCheckpoints : IDisposable
{
    #region Fields

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
    private readonly ILogger<MotorSkillsTrainerWithCheckpoints> _logger;
    private readonly ILoggerFactory _loggerFactory;

    private readonly TrainingMetricsLogger _metricsLogger;
    private readonly TrainingCheckpoint _checkpoint;
    private readonly int _checkpointInterval; // Episodes between checkpoints

    #endregion

    #region Constructors

    public MotorSkillsTrainerWithCheckpoints(
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
        PolicyGradientTrainer policyGradientTrainer,
        ILoggerFactory loggerFactory,
        string metricsDirectory = "training_metrics",
        string checkpointDirectory = "checkpoints",
        int checkpointInterval = 50,
        string? runId = null)
    {
        _eventBus = eventBus;
        _clock = clock;
        _registerFile = registerFile;
        _alu = alu;
        _stack = stack;
        _memory = memory;
        _inputDevice = inputDevice;
        _outputDevice = outputDevice;
        _deviceRegistry = deviceRegistry;
        _stateVectorBuilder = stateVectorBuilder;
        _controller = controller;
        _policyGradientTrainer = policyGradientTrainer;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<MotorSkillsTrainerWithCheckpoints>();
        _checkpointInterval = checkpointInterval;

        _metricsLogger = new TrainingMetricsLogger(metricsDirectory, runId);
        _checkpoint = new TrainingCheckpoint(checkpointDirectory, runId);
    }

    #endregion

    #region Training Methods

    /// <summary>
    /// Trains all five basic motor skills tasks with metrics logging and checkpointing.
    /// </summary>
    public MotorSkillsTrainingResult TrainAllTasks(
        int episodesPerTask = 200,
        int maxCyclesPerEpisode = 50,
        TrainingState? resumeState = null)
    {
        _logger.LogInformation("Starting Basic Motor Skills Training with Checkpointing");
        _logger.LogInformation("Configuration: {Episodes} episodes per task, {MaxCycles} max cycles, checkpoint every {Interval} episodes",
            episodesPerTask, maxCyclesPerEpisode, _checkpointInterval);

        var startTime = DateTimeOffset.UtcNow;
        var networkConfig = new NetworkConfiguration(
            EmbeddingDim: 128,  // Would extract from controller if possible
            NumHeads: 4,
            NumEncoderLayers: 2,
            Dropout: 0.1);

        var trainingConfig = new TrainingConfiguration(
            LearningRate: 1e-4,
            Gamma: 0.99,
            EpisodesPerTask: episodesPerTask,
            MaxCyclesPerEpisode: maxCyclesPerEpisode);

        var taskLogger = _loggerFactory.CreateLogger<BasicMotorSkillsTask>();
        var task = new BasicMotorSkillsTask(
            _eventBus, _clock, _registerFile, _stack, _memory,
            _controller, _stateVectorBuilder,
            taskLogger,
            maxCyclesPerEpisode: maxCyclesPerEpisode,
            inputDevice: _inputDevice,
            outputDevice: _outputDevice);

        var results = new MotorSkillsTrainingResult();
        var taskProgress = new Dictionary<string, TaskProgress>();
        int totalEpisodes = 0;

        // Task definitions
        var tasks = new[]
        {
            ("RegisterActivation", (Func<TaskResult>)(() => task.RunRegisterActivationTask())),
            ("StackGrowth", (Func<TaskResult>)(() => task.RunStackGrowthTask())),
            ("ALUActivation", (Func<TaskResult>)(() => task.RunAluActivationTask())),
            ("InputRead", (Func<TaskResult>)(() => task.RunInputReadTask())),
            ("OutputWrite", (Func<TaskResult>)(() => task.RunOutputWriteTask()))
        };

        for (int taskIdx = 0; taskIdx < tasks.Length; taskIdx++)
        {
            var (taskName, taskFunc) = tasks[taskIdx];
            _logger.LogInformation("=== Task {Index}/5: {TaskName} ===", taskIdx + 1, taskName);

            var metrics = TrainTask(
                taskName: taskName,
                taskFunc: taskFunc,
                episodes: episodesPerTask,
                networkConfig: networkConfig,
                trainingConfig: trainingConfig,
                startingEpisode: resumeState?.TaskProgress.ContainsKey(taskName) == true
                    ? resumeState.TaskProgress[taskName].EpisodesCompleted
                    : 0,
                totalEpisodesCompleted: totalEpisodes);

            // Store results
            switch (taskName)
            {
                case "RegisterActivation": results.RegisterActivation = metrics; break;
                case "StackGrowth": results.StackGrowth = metrics; break;
                case "ALUActivation": results.AluActivation = metrics; break;
                case "InputRead": results.InputRead = metrics; break;
                case "OutputWrite": results.OutputWrite = metrics; break;
            }

            totalEpisodes += metrics.Length;

            // Log task summary to metrics
            var successCount = metrics.Count(m => m.Success);
            var avgCycles = metrics.Where(m => m.Success).Select(m => m.CycleCount).DefaultIfEmpty(0).Average();
            _metricsLogger.LogTaskSummary(taskName, metrics.Length, successCount, avgCycles);
        }

        var elapsed = DateTimeOffset.UtcNow - startTime;
        _logger.LogInformation("Basic Motor Skills Training Complete in {Elapsed:F1} minutes", elapsed.TotalMinutes);
        _logger.LogInformation("Metrics saved to: {Path}", Path.Combine("training_metrics", $"metrics_{_metricsLogger.GetType().Name}.csv"));

        LogOverallResults(results);

        return results;
    }

    /// <summary>
    /// Trains a single motor skills task with checkpointing.
    /// </summary>
    private TaskMetrics[] TrainTask(
        string taskName,
        Func<TaskResult> taskFunc,
        int episodes,
        NetworkConfiguration networkConfig,
        TrainingConfiguration trainingConfig,
        int startingEpisode,
        int totalEpisodesCompleted)
    {
        var metrics = new List<TaskMetrics>();

        for (int ep = startingEpisode; ep < episodes; ep++)
        {
            // Run task episode
            var result = taskFunc();

            // Get episode buffer from controller
            var episodeBuffer = _controller.GetEpisodeBuffer();

            // Train with policy gradient
            var pgMetrics = _policyGradientTrainer.TrainOnEpisode(episodeBuffer, result.Reward);

            var taskMetric = new TaskMetrics(
                Episode: ep,
                Success: result.Success,
                Reward: result.Reward,
                CycleCount: result.CycleCount,
                PolicyLoss: pgMetrics.PolicyLoss);

            metrics.Add(taskMetric);

            // Log to metrics file
            _metricsLogger.LogEpisode(taskName, ep, result.Success, result.Reward, result.CycleCount, pgMetrics.PolicyLoss);

            // Log progress every 20 episodes
            if (ep % 20 == 0 || ep == episodes - 1)
            {
                _logger.LogInformation("{TaskName} Episode {Episode}/{Total}: Success={Success}, Reward={Reward:F2}, Cycles={Cycles}, Loss={Loss:F6}",
                    taskName, ep, episodes, result.Success, result.Reward, result.CycleCount, pgMetrics.PolicyLoss);
            }

            // Checkpoint every N episodes
            if (_checkpointInterval > 0 && (ep + 1) % _checkpointInterval == 0)
            {
                SaveCheckpoint(taskName, ep, totalEpisodesCompleted + ep, networkConfig, trainingConfig);
            }

            // Reset controller for next episode
            _controller.Reset();
        }

        // Final checkpoint after task completion
        SaveCheckpoint(taskName, episodes - 1, totalEpisodesCompleted + episodes, networkConfig, trainingConfig);

        // Log task summary
        var successCount = metrics.Count(m => m.Success);
        var avgCycles = metrics.Where(m => m.Success).Select(m => m.CycleCount).DefaultIfEmpty(0).Average();
        var successRate = (double)successCount / episodes;

        _logger.LogInformation("{TaskName} Summary: {SuccessRate:P1} success rate ({Success}/{Total}), {AvgCycles:F1} avg cycles to success",
            taskName, successRate, successCount, episodes, avgCycles);

        return metrics.ToArray();
    }

    private void SaveCheckpoint(
        string taskName,
        int episode,
        int totalEpisodes,
        NetworkConfiguration networkConfig,
        TrainingConfiguration trainingConfig)
    {
        _logger.LogInformation("Saving checkpoint: {Task} episode {Episode}", taskName, episode);

        var state = new TrainingState(
            RunId: "current",  // Would get from constructor
            NetworkConfig: networkConfig,
            TrainingConfig: trainingConfig,
            CurrentTask: taskName,
            CurrentTaskEpisode: episode,
            TotalEpisodesCompleted: totalEpisodes,
            TaskProgress: new Dictionary<string, TaskProgress>(),  // Would track actual progress
            StartTime: DateTimeOffset.UtcNow,  // Would track from beginning
            LastCheckpointTime: DateTimeOffset.UtcNow);

        _checkpoint.Save(_controller, _policyGradientTrainer, state, taskName, episode);
    }

    private void LogOverallResults(MotorSkillsTrainingResult results)
    {
        _logger.LogInformation("=== Overall Summary ===");

        var tasks = new[]
        {
            ("RegisterActivation", results.RegisterActivation),
            ("StackGrowth", results.StackGrowth),
            ("ALUActivation", results.AluActivation),
            ("InputRead", results.InputRead),
            ("OutputWrite", results.OutputWrite)
        };

        foreach (var (name, metrics) in tasks)
        {
            if (metrics == null) continue;

            var successRate = metrics.Count(m => m.Success) / (double)metrics.Length;
            var avgCyclesToSuccess = metrics
                .Where(m => m.Success)
                .Select(m => m.CycleCount)
                .DefaultIfEmpty(0)
                .Average();

            _logger.LogInformation("{TaskName}: {SuccessRate:P1} success, {AvgCycles:F1} avg cycles",
                name, successRate, avgCyclesToSuccess);
        }
    }

    #endregion

    public void Dispose()
    {
        _metricsLogger?.Dispose();
    }
}
