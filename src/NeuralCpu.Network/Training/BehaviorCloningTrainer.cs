using Microsoft.Extensions.Logging;
using NeuralCpu.Core.Components;
using NeuralCpu.Network.Neural;
using TorchSharp;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace NeuralCpu.Network.Training;

/// <summary>
/// Trains a neural controller via behavior cloning (imitation learning).
/// Uses supervised learning with cross-entropy loss to imitate expert demonstrations.
/// </summary>
public sealed class BehaviorCloningTrainer : IDisposable
{
    #region Fields

    private readonly TransformerCpuController _controller;
    private readonly Device _device;
    private readonly optim.Optimizer _optimizer;
    private readonly ILogger<BehaviorCloningTrainer> _logger;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a behavior cloning trainer.
    /// </summary>
    /// <param name="controller">The controller to train.</param>
    /// <param name="learningRate">Learning rate (default 1e-3, higher than RL).</param>
    /// <param name="device">Torch device.</param>
    /// <param name="logger">Logger.</param>
    public BehaviorCloningTrainer(
        TransformerCpuController controller,
        double learningRate = 1e-3,
        Device? device = null,
        ILogger<BehaviorCloningTrainer>? logger = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _device = device ?? CPU;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Create Adam optimizer (higher LR than RL phase)
        _optimizer = optim.Adam(_controller.parameters(), lr: learningRate);
    }

    #endregion

    #region Training Methods

    /// <summary>
    /// Trains on demonstrations for multiple epochs.
    /// </summary>
    /// <param name="demonstrations">Expert demonstrations (state, action) pairs.</param>
    /// <param name="epochs">Number of training epochs.</param>
    /// <param name="batchSize">Batch size for training.</param>
    /// <param name="validationSplit">Fraction of data for validation (default 0.1).</param>
    /// <returns>Training metrics.</returns>
    public BehaviorCloningMetrics TrainOnDemonstrations(
        List<Demonstration> demonstrations,
        int epochs = 100,
        int batchSize = 32,
        double validationSplit = 0.1)
    {
        if (demonstrations.Count == 0)
        {
            _logger.LogWarning("No demonstrations provided for training");
            return new BehaviorCloningMetrics(0, 0, 0, 0);
        }

        // Split into train/validation
        var shuffled = demonstrations.OrderBy(_ => Random.Shared.Next()).ToList();
        var splitIndex = (int)(demonstrations.Count * (1 - validationSplit));
        var trainDemos = shuffled.Take(splitIndex).ToList();
        var valDemos = shuffled.Skip(splitIndex).ToList();

        _logger.LogInformation("Training on {Train} demonstrations, validating on {Val}",
            trainDemos.Count, valDemos.Count);

        double bestValLoss = double.MaxValue;
        int epochsSinceImprovement = 0;
        const int EARLY_STOPPING_PATIENCE = 10;

        var metrics = new BehaviorCloningMetrics(epochs, trainDemos.Count, valDemos.Count, batchSize);

        for (int epoch = 0; epoch < epochs; epoch++)
        {
            // Training phase
            _controller.train();
            var trainLoss = TrainEpoch(trainDemos, batchSize);

            // Validation phase
            _controller.eval();
            var valLoss = ValidateEpoch(valDemos, batchSize);
            var valAccuracy = ComputeAccuracy(valDemos);

            // Record metrics
            metrics.AddEpochMetrics(epoch, trainLoss, valLoss, valAccuracy);

            // Log progress
            if (epoch % 10 == 0 || epoch == epochs - 1)
            {
                _logger.LogInformation("Epoch {Epoch}/{Total}: TrainLoss={Train:F4}, ValLoss={Val:F4}, ValAcc={Acc:F2}%",
                    epoch + 1, epochs, trainLoss, valLoss, valAccuracy * 100);
            }

            // Early stopping
            if (valLoss < bestValLoss)
            {
                bestValLoss = valLoss;
                epochsSinceImprovement = 0;
            }
            else
            {
                epochsSinceImprovement++;
                if (epochsSinceImprovement >= EARLY_STOPPING_PATIENCE)
                {
                    _logger.LogInformation("Early stopping at epoch {Epoch} (no improvement for {Patience} epochs)",
                        epoch + 1, EARLY_STOPPING_PATIENCE);
                    break;
                }
            }
        }

        _logger.LogInformation("Behavior cloning complete. Final validation accuracy: {Acc:F2}%",
            metrics.FinalValidationAccuracy * 100);

        return metrics;
    }

