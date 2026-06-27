using Microsoft.Extensions.Logging;
using NeuralCpu.Core.Components;
using NeuralCpu.Network.Neural;
using TorchSharp;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace NeuralCpu.Network.Training;

/// <summary>
/// Implements policy gradient (REINFORCE) training for the neural CPU controller.
/// Collects episode trajectories and computes gradients from discounted returns.
/// </summary>
public sealed class PolicyGradientTrainer : IDisposable
{
    #region Fields

    private readonly TransformerCpuController _controller;
    private readonly double _gamma; // Discount factor
    private readonly Device _device;
    private readonly optim.Optimizer _optimizer;

    // Episode buffer: stores (state, action_logits, reward) for each step
    private readonly List<EpisodeStep> _episodeBuffer;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a policy gradient trainer.
    /// </summary>
    /// <param name="controller">The transformer controller to train.</param>
    /// <param name="gamma">Discount factor for returns (default 0.99).</param>
    /// <param name="learningRate">Learning rate for optimizer (default 1e-4).</param>
    /// <param name="device">Torch device (CPU or CUDA).</param>
    public PolicyGradientTrainer(
        TransformerCpuController controller,
        double gamma = 0.99,
        double learningRate = 1e-4,
        Device? device = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _gamma = gamma;
        _device = device ?? CPU;

        // Create Adam optimizer for controller parameters
        _optimizer = optim.Adam(_controller.parameters(), lr: learningRate);

        _episodeBuffer = new List<EpisodeStep>();
    }

    #endregion

    #region Episode Management

    /// <summary>
    /// Begins a new episode, clearing the episode buffer.
    /// </summary>
    public void BeginEpisode()
    {
        _episodeBuffer.Clear();
    }

    /// <summary>
    /// Records a step in the current episode.
    /// </summary>
    /// <param name="state">CPU state at this step.</param>
    /// <param name="reward">Reward received at this step.</param>
    public void RecordStep(CpuStateVector state, double reward)
    {
        // Get action logits from network (will be used for computing log probs)
        var actionLogits = _controller.GetActionLogits(state);

        // Store step in buffer (keep tensor in computation graph)
        _episodeBuffer.Add(new EpisodeStep(
            State: state,
            ActionLogits: actionLogits.clone(), // Clone to keep in memory
            Reward: reward));
    }

    /// <summary>
    /// Ends the current episode and runs a training step.
    /// </summary>
    /// <returns>Training metrics.</returns>
    public PolicyGradientMetrics EndEpisode()
    {
        return TrainStep();
    }

    #endregion

    #region Policy Gradient Algorithm

    /// <summary>
    /// Computes policy gradient loss and performs backpropagation.
    /// Implements REINFORCE algorithm: ∇J = E[∑_t ∇log π(a_t|s_t) * R_t]
    /// </summary>
    /// <param name="useBaseline">Whether to use baseline (mean return) for variance reduction.</param>
    /// <returns>Training metrics.</returns>
    public PolicyGradientMetrics TrainStep(bool useBaseline = true)
    {
        if (_episodeBuffer.Count == 0)
        {
            // Empty episode - return zero metrics
            return new PolicyGradientMetrics(
                PolicyLoss: 0.0,
                AverageReturn: 0.0,
                StepCount: 0);
        }

        // Zero gradients
        _optimizer.zero_grad();

        // Compute policy loss (as tensor with gradients)
        var lossTensor = ComputePolicyLossTensor(useBaseline);
        var lossValue = lossTensor.item<float>();

        // Backward pass
        lossTensor.backward();

        // Update parameters
        _optimizer.step();

        // Compute average return for metrics
        var rewards = _episodeBuffer.Select(s => s.Reward).ToArray();
        var avgReturn = rewards.Average();

        return new PolicyGradientMetrics(
            PolicyLoss: lossValue,
            AverageReturn: avgReturn,
            StepCount: _episodeBuffer.Count);
    }

    /// <summary>
    /// Computes policy loss as scalar (for testing/metrics).
    /// </summary>
    public double ComputePolicyLoss(bool useBaseline = true)
    {
        using var _ = no_grad(); // Don't build computation graph
        var lossTensor = ComputePolicyLossTensor(useBaseline);
        return lossTensor.item<float>();
    }

    /// <summary>
    /// Computes policy loss tensor with gradients.
    /// Loss = -∑_t log π(a_t|s_t) * (R_t - baseline)
    /// Negative because we want to maximize log prob of actions with high returns.
    /// </summary>
    private Tensor ComputePolicyLossTensor(bool useBaseline)
    {
        // Extract rewards from episode
        var rewards = _episodeBuffer.Select(s => s.Reward).ToArray();

        // Compute discounted returns: R_t = ∑_{k=0}^∞ γ^k r_{t+k}
        var returns = ComputeDiscountedReturns(rewards, _gamma);

        // Compute baseline (mean of returns) for variance reduction
        var baseline = useBaseline ? returns.Average() : 0.0;

        // Compute advantages: A_t = R_t - baseline
        var advantages = returns.Select(r => r - baseline).ToArray();

        // Convert advantages to tensor
        var advantagesTensor = tensor(advantages, dtype: ScalarType.Float32, device: _device);

        // Compute log probabilities for each step
        var logProbs = new List<Tensor>();
        for (int t = 0; t < _episodeBuffer.Count; t++)
        {
            var step = _episodeBuffer[t];

            // Decode event from logits
            var decodedEvent = _controller.DecodeEncodedEvent(step.ActionLogits);

            // Compute log probability of this action
            var logProb = ComputeLogProbTensor(step.ActionLogits, decodedEvent);
            logProbs.Add(logProb);
        }

        // Stack log probs into tensor: (num_steps,)
        var logProbsTensor = stack(logProbs.ToArray(), dim: 0);

        // Policy gradient loss: -∑_t log π(a_t|s_t) * A_t
        // Element-wise multiply, then sum
        var loss = -(logProbsTensor * advantagesTensor).sum();

        // Dispose intermediate tensors
        foreach (var lp in logProbs)
            lp.Dispose();
        advantagesTensor.Dispose();
        logProbsTensor.Dispose();

        return loss;
    }

