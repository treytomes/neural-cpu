using NeuralCpu.Core.Components;
using NeuralCpu.Core.Events;
using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace NeuralCpu.Network.Neural;

/// <summary>
/// Transformer-based CPU controller with dual-attention mechanism.
///
/// Architecture:
/// 1. Event Encoder: Transforms event history into embeddings with positional encoding
/// 2. CPU State Encoder: Transforms CPU state (registers, stack, memory) into embeddings
/// 3. Transformer Layers: Self-attention within each stream
/// 4. Dual Attention: Cross-attention between events and CPU state
/// 5. MoE Router: Routes fused representation to domain experts
/// 6. Action Decoder: Generates control signals
/// </summary>
public sealed class TransformerCpuController : Module<Tensor, Tensor>, INetworkController, IDisposable
{
    #region Fields

    private readonly Device _device;
    private readonly long _embeddingDim;
    private readonly bool _isTraining;

    // Event encoding
    private readonly Linear _eventEmbedding;
    private readonly PositionalEncoding _eventPositionalEncoding;
    private readonly ModuleList<TransformerEncoderBlock> _eventEncoders;

    // CPU state encoding
    private readonly Linear _cpuStateEmbedding;
    private readonly PositionalEncoding _cpuPositionalEncoding;
    private readonly ModuleList<TransformerEncoderBlock> _cpuEncoders;

    // Dual attention
    private readonly DualAttentionBlock _dualAttention;

    // MoE routing and experts
    private readonly MixtureOfExpertsNetwork _moeNetwork;

    // Action decoding
    private readonly Linear _actionDecoder;

    private readonly List<(Tensor state, Tensor action, double reward)> _episodeBuffer;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a transformer-based CPU controller.
    /// </summary>
    /// <param name="embeddingDim">Embedding dimension for transformer.</param>
    /// <param name="numHeads">Number of attention heads.</param>
    /// <param name="numEncoderLayers">Number of transformer encoder layers for each stream.</param>
    /// <param name="moeNetwork">Mixture of Experts network for domain specialization.</param>
    /// <param name="actionSpaceSize">Size of action space (number of possible CPU events).</param>
    /// <param name="device">Torch device (CPU or CUDA).</param>
    /// <param name="isTraining">Whether in training mode.</param>
    public TransformerCpuController(
        long embeddingDim = 256,
        int numHeads = 8,
        int numEncoderLayers = 4,
        MixtureOfExpertsNetwork? moeNetwork = null,
        long actionSpaceSize = 100,
        Device? device = null,
        bool isTraining = true)
        : base("TransformerCpuController")
    {
        _embeddingDim = embeddingDim;
        _device = device ?? CPU;
        _isTraining = isTraining;
        _episodeBuffer = new List<(Tensor, Tensor, double)>();

        // Event encoding: 3 fields per event (type, reg, value) → embedding_dim
        _eventEmbedding = Linear(3, embeddingDim);
        _eventPositionalEncoding = new PositionalEncoding(embeddingDim, maxSequenceLength: 64, dropoutRate: 0.1);

        // Event transformer stack
        _eventEncoders = ModuleList<TransformerEncoderBlock>();
        for (int i = 0; i < numEncoderLayers; i++)
        {
            _eventEncoders.Add(new TransformerEncoderBlock(
                $"EventEncoder{i}",
                embeddingDim,
                numHeads,
                ffnHiddenDim: embeddingDim * 4,
                dropoutRate: 0.1));
        }

        // CPU state encoding: registers (7) + stack (8) + memory (16) = 31 elements → embedding_dim
        _cpuStateEmbedding = Linear(1, embeddingDim);  // Each scalar becomes an embedding
        _cpuPositionalEncoding = new PositionalEncoding(embeddingDim, maxSequenceLength: 32, dropoutRate: 0.1);

        // CPU state transformer stack
        _cpuEncoders = ModuleList<TransformerEncoderBlock>();
        for (int i = 0; i < numEncoderLayers; i++)
        {
            _cpuEncoders.Add(new TransformerEncoderBlock(
                $"CpuEncoder{i}",
                embeddingDim,
                numHeads,
                ffnHiddenDim: embeddingDim * 4,
                dropoutRate: 0.1));
        }

        // Dual attention between event stream and CPU state
        _dualAttention = new DualAttentionBlock("DualAttention", embeddingDim, numHeads, dropoutRate: 0.1);

        // MoE network (or create default)
        _moeNetwork = moeNetwork ?? new MoENetworkBuilder()
            .WithInputDim(embeddingDim)
            .WithHiddenDim(embeddingDim)
            .WithOutputDim(embeddingDim)
            .WithStandardExperts()
            .WithExpertDepth(2)
            .WithTopK(2)
            .Build();

        // Action decoder: embedding_dim → action_space_size
        _actionDecoder = Linear(embeddingDim, actionSpaceSize);

        RegisterComponents();

        if (_isTraining)
        {
            train();
        }
        else
        {
            eval();
        }
    }

    #endregion

    #region Properties

    public bool IsTraining => _isTraining;

    #endregion

    #region Methods