    /// <summary>
    /// Trains on demonstrations for all tasks (multi-task learning).
    /// </summary>
    public Dictionary<string, BehaviorCloningMetrics> TrainOnAllTasks(
        Dictionary<string, List<Demonstration>> allDemonstrations,
        int epochs = 100,
        int batchSize = 32)
    {
        var results = new Dictionary<string, BehaviorCloningMetrics>();

        _logger.LogInformation("=== Multi-Task Behavior Cloning ===");
        _logger.LogInformation("Training on {Tasks} tasks", allDemonstrations.Count);

        // Combine all demonstrations (multi-task learning)
        var combinedDemos = allDemonstrations.Values
            .SelectMany(demos => demos)
            .ToList();

        _logger.LogInformation("Combined dataset: {Count} demonstrations", combinedDemos.Count);

        // Train on combined dataset
        var metrics = TrainOnDemonstrations(combinedDemos, epochs, batchSize);
        results["Combined"] = metrics;

        // Evaluate per-task accuracy
        foreach (var (taskName, taskDemos) in allDemonstrations)
        {
            _controller.eval();
            var accuracy = ComputeAccuracy(taskDemos);
            _logger.LogInformation("{Task} accuracy: {Acc:F2}%", taskName, accuracy * 100);
        }

        return results;
    }

    #endregion

    #region Training Loop

    private double TrainEpoch(List<Demonstration> demonstrations, int batchSize)
    {
        double totalLoss = 0;
        int batchCount = 0;

        // Shuffle demonstrations
        var shuffled = demonstrations.OrderBy(_ => Random.Shared.Next()).ToList();

        for (int i = 0; i < shuffled.Count; i += batchSize)
        {
            var batch = shuffled.Skip(i).Take(batchSize).ToList();
            if (batch.Count == 0) continue;

            var loss = TrainBatch(batch);
            totalLoss += loss;
            batchCount++;
        }

        return batchCount > 0 ? totalLoss / batchCount : 0;
    }

    private double TrainBatch(List<Demonstration> batch)
    {
        _optimizer.zero_grad();

        double totalLoss = 0;

        foreach (var demo in batch)
        {
            // Get network prediction
            var actionLogits = _controller.forward(demo.State);

            // Compute loss for this demonstration
            var loss = ComputeCrossEntropyLoss(actionLogits, demo.Action);

            // Accumulate gradients
            loss.backward();

            totalLoss += loss.item<float>();
            loss.Dispose();
            actionLogits.Dispose();
        }

        // Update parameters
        _optimizer.step();

        return totalLoss / batch.Count;
    }

    private double ValidateEpoch(List<Demonstration> demonstrations, int batchSize)
    {
        double totalLoss = 0;
        int count = 0;

        using var _ = no_grad();

        for (int i = 0; i < demonstrations.Count; i += batchSize)
        {
            var batch = demonstrations.Skip(i).Take(batchSize).ToList();
            if (batch.Count == 0) continue;

            foreach (var demo in batch)
            {
                var actionLogits = _controller.forward(demo.State);
                var loss = ComputeCrossEntropyLoss(actionLogits, demo.Action);

                totalLoss += loss.item<float>();
                count++;

                loss.Dispose();
                actionLogits.Dispose();
            }
        }

        return count > 0 ? totalLoss / count : 0;
    }

    #endregion

    #region Loss Computation

    /// <summary>
    /// Computes cross-entropy loss between predicted action logits and target action.
    /// </summary>
    private Tensor ComputeCrossEntropyLoss(Tensor actionLogits, EncodedEvent targetAction)
    {
        // actionLogits shape: (1, 313)
        // We need to compute separate cross-entropy for each action component

        const int EVENT_TYPE_DIM = 33;
        const int REGISTER_DIM = 8;

        var logitsFlat = actionLogits.squeeze(0); // (313,)

        // Extract logit sections
        var eventTypeLogits = logitsFlat[TensorIndex.Slice(0, EVENT_TYPE_DIM)];
        var reg1Logits = logitsFlat[TensorIndex.Slice(EVENT_TYPE_DIM, EVENT_TYPE_DIM + REGISTER_DIM)];
        var reg2Logits = logitsFlat[TensorIndex.Slice(EVENT_TYPE_DIM + REGISTER_DIM, EVENT_TYPE_DIM + REGISTER_DIM * 2)];
        var reg3Logits = logitsFlat[TensorIndex.Slice(EVENT_TYPE_DIM + REGISTER_DIM * 2, EVENT_TYPE_DIM + REGISTER_DIM * 3)];

        // Convert target action to indices
        var eventTypeIndex = MapEventTypeToIndex(targetAction.Type);
        var reg1Index = MapRegisterIdToIndex(targetAction.Reg1);
        var reg2Index = MapRegisterIdToIndex(targetAction.Reg2);
        var reg3Index = MapRegisterIdToIndex(targetAction.Reg3);

        // Create target tensors
        var eventTypeTarget = tensor(new long[] { eventTypeIndex }, device: _device);
        var reg1Target = tensor(new long[] { reg1Index }, device: _device);
        var reg2Target = tensor(new long[] { reg2Index }, device: _device);
        var reg3Target = tensor(new long[] { reg3Index }, device: _device);

        // Compute cross-entropy for each component
        var eventTypeLoss = functional.cross_entropy(eventTypeLogits.unsqueeze(0), eventTypeTarget);
        var reg1Loss = functional.cross_entropy(reg1Logits.unsqueeze(0), reg1Target);
        var reg2Loss = functional.cross_entropy(reg2Logits.unsqueeze(0), reg2Target);
        var reg3Loss = functional.cross_entropy(reg3Logits.unsqueeze(0), reg3Target);

        // Sum losses (equally weighted)
        var totalLoss = eventTypeLoss + reg1Loss + reg2Loss + reg3Loss;

        // Cleanup
        eventTypeTarget.Dispose();
        reg1Target.Dispose();
        reg2Target.Dispose();
        reg3Target.Dispose();
        eventTypeLoss.Dispose();
        reg1Loss.Dispose();
        reg2Loss.Dispose();
        reg3Loss.Dispose();

        return totalLoss;
    }