    /// <summary>
    /// Computes log probability of an action given logits.
    /// Same as DPOTrainer.ComputeLogProbTensor but duplicated to avoid circular dependency.
    /// </summary>
    private Tensor ComputeLogProbTensor(Tensor actionLogits, EncodedEvent evt)
    {
        // actionLogits shape: (1, 313)
        const int EVENT_TYPE_DIM = 33;
        const int REGISTER_DIM = 8;

        var logitsFlat = actionLogits.squeeze(0); // (313,)

        // Extract sections
        var eventTypeLogits = logitsFlat[TensorIndex.Slice(0, EVENT_TYPE_DIM)];
        var reg1Logits = logitsFlat[TensorIndex.Slice(EVENT_TYPE_DIM, EVENT_TYPE_DIM + REGISTER_DIM)];
        var reg2Logits = logitsFlat[TensorIndex.Slice(EVENT_TYPE_DIM + REGISTER_DIM, EVENT_TYPE_DIM + REGISTER_DIM * 2)];
        var reg3Logits = logitsFlat[TensorIndex.Slice(EVENT_TYPE_DIM + REGISTER_DIM * 2, EVENT_TYPE_DIM + REGISTER_DIM * 3)];

        // Compute log softmax for each field
        var eventTypeLogProbs = functional.log_softmax(eventTypeLogits, dim: 0);
        var reg1LogProbs = functional.log_softmax(reg1Logits, dim: 0);
        var reg2LogProbs = functional.log_softmax(reg2Logits, dim: 0);
        var reg3LogProbs = functional.log_softmax(reg3Logits, dim: 0);

        // Map event and registers to indices
        var eventTypeIndex = MapEventTypeToIndex(evt.Type);
        var reg1Index = MapRegisterIdToIndex(evt.Reg1);
        var reg2Index = MapRegisterIdToIndex(evt.Reg2);
        var reg3Index = MapRegisterIdToIndex(evt.Reg3);

        // Extract log probs at indices (as tensors)
        var logProbEventType = eventTypeLogProbs[eventTypeIndex];
        var logProbReg1 = reg1LogProbs[reg1Index];
        var logProbReg2 = reg2LogProbs[reg2Index];
        var logProbReg3 = reg3LogProbs[reg3Index];

        // Sum log probs (product of probabilities = sum of log probs)
        var totalLogProb = logProbEventType + logProbReg1 + logProbReg2 + logProbReg3;

        return totalLogProb;
    }

    #endregion

    #region Discounted Returns

    /// <summary>
    /// Computes discounted returns from rewards.
    /// R_t = ∑_{k=0}^∞ γ^k r_{t+k}
    /// </summary>
    /// <param name="rewards">Rewards for each step.</param>
    /// <param name="gamma">Discount factor.</param>
    /// <returns>Discounted returns for each step.</returns>
    public static double[] ComputeDiscountedReturns(double[] rewards, double gamma)
    {
        var returns = new double[rewards.Length];
        double runningReturn = 0.0;

        // Compute returns backward (from last step to first)
        for (int t = rewards.Length - 1; t >= 0; t--)
        {
            runningReturn = rewards[t] + gamma * runningReturn;
            returns[t] = runningReturn;
        }

        return returns;
    }

    #endregion

    #region Mapping Helpers

    /// <summary>
    /// Maps EventType to action space index (0-32).
    /// Must match TransformerCpuController.MapIndexToEventType.
    /// </summary>
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
            _ => 0 // Default to RegisterWrite
        };
    }

    /// <summary>
    /// Maps RegisterId to action space index (0-7).
    /// Must match TransformerCpuController.MapIndexToRegisterId.
    /// </summary>
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

        // Dispose tensors in episode buffer
        foreach (var step in _episodeBuffer)
        {
            step.ActionLogits?.Dispose();
        }
        _episodeBuffer.Clear();
    }

    #endregion
}

/// <summary>
/// Represents a single step in an episode trajectory.
/// </summary>
internal record EpisodeStep(
    CpuStateVector State,
    Tensor ActionLogits, // Keep in computation graph for backprop
    double Reward);

/// <summary>
/// Metrics from a policy gradient training step.
/// </summary>
public record PolicyGradientMetrics(
    double PolicyLoss,
    double AverageReturn,
    int StepCount);
