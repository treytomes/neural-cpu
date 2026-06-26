using NeuralCpu.Core.Components;
using NeuralCpu.Network.Neural;
using TorchSharp;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace NeuralCpu.Network.Training;

/// <summary>
/// Implements Direct Preference Optimization (DPO) training for the neural CPU controller.
/// Trains the network to prefer valid events over invalid ones by maximizing the
/// log probability ratio between corrected (preferred) and invalid (rejected) events.
/// </summary>
public sealed class DPOTrainer : IDisposable
{
    #region Fields

    private readonly TransformerCpuController _controller;
    private readonly double _beta;           // DPO temperature (strength of preference)
    private readonly double _lambdaDpo;      // Weight for DPO loss
    private readonly Device _device;
    private readonly optim.Optimizer _optimizer;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a DPO trainer for the neural controller.
    /// </summary>
    /// <param name="controller">The transformer controller to train.</param>
    /// <param name="beta">DPO temperature (default 0.1). Higher = stronger preference for valid events.</param>
    /// <param name="lambdaDpo">Weight for DPO loss (default 1.0).</param>
    /// <param name="learningRate">Learning rate for optimizer (default 1e-4).</param>
    /// <param name="device">Torch device (CPU or CUDA).</param>
    public DPOTrainer(
        TransformerCpuController controller,
        double beta = 0.1,
        double lambdaDpo = 1.0,
        double learningRate = 1e-4,
        Device? device = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _beta = beta;
        _lambdaDpo = lambdaDpo;
        _device = device ?? CPU;

        // Create Adam optimizer for controller parameters
        _optimizer = optim.Adam(_controller.parameters(), lr: learningRate);
    }

    #endregion

    #region Training

    /// <summary>
    /// Performs a single DPO training step.
    /// </summary>
    /// <param name="state">Current CPU state.</param>
    /// <returns>Training metrics (loss, log probs).</returns>
    public DPOMetrics TrainStep(CpuStateVector state)
    {
        // Zero gradients
        _optimizer.zero_grad();

        // Get action logits from network
        var actionLogits = _controller.GetActionLogits(state);

        // Decode to EncodedEvent
        var generatedEvent = _controller.DecodeEncodedEvent(actionLogits);

        // Validate event
        var validation = EventValidationDPO.Validate(generatedEvent);

        // If invalid, create corrected version for DPO
        EncodedEvent preferredEvent;
        EncodedEvent rejectedEvent;

        if (!validation.IsValid)
        {
            // Preferred: corrected version (valid)
            // Rejected: original version (invalid)
            preferredEvent = EventValidationDPO.Correct(generatedEvent);
            rejectedEvent = generatedEvent;
        }
        else
        {
            // Already valid - no DPO update needed
            // Use same event for both (zero loss)
            preferredEvent = generatedEvent;
            rejectedEvent = generatedEvent;
        }

        // Compute DPO loss (as tensor with gradients)
        var lossTensor = ComputeDPOLossTensor(actionLogits, preferredEvent, rejectedEvent);
        var lossValue = lossTensor.item<float>();

        // Backward pass
        lossTensor.backward();

        // Update parameters
        _optimizer.step();

        // Return metrics (compute log probs for reporting)
        var logProbPreferred = ComputeLogProb(actionLogits.detach(), preferredEvent);
        var logProbRejected = ComputeLogProb(actionLogits.detach(), rejectedEvent);

        return new DPOMetrics(
            Loss: lossValue,
            LogProbPreferred: logProbPreferred,
            LogProbRejected: logProbRejected,
            WasInvalid: !validation.IsValid,
            ErrorCount: validation.Errors.Count);
    }

    #endregion

    #region DPO Algorithm

    /// <summary>
    /// Computes DPO loss tensor with gradients (for backpropagation).
    /// </summary>
    private Tensor ComputeDPOLossTensor(Tensor actionLogits, EncodedEvent preferredEvent, EncodedEvent rejectedEvent)
    {
        // Compute log probabilities as tensors (with gradients)
        var logProbPreferred = ComputeLogProbTensor(actionLogits, preferredEvent);
        var logProbRejected = ComputeLogProbTensor(actionLogits, rejectedEvent);

        // DPO loss: -log(sigmoid(beta * (log_prob_preferred - log_prob_rejected)))
        var logRatio = logProbPreferred - logProbRejected;
        var scaledRatio = logRatio * _beta;

        // -log_sigmoid(x) = softplus(-x)
        var loss = functional.softplus(-scaledRatio);

        return loss * _lambdaDpo;
    }

    /// <summary>
    /// Computes DPO loss (scalar, for metrics/testing).
    /// This encourages the network to assign higher probability to preferred events.
    /// </summary>
    public double ComputeDPOLoss(Tensor actionLogits, EncodedEvent preferredEvent, EncodedEvent rejectedEvent)
    {
        using var logits = actionLogits.detach();
        var lossTensor = ComputeDPOLossTensor(logits, preferredEvent, rejectedEvent);
        return lossTensor.item<float>();
    }

    /// <summary>
    /// Computes log probability tensor with gradients (for backpropagation).
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

    /// <summary>
    /// Computes log probability of an event given action logits (scalar, for metrics/testing).
    /// Log prob = sum of log probs for each field (event type, registers).
    /// </summary>
    public double ComputeLogProb(Tensor actionLogits, EncodedEvent evt)
    {
        using var logits = actionLogits.detach();
        var logProbTensor = ComputeLogProbTensor(logits, evt);
        return logProbTensor.item<float>();
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
    }

    #endregion
}

/// <summary>
/// Metrics from a single DPO training step.
/// </summary>
/// <param name="Loss">DPO loss value.</param>
/// <param name="LogProbPreferred">Log probability of preferred (valid) event.</param>
/// <param name="LogProbRejected">Log probability of rejected (invalid) event.</param>
/// <param name="WasInvalid">True if generated event was invalid (DPO update occurred).</param>
/// <param name="ErrorCount">Number of validation errors in generated event.</param>
public record DPOMetrics(
    double Loss,
    double LogProbPreferred,
    double LogProbRejected,
    bool WasInvalid,
    int ErrorCount);