    #endregion

    #region Accuracy Computation

    private double ComputeAccuracy(List<Demonstration> demonstrations)
    {
        int correct = 0;
        int total = 0;

        using var _ = no_grad();

        foreach (var demo in demonstrations)
        {
            var actionLogits = _controller.forward(demo.State);
            var predictedAction = _controller.DecodeEncodedEvent(actionLogits);

            // Check if prediction matches target
            if (ActionsMatch(predictedAction, demo.Action))
                correct++;

            total++;

            actionLogits.Dispose();
        }

        return total > 0 ? (double)correct / total : 0;
    }

    private bool ActionsMatch(EncodedEvent predicted, EncodedEvent target)
    {
        return predicted.Type == target.Type &&
               predicted.Reg1 == target.Reg1 &&
               predicted.Reg2 == target.Reg2 &&
               predicted.Reg3 == target.Reg3;
        // Note: Not checking Value field for simplicity
    }

    #endregion

    #region Mapping Helpers

    private static int MapEventTypeToIndex(EventType eventType)
    {
        return eventType switch
        {
            EventType.RegisterWrite => 0,
            EventType.RegisterCopy => 1,
            EventType.HardwareInterrupt => 2,
            EventType.AluAdd => 3,
            EventType.AluSub => 4,
            EventType.AluMul => 5,
            EventType.AluDiv => 6,
            EventType.AluAnd => 7,
            EventType.AluNand => 8,
            EventType.AluOr => 9,
            EventType.AluNor => 10,
            EventType.AluXor => 11,
            EventType.AluXnor => 12,
            EventType.AluShl => 13,
            EventType.AluShr => 14,
            EventType.AluSar => 15,
            EventType.AluRol => 16,
            EventType.AluRor => 17,
            EventType.AluNot => 18,
            EventType.AluInc => 19,
            EventType.AluDec => 20,
            EventType.AluNeg => 21,
            EventType.AluAbs => 22,
            EventType.AluClz => 23,
            EventType.Push => 24,
            EventType.PushImmediate => 25,
            EventType.Pop => 26,
            EventType.Peek => 27,
            EventType.Dup => 28,
            EventType.LoadDirect => 29,
            EventType.LoadIndirect => 30,
            EventType.StoreDirect => 31,
            EventType.StoreIndirect => 32,
            _ => 0
        };
    }

    private static int MapRegisterIdToIndex(RegisterId registerId)
    {
        return registerId switch
        {
            RegisterId.None => 0,
            RegisterId.R0 => 1,
            RegisterId.R1 => 2,
            RegisterId.R2 => 3,
            RegisterId.R3 => 4,
            RegisterId.PC => 5,
            RegisterId.SP => 6,
            RegisterId.Flags => 7,
            _ => 0
        };
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        _optimizer?.Dispose();
    }

    #endregion
}

/// <summary>
/// Metrics from behavior cloning training.
/// </summary>
public class BehaviorCloningMetrics
{
    public int TotalEpochs { get; }
    public int TrainingSamples { get; }
    public int ValidationSamples { get; }
    public int BatchSize { get; }

    public List<double> TrainLosses { get; } = new();
    public List<double> ValidationLosses { get; } = new();
    public List<double> ValidationAccuracies { get; } = new();

    public double FinalTrainLoss => TrainLosses.LastOrDefault();
    public double FinalValidationLoss => ValidationLosses.LastOrDefault();
    public double FinalValidationAccuracy => ValidationAccuracies.LastOrDefault();

    public BehaviorCloningMetrics(int totalEpochs, int trainingSamples, int validationSamples, int batchSize)
    {
        TotalEpochs = totalEpochs;
        TrainingSamples = trainingSamples;
        ValidationSamples = validationSamples;
        BatchSize = batchSize;
    }

    public void AddEpochMetrics(int epoch, double trainLoss, double valLoss, double valAccuracy)
    {
        TrainLosses.Add(trainLoss);
        ValidationLosses.Add(valLoss);
        ValidationAccuracies.Add(valAccuracy);
    }
}
