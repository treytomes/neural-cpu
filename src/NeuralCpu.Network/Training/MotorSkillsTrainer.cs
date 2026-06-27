using Microsoft.Extensions.Logging;
using NeuralCpu.Core;
using NeuralCpu.Core.Components;
using NeuralCpu.Core.Configuration;
using NeuralCpu.Core.Events;
using NeuralCpu.Network.Neural;

namespace NeuralCpu.Network.Training;

/// <summary>
/// Trains a neural controller on basic motor skills tasks using policy gradient.
/// Phase 5.5.3: Component Mastery - teaches network individual component control.
/// </summary>
public sealed class MotorSkillsTrainer
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
    private readonly ILogger<MotorSkillsTrainer> _logger;
    private readonly ILoggerFactory _loggerFactory;

    #endregion

    #region Constructors

    public MotorSkillsTrainer(
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
        ILoggerFactory loggerFactory)
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
        _logger = loggerFactory.CreateLogger<MotorSkillsTrainer>();
    }

    #endregion

    #region Training Methods

    /// <summary>
    /// Trains all five basic motor skills tasks sequentially.
    /// </summary>
    /// <param name="episodesPerTask">Number of training episodes per task (default 200).</param>
    /// <param name="maxCyclesPerEpisode">Max cycles per episode (default 50).</param>
    /// <returns>Training results for all tasks.</returns>
    public MotorSkillsTrainingResult TrainAllTasks(int episodesPerTask = 200, int maxCyclesPerEpisode = 50)
    {
        _logger.LogInformation("Starting Basic Motor Skills Training");
        _logger.LogInformation("Configuration: {Episodes} episodes per task, {MaxCycles} max cycles",
            episodesPerTask, maxCyclesPerEpisode);

        var taskLogger = _loggerFactory.CreateLogger<BasicMotorSkillsTask>();
        var task = new BasicMotorSkillsTask(
            _eventBus, _clock, _registerFile, _stack, _memory,
            _controller, _stateVectorBuilder,
            taskLogger,
            maxCyclesPerEpisode: maxCyclesPerEpisode,
            inputDevice: _inputDevice,
            outputDevice: _outputDevice);

        var results = new MotorSkillsTrainingResult();

        // Task 1: RegisterActivation
        _logger.LogInformation("=== Task 1/5: RegisterActivation ===");
        results.RegisterActivation = TrainTask(
            taskName: "RegisterActivation",
            taskFunc: () => task.RunRegisterActivationTask(),
            episodes: episodesPerTask);

        // Task 2: StackGrowth
        _logger.LogInformation("=== Task 2/5: StackGrowth ===");
        results.StackGrowth = TrainTask(
            taskName: "StackGrowth",
            taskFunc: () => task.RunStackGrowthTask(),
            episodes: episodesPerTask);

        // Task 3: ALUActivation
        _logger.LogInformation("=== Task 3/5: ALUActivation ===");
        results.AluActivation = TrainTask(
            taskName: "ALUActivation",
            taskFunc: () => task.RunAluActivationTask(),
            episodes: episodesPerTask);

        // Task 4: InputRead
        _logger.LogInformation("=== Task 4/5: InputRead ===");
        results.InputRead = TrainTask(
            taskName: "InputRead",
            taskFunc: () => task.RunInputReadTask(),
            episodes: episodesPerTask);

        // Task 5: OutputWrite
        _logger.LogInformation("=== Task 5/5: OutputWrite ===");
        results.OutputWrite = TrainTask(
            taskName: "OutputWrite",
            taskFunc: () => task.RunOutputWriteTask(),
            episodes: episodesPerTask);

        _logger.LogInformation("Basic Motor Skills Training Complete");
        LogOverallResults(results);

        return results;
    }

    /// <summary>
    /// Trains a single motor skills task.
    /// </summary>
    private TaskMetrics[] TrainTask(string taskName, Func<TaskResult> taskFunc, int episodes)
    {
        var metrics = new TaskMetrics[episodes];

        for (int ep = 0; ep < episodes; ep++)
        {
            // Run task episode (controller auto-buffers when isTraining=true)
            var result = taskFunc();

            // Get episode buffer from controller
            var episodeBuffer = _controller.GetEpisodeBuffer();

            // Train with policy gradient
            var pgMetrics = _policyGradientTrainer.TrainOnEpisode(episodeBuffer, result.Reward);

            metrics[ep] = new TaskMetrics(
                Episode: ep,
                Success: result.Success,
                Reward: result.Reward,
                CycleCount: result.CycleCount,
                PolicyLoss: pgMetrics.PolicyLoss);

            // Log progress every 20 episodes
            if (ep % 20 == 0 || ep == episodes - 1)
            {
                _logger.LogInformation("{TaskName} Episode {Episode}/{Total}: Success={Success}, Reward={Reward:F2}, Cycles={Cycles}, Loss={Loss:F4}",
                    taskName, ep, episodes, result.Success, result.Reward, result.CycleCount, pgMetrics.PolicyLoss);
            }

            // Reset controller buffer for next episode
            _controller.Reset();
        }

        // Log task summary
        var successCount = metrics.Count(m => m.Success);
        var avgCycles = metrics.Where(m => m.Success).Select(m => m.CycleCount).DefaultIfEmpty(0).Average();
        var successRate = (double)successCount / episodes;

        _logger.LogInformation("{TaskName} Summary: {SuccessRate:P1} success rate ({Success}/{Total}), {AvgCycles:F1} avg cycles to success",
            taskName, successRate, successCount, episodes, avgCycles);

        return metrics;
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
}

/// <summary>
/// Training results for all five basic motor skills tasks.
/// </summary>
public sealed record MotorSkillsTrainingResult
{
    public TaskMetrics[]? RegisterActivation { get; set; }
    public TaskMetrics[]? StackGrowth { get; set; }
    public TaskMetrics[]? AluActivation { get; set; }
    public TaskMetrics[]? InputRead { get; set; }
    public TaskMetrics[]? OutputWrite { get; set; }
}

/// <summary>
/// Metrics for a single training episode.
/// </summary>
public sealed record TaskMetrics(
    int Episode,
    bool Success,
    double Reward,
    int CycleCount,
    double PolicyLoss);
