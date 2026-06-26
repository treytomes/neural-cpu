using NeuralCpu.Core.Components;
using NeuralCpu.Core.Events;
using TorchSharp;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace NeuralCpu.Network.Neural;

/// <summary>
/// Neural network controller that learns to control the CPU through reinforcement learning.
/// Uses a Mixture of Experts architecture to map state vectors to control events.
/// </summary>
public sealed class NeuralNetworkController : INetworkController, IDisposable
{
    #region Fields

    private readonly MixtureOfExpertsNetwork _network;
    private readonly Device _device;
    private readonly List<(Tensor state, Tensor action, double reward)> _episodeBuffer;
    private bool _isTraining;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a neural network controller.
    /// </summary>
    /// <param name="network">The MoE network to use for control.</param>
    /// <param name="device">Torch device (CPU or CUDA).</param>
    /// <param name="isTraining">Whether the network is in training mode.</param>
    public NeuralNetworkController(MixtureOfExpertsNetwork network, Device? device = null, bool isTraining = true)
    {
        _network = network ?? throw new ArgumentNullException(nameof(network));
        _device = device ?? torch.CPU;
        _isTraining = isTraining;
        _episodeBuffer = new List<(Tensor, Tensor, double)>();

        if (_isTraining)
        {
            _network.train();
        }
        else
        {
            _network.eval();
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets whether the controller is in training mode.
    /// </summary>
    public bool IsTraining => _isTraining;

    /// <summary>
    /// Gets the underlying MoE network.
    /// </summary>
    public MixtureOfExpertsNetwork Network => _network;

    #endregion

    #region Methods

    /// <summary>
    /// Sets training mode.
    /// </summary>
    public void SetTraining(bool enabled)
    {
        _isTraining = enabled;
        if (enabled)
        {
            _network.train();
        }
        else
        {
            _network.eval();
        }
    }

    public object DecideNextAction(CpuStateVector state)
    {
        // Convert state vector to tensor
        var stateTensor = StateVectorToTensor(state);

        // Forward pass through network
        Tensor actionLogits;
        if (_isTraining)
        {
            actionLogits = _network.forward(stateTensor);
        }
        else
        {
            using (no_grad())
            {
                actionLogits = _network.forward(stateTensor);
            }
        }

        // Sample action from logits (or use argmax for evaluation)
        var action = _isTraining
            ? SampleAction(actionLogits)
            : GreedyAction(actionLogits);

        // Store state-action pair for training
        if (_isTraining)
        {
            _episodeBuffer.Add((stateTensor.detach(), action.detach(), 0.0));
        }

        // Decode action tensor to CPU event
        var cpuEvent = DecodeAction(action);

        return cpuEvent;
    }

    public void OnEpisodeEnd(bool success, double reward)
    {
        if (!_isTraining || _episodeBuffer.Count == 0)
            return;

        // Assign reward to last action (simple credit assignment)
        // More sophisticated: discounted reward propagation
        var lastIndex = _episodeBuffer.Count - 1;
        var (state, action, _) = _episodeBuffer[lastIndex];
        _episodeBuffer[lastIndex] = (state, action, reward);

        // TODO: Implement policy gradient update here
        // For now, just clear the buffer (training loop will be external)
        _episodeBuffer.Clear();
    }

    public void Reset()
    {
        _episodeBuffer.Clear();
    }

    /// <summary>
    /// Gets the episode buffer for external training.
    /// </summary>
    public IReadOnlyList<(Tensor state, Tensor action, double reward)> GetEpisodeBuffer()
    {
        return _episodeBuffer.AsReadOnly();
    }

    /// <summary>
    /// Clears the episode buffer.
    /// </summary>
    public void ClearEpisodeBuffer()
    {
        _episodeBuffer.Clear();
    }

    private Tensor StateVectorToTensor(CpuStateVector state)
    {
        // Flatten state vector into 1D tensor
        var values = new List<float>();

        // Registers (7 registers, normalized to [0, 1])
        foreach (var reg in state.Registers)
        {
            values.Add(reg / (float)ulong.MaxValue);
        }

        // Event history (64 events × 3 fields: type, reg, value)
        foreach (var evt in state.EventHistory)
        {
            values.Add((int)evt.Type / 100.0f);  // Normalize event type
            values.Add((int)evt.Reg1 / 10.0f);   // Normalize register ID
            values.Add(evt.Value / (float)ulong.MaxValue);  // Normalize value
        }

        // Stack snapshot (8 items)
        foreach (var item in state.StackSnapshot)
        {
            values.Add(item / (float)ulong.MaxValue);
        }

        // Memory values (16 items)
        foreach (var item in state.MemoryValues)
        {
            values.Add(item / (float)ulong.MaxValue);
        }

        // Cycle count (normalize to reasonable range)
        values.Add(state.CycleCount / 1000.0f);

        return tensor(values.ToArray(), device: _device).unsqueeze(0);  // Add batch dimension
    }

    private Tensor SampleAction(Tensor logits)
    {
        // Sample from categorical distribution (exploration)
        var probs = functional.softmax(logits, dim: -1);
        var action = multinomial(probs, num_samples: 1);
        return action;
    }

    private Tensor GreedyAction(Tensor logits)
    {
        // Take highest probability action (exploitation)
        var action = logits.argmax(dim: -1, keepdim: true);
        return action;
    }

    private object DecodeAction(Tensor actionTensor)
    {
        // Convert action tensor (index) to CPU event
        var actionIndex = actionTensor.item<long>();

        // TODO: Implement proper action space encoding
        // For now, return a simple register write as placeholder
        // Action space should map to: RegisterWrite, ALU ops, Stack ops, Memory ops, etc.

        return new RegisterWriteCommand(RegisterId.R0, (ulong)actionIndex);
    }

    public void Dispose()
    {
        // Dispose episode buffer tensors
        foreach (var (state, action, _) in _episodeBuffer)
        {
            state.Dispose();
            action.Dispose();
        }
        _episodeBuffer.Clear();

        _network.Dispose();
    }

    #endregion
}
