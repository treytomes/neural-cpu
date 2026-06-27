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
    #region Constants

    // Action space dimensions (see TODO.md Phase 5.5.1)
    private const int EVENT_TYPE_DIM = 35;    // Actionable event types (33 + InputRead + OutputWrite)
    private const int REGISTER_DIM = 8;       // R0-R3, PC, SP, Flags, None
    private const int DEVICE_ID_DIM = 16;     // Device IDs 0-15
    private const int VALUE_EMBED_DIM = 256;  // Embedding for 64-bit value
    private const int ACTION_DIM = EVENT_TYPE_DIM + (REGISTER_DIM * 3) + DEVICE_ID_DIM + VALUE_EMBED_DIM; // 329

    #endregion

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
    private readonly Sequential _valueDecoder;

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

        // Action decoder: embedding_dim → ACTION_DIM
        _actionDecoder = Linear(embeddingDim, ACTION_DIM);

        // Value decoder: VALUE_EMBED_DIM → 64 bits
        _valueDecoder = Sequential(
            ("fc1", Linear(VALUE_EMBED_DIM, 128)),
            ("relu", ReLU()),
            ("fc2", Linear(128, 64)),
            ("sigmoid", Sigmoid()));

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

    /// <summary>
    /// Gets raw action logits from network output (for training/testing).
    /// </summary>
    public Tensor GetActionLogits(CpuStateVector state)
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
        return _actionDecoder.forward(expertOutput);  // (1, ACTION_DIM)
    }

    /// <summary>
    /// Decodes action logits tensor into EncodedEvent structure.
    /// </summary>
    public EncodedEvent DecodeEncodedEvent(Tensor actionLogits)
    {
        // actionLogits shape: (1, ACTION_DIM) where ACTION_DIM = 329
        // Layout: [event_type(35) | reg1(8) | reg2(8) | reg3(8) | deviceId(16) | value_embed(256)]

        var logitsFlat = actionLogits.squeeze(0); // (ACTION_DIM,)

        // Extract sections
        var eventTypeLogits = logitsFlat[TensorIndex.Slice(0, EVENT_TYPE_DIM)];
        var reg1Logits = logitsFlat[TensorIndex.Slice(EVENT_TYPE_DIM, EVENT_TYPE_DIM + REGISTER_DIM)];
        var reg2Logits = logitsFlat[TensorIndex.Slice(EVENT_TYPE_DIM + REGISTER_DIM, EVENT_TYPE_DIM + REGISTER_DIM * 2)];
        var reg3Logits = logitsFlat[TensorIndex.Slice(EVENT_TYPE_DIM + REGISTER_DIM * 2, EVENT_TYPE_DIM + REGISTER_DIM * 3)];
        var deviceIdLogits = logitsFlat[TensorIndex.Slice(EVENT_TYPE_DIM + REGISTER_DIM * 3, EVENT_TYPE_DIM + REGISTER_DIM * 3 + DEVICE_ID_DIM)];
        var valueEmbed = logitsFlat[TensorIndex.Slice(EVENT_TYPE_DIM + REGISTER_DIM * 3 + DEVICE_ID_DIM, ACTION_DIM)].unsqueeze(0);

        // Decode event type (argmax over actionable types)
        var eventTypeIndex = eventTypeLogits.argmax().item<long>();
        var eventType = MapIndexToEventType((int)eventTypeIndex);

        // Decode registers (argmax, then map from 0-7 to RegisterId including None)
        var reg1Index = reg1Logits.argmax().item<long>();
        var reg2Index = reg2Logits.argmax().item<long>();
        var reg3Index = reg3Logits.argmax().item<long>();

        var reg1 = MapIndexToRegisterId((int)reg1Index);
        var reg2 = MapIndexToRegisterId((int)reg2Index);
        var reg3 = MapIndexToRegisterId((int)reg3Index);

        // Decode device ID (argmax, 0-15)
        var deviceIdIndex = deviceIdLogits.argmax().item<long>();
        var deviceId = (byte)deviceIdIndex;

        // Decode value (embedding → 64 bits)
        var value = DecodeValue(valueEmbed);

        return new EncodedEvent(eventType, reg1, reg2, reg3, deviceId, value);
    }

    /// <summary>
    /// Decodes value embedding into 64-bit unsigned integer.
    /// </summary>
    private ulong DecodeValue(Tensor valueEmbed)
    {
        // valueEmbed shape: (1, VALUE_EMBED_DIM)
        // Pass through value decoder to get 64 binary predictions
        var bits = _valueDecoder.forward(valueEmbed).squeeze(0); // (64,)

        // Threshold at 0.5 and pack into ulong
        ulong result = 0;
        for (int i = 0; i < 64; i++)
        {
            float bitValue = bits[i].item<float>();
            if (bitValue >= 0.5f)
            {
                result |= (1UL << i);
            }
        }

        return result;
    }

    /// <summary>
    /// Converts EncodedEvent to actual CPU event object.
    /// </summary>
    public object? DecodeCpuEvent(EncodedEvent evt)
    {
        return evt.Type switch
        {
            EventType.None => null,

            // Register operations
            EventType.RegisterWrite => new RegisterWriteCommand(evt.Reg1, evt.Value),
            EventType.RegisterCopy => new RegisterCopyCommand(evt.Reg1, evt.Reg2),

            // ALU binary operations
            EventType.AluAdd => new AluCommand(AluOp.Add, evt.Reg1, evt.Reg2, evt.Reg3),
            EventType.AluSub => new AluCommand(AluOp.Sub, evt.Reg1, evt.Reg2, evt.Reg3),
            EventType.AluMul => new AluCommand(AluOp.Mul, evt.Reg1, evt.Reg2, evt.Reg3),
            EventType.AluDiv => new AluCommand(AluOp.Div, evt.Reg1, evt.Reg2, evt.Reg3),
            EventType.AluAnd => new AluCommand(AluOp.And, evt.Reg1, evt.Reg2, evt.Reg3),
            EventType.AluNand => new AluCommand(AluOp.Nand, evt.Reg1, evt.Reg2, evt.Reg3),
            EventType.AluOr => new AluCommand(AluOp.Or, evt.Reg1, evt.Reg2, evt.Reg3),
            EventType.AluNor => new AluCommand(AluOp.Nor, evt.Reg1, evt.Reg2, evt.Reg3),
            EventType.AluXor => new AluCommand(AluOp.Xor, evt.Reg1, evt.Reg2, evt.Reg3),
            EventType.AluXnor => new AluCommand(AluOp.Xnor, evt.Reg1, evt.Reg2, evt.Reg3),
            EventType.AluShl => new AluCommand(AluOp.Shl, evt.Reg1, evt.Reg2, evt.Reg3),
            EventType.AluShr => new AluCommand(AluOp.Shr, evt.Reg1, evt.Reg2, evt.Reg3),
            EventType.AluSar => new AluCommand(AluOp.Sar, evt.Reg1, evt.Reg2, evt.Reg3),
            EventType.AluRol => new AluCommand(AluOp.Rol, evt.Reg1, evt.Reg2, evt.Reg3),
            EventType.AluRor => new AluCommand(AluOp.Ror, evt.Reg1, evt.Reg2, evt.Reg3),

            // ALU unary operations
            EventType.AluNot => new AluUnaryCommand(AluOp.Not, evt.Reg1, evt.Reg2),
            EventType.AluInc => new AluUnaryCommand(AluOp.Inc, evt.Reg1, evt.Reg2),
            EventType.AluDec => new AluUnaryCommand(AluOp.Dec, evt.Reg1, evt.Reg2),
            EventType.AluNeg => new AluUnaryCommand(AluOp.Neg, evt.Reg1, evt.Reg2),
            EventType.AluAbs => new AluUnaryCommand(AluOp.Abs, evt.Reg1, evt.Reg2),
            EventType.AluClz => new AluUnaryCommand(AluOp.Clz, evt.Reg1, evt.Reg2),

            // Stack operations
            EventType.Push => new PushCommand(evt.Reg1),
            EventType.PushImmediate => new PushImmediateCommand(evt.Value),
            EventType.Pop => new PopCommand(evt.Reg1),
            EventType.Peek => new PeekCommand(evt.Reg1),
            EventType.Dup => new DupCommand(),

            // Memory operations
            EventType.LoadDirect => new LoadCommand(evt.Value, evt.Reg1),
            EventType.LoadIndirect => new LoadIndirectCommand(evt.Reg1, evt.Reg2),
            EventType.StoreDirect => new StoreCommand(evt.Value, evt.Reg1),
            EventType.StoreIndirect => new StoreIndirectCommand(evt.Reg1, evt.Reg2),

            // Device I/O operations
            EventType.InputRead => new InputReadRequest(evt.DeviceId, DateTimeOffset.Now),
            EventType.OutputWrite => new OutputWriteCommand(evt.DeviceId, evt.Value, DateTimeOffset.Now),

            // Hardware interrupt
            EventType.HardwareInterrupt => new HardwareInterrupt((byte)(evt.Value & 0xFF)),

            // Observe-only events (network shouldn't emit these)
            _ => null
        };
    }

    /// <summary>
    /// Maps action space index (0-34) to actionable EventType.
    /// </summary>
    private static EventType MapIndexToEventType(int index)
    {
        return index switch
        {
            0 => EventType.RegisterWrite,
            1 => EventType.RegisterCopy,
            2 => EventType.HardwareInterrupt,
            3 => EventType.AluAdd,
            4 => EventType.AluSub,
            5 => EventType.AluMul,
            6 => EventType.AluDiv,
            7 => EventType.AluAnd,
            8 => EventType.AluNand,
            9 => EventType.AluOr,
            10 => EventType.AluNor,
            11 => EventType.AluXor,
            12 => EventType.AluXnor,
            13 => EventType.AluShl,
            14 => EventType.AluShr,
            15 => EventType.AluSar,
            16 => EventType.AluRol,
            17 => EventType.AluRor,
            18 => EventType.AluNot,
            19 => EventType.AluInc,
            20 => EventType.AluDec,
            21 => EventType.AluNeg,
            22 => EventType.AluAbs,
            23 => EventType.AluClz,
            24 => EventType.Push,
            25 => EventType.PushImmediate,
            26 => EventType.Pop,
            27 => EventType.Peek,
            28 => EventType.Dup,
            29 => EventType.LoadDirect,
            30 => EventType.LoadIndirect,
            31 => EventType.StoreDirect,
            32 => EventType.StoreIndirect,
            33 => EventType.InputRead,
            34 => EventType.OutputWrite,
            _ => EventType.None
        };
    }

    /// <summary>
    /// Maps register index (0-7) to RegisterId enum (including None at index 0).
    /// </summary>
    private static RegisterId MapIndexToRegisterId(int index)
    {
        return index switch
        {
            0 => RegisterId.None,
            1 => RegisterId.R0,
            2 => RegisterId.R1,
            3 => RegisterId.R2,
            4 => RegisterId.R3,
            5 => RegisterId.PC,
            6 => RegisterId.SP,
            7 => RegisterId.Flags,
            _ => RegisterId.None
        };
    }

    public object DecideNextAction(CpuStateVector state)
    {
        // Get action logits from network
        var actionLogits = GetActionLogits(state);

        // Decode to EncodedEvent
        var encodedEvent = DecodeEncodedEvent(actionLogits);

        // Validate event
        var validation = EventValidationDPO.Validate(encodedEvent);

        // If invalid, correct it (for now, during development)
        // TODO: In DPO training, we'll use both invalid and corrected versions
        if (!validation.IsValid)
        {
            encodedEvent = EventValidationDPO.Correct(encodedEvent);
        }

        // Convert to CPU event object
        var cpuEvent = DecodeCpuEvent(encodedEvent);

        // Store for training (keep gradients for backpropagation)
        if (_isTraining)
        {
            // Store actionLogits WITH gradients (don't detach)
            // The computation graph will be retained for policy gradient training
            _episodeBuffer.Add((actionLogits.alias(), actionLogits.alias(), 0.0));
        }

        // Return CPU event (or null if invalid)
        return cpuEvent ?? new RegisterWriteCommand(RegisterId.R0, 0); // Fallback: no-op
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