    /// <summary>
    /// Encodes event history into embeddings.
    /// </summary>
    private Tensor EncodeEvents(CpuStateVector state)
    {
        // Convert events to tensor: (batch=1, seq_len=64, features=3)
        var eventFeatures = new List<float>();
        foreach (var evt in state.EventHistory)
        {
            eventFeatures.Add((int)evt.Type / 100.0f);
            eventFeatures.Add((int)evt.Reg1 / 10.0f);
            eventFeatures.Add(evt.Value / (float)ulong.MaxValue);
        }

        var eventTensor = tensor(eventFeatures.ToArray(), device: _device)
            .view(1, 64, 3);  // Reshape to (batch=1, seq_len=64, features=3)

        // Embed events
        var embedded = _eventEmbedding.forward(eventTensor);  // (1, 64, embedding_dim)

        // Add positional encoding
        embedded = _eventPositionalEncoding.forward(embedded);

        // Pass through transformer encoders
        foreach (var encoder in _eventEncoders)
        {
            embedded = encoder.forward(embedded);
        }

        return embedded;
    }

    /// <summary>
    /// Encodes CPU state into embeddings.
    /// </summary>
    private Tensor EncodeCpuState(CpuStateVector state)
    {
        // Flatten CPU state: registers (7) + stack (8) + memory values (16) = 31 scalars
        var stateScalars = new List<float>();

        // Registers
        foreach (var reg in state.Registers)
        {
            stateScalars.Add(reg / (float)ulong.MaxValue);
        }

        // Stack
        foreach (var item in state.StackSnapshot)
        {
            stateScalars.Add(item / (float)ulong.MaxValue);
        }

        // Memory
        foreach (var item in state.MemoryValues)
        {
            stateScalars.Add(item / (float)ulong.MaxValue);
        }

        // Reshape to (1, 31, 1) - each scalar is a "token"
        var cpuTensor = tensor(stateScalars.ToArray(), device: _device)
            .unsqueeze(0)   // Add batch dimension
            .unsqueeze(-1); // Add feature dimension

        // Embed each scalar
        var embedded = _cpuStateEmbedding.forward(cpuTensor);  // (1, 31, embedding_dim)

        // Add positional encoding
        embedded = _cpuPositionalEncoding.forward(embedded);

        // Pass through transformer encoders
        foreach (var encoder in _cpuEncoders)
        {
            embedded = encoder.forward(embedded);
        }

        return embedded;
    }

    public object DecideNextAction(CpuStateVector state)
    {
        // Encode event history and CPU state separately
        var eventEmbeddings = EncodeEvents(state);      // (1, 64, embedding_dim)
        var cpuEmbeddings = EncodeCpuState(state);      // (1, 31, embedding_dim)

        // Dual attention: events ↔ CPU state
        Tensor fusedRepresentation;
        if (_isTraining)
        {
            (_, _, fusedRepresentation) = _dualAttention.Forward(eventEmbeddings, cpuEmbeddings);
        }
        else
        {
            using (no_grad())
            {
                (_, _, fusedRepresentation) = _dualAttention.Forward(eventEmbeddings, cpuEmbeddings);
            }
        }

        // Route through MoE experts
        var expertOutput = _moeNetwork.forward(fusedRepresentation);  // (1, embedding_dim)

        // Decode to action logits
        var actionLogits = _actionDecoder.forward(expertOutput);  // (1, action_space_size)

        // Sample or greedy action
        var action = _isTraining
            ? SampleAction(actionLogits)
            : GreedyAction(actionLogits);

        // Store for training
        if (_isTraining)
        {
            _episodeBuffer.Add((fusedRepresentation.detach(), action.detach(), 0.0));
        }

        // Decode action to CPU event
        return DecodeAction(action);
    }

    public void OnEpisodeEnd(bool success, double reward)
    {
        if (!_isTraining || _episodeBuffer.Count == 0)
            return;

        // Assign reward to last action
        var lastIndex = _episodeBuffer.Count - 1;
        var (state, action, _) = _episodeBuffer[lastIndex];
        _episodeBuffer[lastIndex] = (state, action, reward);

        // Training loop will handle updates externally
    }

    public void Reset()
    {
        foreach (var (state, action, _) in _episodeBuffer)
        {
            state.Dispose();
            action.Dispose();
        }
        _episodeBuffer.Clear();
    }

    public IReadOnlyList<(Tensor state, Tensor action, double reward)> GetEpisodeBuffer()
    {
        return _episodeBuffer.AsReadOnly();
    }

    private Tensor SampleAction(Tensor logits)
    {
        var probs = functional.softmax(logits, dim: -1);
        return multinomial(probs, num_samples: 1);
    }

    private Tensor GreedyAction(Tensor logits)
    {
        return logits.argmax(dim: -1, keepdim: true);
    }

    private object DecodeAction(Tensor actionTensor)
    {
        // TODO: Implement proper action space encoding
        var actionIndex = actionTensor.item<long>();
        return new RegisterWriteCommand(RegisterId.R0, (ulong)actionIndex);
    }

    /// <summary>
    /// Saves the entire controller (including MoE network) to a file.
    /// </summary>
    public void Save(string filePath)
    {
        this.save(filePath);
    }

    /// <summary>
    /// Loads the entire controller state from a file.
    /// </summary>
    public void Load(string filePath)
    {
        this.load(filePath);
    }

    public override Tensor forward(Tensor input)
    {
        // For Module<Tensor, Tensor> compatibility, but this controller uses DecideNextAction
        throw new NotSupportedException(
            "TransformerCpuController should use DecideNextAction(CpuStateVector) instead of forward(Tensor)");
    }

    public new void Dispose()
    {
        Reset();
        base.Dispose();
    }

    #endregion
}
