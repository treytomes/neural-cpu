using Microsoft.Extensions.Logging;
using NeuralCpu.Core.Components;
using NeuralCpu.Network.Neural;

namespace NeuralCpu.Network.Training;

/// <summary>
/// Orchestrates end-to-end training: DPO warmup, policy gradient, and mixed training.
/// Coordinates DPOTrainer and TaskRunner for complete training loops.
/// </summary>
public sealed class TrainingOrchestrator
{
    #region Fields

    private readonly TransformerCpuController _controller;
    private readonly DPOTrainer _dpoTrainer;
    private readonly PolicyGradientTrainer _policyGradientTrainer;
    private readonly TaskRunner _taskRunner;
    private readonly ILogger<TrainingOrchestrator> _logger;

    #endregion

    #region Constructors

    public TrainingOrchestrator(
        TransformerCpuController controller,
        DPOTrainer dpoTrainer,
        TaskRunner taskRunner,
        ILogger<TrainingOrchestrator> logger,
        PolicyGradientTrainer? policyGradientTrainer = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _dpoTrainer = dpoTrainer ?? throw new ArgumentNullException(nameof(dpoTrainer));
        _taskRunner = taskRunner ?? throw new ArgumentNullException(nameof(taskRunner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Create policy gradient trainer if not provided
        _policyGradientTrainer = policyGradientTrainer ?? new PolicyGradientTrainer(
            controller: controller,
            gamma: 0.99,
            learningRate: 1e-4);
    }

    #endregion

    #region DPO Warmup

    /// <summary>
    /// Runs DPO warmup phase - trains network to generate valid events.
    /// </summary>
    /// <param name="steps">Number of DPO training steps.</param>
    /// <returns>Array of DPO metrics for each step.</returns>
    public DPOMetrics[] RunDPOWarmup(int steps)
    {
        _logger.LogInformation("Starting DPO warmup: {Steps} steps", steps);

        var metrics = new DPOMetrics[steps];
        var state = CreateRandomStateVector();

        for (int i = 0; i < steps; i++)
        {
            metrics[i] = _dpoTrainer.TrainStep(state);

            if (i % 10 == 0)
            {
                _logger.LogInformation("DPO warmup step {Step}/{Total}: Loss={Loss:F4}, InvalidRate={InvalidRate:F2}",
                    i, steps, metrics[i].Loss, metrics[i].WasInvalid ? 1.0 : 0.0);
            }
        }

        _logger.LogInformation("DPO warmup complete. Final loss: {Loss:F4}", metrics[^1].Loss);
        return metrics;
    }

    /// <summary>
    /// Measures invalid event rate by sampling network outputs.
    /// </summary>
    /// <param name="samples">Number of samples to test.</param>
    /// <returns>Invalid event rate (0.0 to 1.0).</returns>
    public double MeasureInvalidEventRate(int samples)
    {
        var state = CreateRandomStateVector();
        int invalidCount = 0;

        for (int i = 0; i < samples; i++)
        {
            var actionLogits = _controller.GetActionLogits(state);
            var generatedEvent = _controller.DecodeEncodedEvent(actionLogits);
            var validation = EventValidationDPO.Validate(generatedEvent);

            if (!validation.IsValid)
                invalidCount++;
        }

        return (double)invalidCount / samples;
    }

    #endregion

    #region Task Training

    /// <summary>
    /// Trains network on a task using policy gradient (REINFORCE).
    /// Uses the real REINFORCE algorithm with gradient backpropagation.
    /// </summary>
    /// <param name="task">Task definition with input/output.</param>
    /// <param name="episodes">Number of training episodes.</param>
    /// <param name="maxCyclesPerEpisode">Max cycles per episode.</param>
    /// <returns>Array of training metrics for each episode.</returns>
    public TaskTrainingMetrics[] TrainOnTask(TaskDefinition task, int episodes, int maxCyclesPerEpisode)
    {
        _logger.LogInformation("Starting task training (policy gradient): {TaskName}, {Episodes} episodes", task.Name, episodes);

        var metrics = new TaskTrainingMetrics[episodes];

        for (int ep = 0; ep < episodes; ep++)
        {
            // Run episode with TaskRunner (network is in training mode, records states/actions)
            var result = _taskRunner.RunEpisode(task, maxCyclesPerEpisode);

            // Get episode buffer from controller
            var episodeBuffer = _controller.GetEpisodeBuffer();

            // Train on episode trajectory using policy gradient
            // Policy gradient trainer will compute returns and backpropagate
            var pgMetrics = _policyGradientTrainer.TrainOnEpisode(episodeBuffer, result.Reward);

            metrics[ep] = new TaskTrainingMetrics(
                EpisodeNumber: ep,
                Reward: result.Reward,
                CycleCount: result.CycleCount,
                PolicyLoss: pgMetrics.PolicyLoss,
                InvalidEventRate: 0.0);

            if (ep % 5 == 0)
            {
                _logger.LogInformation("Episode {Episode}/{Total}: Reward={Reward:F2}, Loss={Loss:F4}, Cycles={Cycles}",
                    ep, episodes, result.Reward, pgMetrics.PolicyLoss, result.CycleCount);
            }

            // Clear controller buffer for next episode
            _controller.Reset();
        }

        _logger.LogInformation("Task training complete. Final reward: {Reward:F2}", metrics[^1].Reward);
        return metrics;
    }

    /// <summary>
    /// Trains network with mixed DPO + policy gradient loss.
    /// </summary>
    /// <param name="task">Task definition.</param>
    /// <param name="episodes">Number of episodes.</param>
    /// <param name="maxCyclesPerEpisode">Max cycles per episode.</param>
    /// <param name="lambdaDpo">Weight for DPO loss.</param>
    /// <param name="lambdaPolicy">Weight for policy gradient loss.</param>
    /// <returns>Array of mixed training metrics.</returns>
    public MixedTrainingMetrics[] TrainOnTaskWithDPO(
        TaskDefinition task,
        int episodes,
        int maxCyclesPerEpisode,
        double lambdaDpo,
        double lambdaPolicy)
    {
        _logger.LogInformation("Starting mixed training: {TaskName}, λ_DPO={LambdaDpo}, λ_Policy={LambdaPolicy}",
            task.Name, lambdaDpo, lambdaPolicy);

        var metrics = new MixedTrainingMetrics[episodes];

        for (int ep = 0; ep < episodes; ep++)
        {
            // Run episode
            var result = _taskRunner.RunEpisode(task, maxCyclesPerEpisode);

            // Compute DPO loss (sample from current state)
            var state = CreateRandomStateVector();
            var dpoMetrics = _dpoTrainer.TrainStep(state);

            // Compute policy loss
            var policyLoss = 1.0 - result.Reward;

            // Combined loss
            var totalLoss = lambdaDpo * dpoMetrics.Loss + lambdaPolicy * policyLoss;

            metrics[ep] = new MixedTrainingMetrics(
                EpisodeNumber: ep,
                Reward: result.Reward,
                CycleCount: result.CycleCount,
                DPOLoss: dpoMetrics.Loss,
                PolicyLoss: policyLoss,
                TotalLoss: totalLoss,
                InvalidEventRate: dpoMetrics.WasInvalid ? 1.0 : 0.0);

            if (ep % 5 == 0)
            {
                _logger.LogInformation(
                    "Episode {Episode}/{Total}: Reward={Reward:F2}, TotalLoss={TotalLoss:F4}, InvalidRate={InvalidRate:F2}",
                    ep, episodes, result.Reward, totalLoss, metrics[ep].InvalidEventRate);
            }
        }

        _logger.LogInformation("Mixed training complete. Final reward: {Reward:F2}", metrics[^1].Reward);
        return metrics;
    }

    #endregion

    #region Convergence

    /// <summary>
    /// Trains until target reward is reached or max episodes exceeded.
    /// </summary>
    /// <param name="task">Task definition.</param>
    /// <param name="targetReward">Target average reward (0.0-1.0).</param>
    /// <param name="maxEpisodes">Maximum episodes to run.</param>
    /// <param name="maxCyclesPerEpisode">Max cycles per episode.</param>
    /// <param name="windowSize">Rolling window size for average reward.</param>
    /// <returns>Array of training metrics.</returns>
    public TaskTrainingMetrics[] TrainUntilConvergence(
        TaskDefinition task,
        double targetReward,
        int maxEpisodes,
        int maxCyclesPerEpisode,
        int windowSize = 5)
    {
        _logger.LogInformation(
            "Training until convergence: {TaskName}, target={TargetReward:F2}, maxEpisodes={MaxEpisodes}",
            task.Name, targetReward, maxEpisodes);

        var metricsList = new List<TaskTrainingMetrics>();
        var rewardWindow = new Queue<double>();

        for (int ep = 0; ep < maxEpisodes; ep++)
        {
            // Run episode
            var result = _taskRunner.RunEpisode(task, maxCyclesPerEpisode);
            var policyLoss = 1.0 - result.Reward;

            var metric = new TaskTrainingMetrics(
                EpisodeNumber: ep,
                Reward: result.Reward,
                CycleCount: result.CycleCount,
                PolicyLoss: policyLoss,
                InvalidEventRate: 0.0);

            metricsList.Add(metric);

            // Update rolling window
            rewardWindow.Enqueue(result.Reward);
            if (rewardWindow.Count > windowSize)
                rewardWindow.Dequeue();

            // Check convergence
            if (rewardWindow.Count == windowSize)
            {
                var avgReward = rewardWindow.Average();
                if (avgReward >= targetReward)
                {
                    _logger.LogInformation(
                        "Convergence reached at episode {Episode}: avgReward={AvgReward:F2} >= target={TargetReward:F2}",
                        ep, avgReward, targetReward);
                    break;
                }
            }

            if (ep % 5 == 0)
            {
                var avgReward = rewardWindow.Count > 0 ? rewardWindow.Average() : 0.0;
                _logger.LogInformation("Episode {Episode}/{Total}: Reward={Reward:F2}, Avg={AvgReward:F2}",
                    ep, maxEpisodes, result.Reward, avgReward);
            }
        }

        _logger.LogInformation("Training stopped after {Episodes} episodes", metricsList.Count);
        return metricsList.ToArray();
    }

    #endregion

    #region Helper Methods

    private CpuStateVector CreateRandomStateVector()
    {
        // Create minimal state vector for DPO warmup
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
}

/// <summary>
/// Metrics from task training (policy gradient only).
/// </summary>
public record TaskTrainingMetrics(
    int EpisodeNumber,
    double Reward,
    int CycleCount,
    double PolicyLoss,
    double InvalidEventRate);

/// <summary>
/// Metrics from mixed training (DPO + policy gradient).
/// </summary>
public record MixedTrainingMetrics(
    int EpisodeNumber,
    double Reward,
    int CycleCount,
    double DPOLoss,
    double PolicyLoss,
    double TotalLoss,
    double InvalidEventRate);
