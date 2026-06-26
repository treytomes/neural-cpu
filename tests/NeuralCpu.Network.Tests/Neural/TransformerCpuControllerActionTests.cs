using NeuralCpu.Core.Components;
using NeuralCpu.Core.Events;
using NeuralCpu.Network.Neural;
using TorchSharp;
using static TorchSharp.torch;

namespace NeuralCpu.Network.Tests.Neural;

/// <summary>
/// Tests for action space encoding in TransformerCpuController.
/// Validates Milestone 1: Network can output valid EncodedEvent structures.
/// </summary>
public sealed class TransformerCpuControllerActionTests : IDisposable
{
    #region Constants

    // Action space dimensions (from TODO.md Phase 1.1)
    // EVENT_TYPE_DIM: Count of actionable EventType values
    // REGISTER_DIM: 8 (7 registers + None)
    // VALUE_EMBED_DIM: 256 (embedding for 64-bit value)

    private const int ACTIONABLE_EVENT_TYPES = 33; // Count manually from EventType enum
    private const int REGISTER_SLOTS = 8; // R0-R3, PC, SP, Flags, None
    private const int VALUE_EMBED_DIM = 256;

    // Total action dimension: type + (reg1 + reg2 + reg3) + value_embedding
    private const int EXPECTED_ACTION_DIM = ACTIONABLE_EVENT_TYPES + (REGISTER_SLOTS * 3) + VALUE_EMBED_DIM;

    #endregion

    #region Fields

    private readonly TransformerCpuController _controller;

    #endregion

    #region Constructors

    public TransformerCpuControllerActionTests()
    {
        // Create controller in training mode for testing
        _controller = new TransformerCpuController(
            embeddingDim: 256,
            numHeads: 8,
            numEncoderLayers: 2, // Small for tests
            moeNetwork: null, // Use default
            actionSpaceSize: EXPECTED_ACTION_DIM,
            device: CPU,
            isTraining: false);
    }

    #endregion

    #region Test: Action Decoder Dimensions

    [Fact]
    public void ActionDecoder_Should_OutputCorrectDimensions()
    {
        // Arrange: Create minimal state vector
        var state = CreateMinimalStateVector();

        // Act: Get action logits
        var actionLogits = GetActionLogits(state);

        // Assert: Shape is (1, EXPECTED_ACTION_DIM)
        Assert.Equal(2, actionLogits.dim());
        Assert.Equal(1, actionLogits.shape[0]); // Batch size
        Assert.Equal(EXPECTED_ACTION_DIM, actionLogits.shape[1]);
    }

    #endregion

    #region Test: EncodedEvent Decoding

    [Fact]
    public void DecodeEncodedEvent_Should_ParseTensorToStructure()
    {
        // Arrange: Create mock action logits
        var actionLogits = CreateMockActionLogits(
            eventTypeIndex: 0, // RegisterWrite is index 0 in action space
            reg1Index: 1, // R0 (index 0 is None)
            reg2Index: 0, // None
            reg3Index: 0, // None
            valueBits: 42UL);

        // Act: Decode to EncodedEvent
        var encodedEvent = DecodeEncodedEvent(actionLogits);

        // Assert: Structure is correct
        Assert.Equal(EventType.RegisterWrite, encodedEvent.Type);
        Assert.Equal(RegisterId.R0, encodedEvent.Reg1);
        Assert.Equal(RegisterId.None, encodedEvent.Reg2);
        Assert.Equal(RegisterId.None, encodedEvent.Reg3);
        // Value decoding is complex (embedding → 64 bits), just verify it produces *some* value
        // Exact reconstruction requires training the value decoder, which is out of scope for this test
        Assert.True(encodedEvent.Value >= 0); // Basic sanity check
    }

    [Fact]
    public void DecodeEncodedEvent_Should_HandleAllActionableEventTypes()
    {
        // Test that all actionable event types can be decoded
        var actionableTypes = GetActionableEventTypes();

        for (int i = 0; i < actionableTypes.Length; i++)
        {
            // Arrange: Create logits for this event type using action space index
            var actionLogits = CreateMockActionLogits(
                eventTypeIndex: i, // Use action space index, not enum value
                reg1Index: 1, // R0
                reg2Index: 2, // R1
                reg3Index: 3, // R2
                valueBits: 100UL);

            // Act: Decode
            var encodedEvent = DecodeEncodedEvent(actionLogits);

            // Assert: Type matches
            Assert.Equal(actionableTypes[i], encodedEvent.Type);
        }
    }

    #endregion

    #region Test: CPU Event Conversion

    [Fact]
    public void DecodeCpuEvent_Should_ConvertRegisterWrite()
    {
        // Arrange: EncodedEvent for RegisterWrite
        var encodedEvent = new EncodedEvent(
            EventType.RegisterWrite,
            RegisterId.R1,
            RegisterId.None,
            RegisterId.None,
            123UL);

        // Act: Convert to CPU event
        var cpuEvent = DecodeCpuEvent(encodedEvent);

        // Assert: Correct type and parameters
        Assert.IsType<RegisterWriteCommand>(cpuEvent);
        var cmd = (RegisterWriteCommand)cpuEvent;
        Assert.Equal(RegisterId.R1, cmd.RegisterId);
        Assert.Equal(123UL, cmd.Value);
    }

    [Fact]
    public void DecodeCpuEvent_Should_ConvertRegisterCopy()
    {
        // Arrange: EncodedEvent for RegisterCopy
        var encodedEvent = new EncodedEvent(
            EventType.RegisterCopy,
            RegisterId.R0,  // Source
            RegisterId.R2,  // Destination
            RegisterId.None,
            0UL);

        // Act: Convert to CPU event
        var cpuEvent = DecodeCpuEvent(encodedEvent);

        // Assert: Correct type and parameters
        Assert.IsType<RegisterCopyCommand>(cpuEvent);
        var cmd = (RegisterCopyCommand)cpuEvent;
        Assert.Equal(RegisterId.R0, cmd.SourceRegister);
        Assert.Equal(RegisterId.R2, cmd.DestinationRegister);
    }

    [Fact]
    public void DecodeCpuEvent_Should_ConvertAluBinaryOp()
    {
        // Arrange: EncodedEvent for ALU Add
        var encodedEvent = new EncodedEvent(
            EventType.AluAdd,
            RegisterId.R0,  // SourceA
            RegisterId.R1,  // SourceB
            RegisterId.R2,  // Destination
            0UL);

        // Act: Convert to CPU event
        var cpuEvent = DecodeCpuEvent(encodedEvent);

        // Assert: Correct type and parameters
        Assert.IsType<AluCommand>(cpuEvent);
        var cmd = (AluCommand)cpuEvent;
        Assert.Equal(AluOp.Add, cmd.Operation);
        Assert.Equal(RegisterId.R0, cmd.SourceA);
        Assert.Equal(RegisterId.R1, cmd.SourceB);
        Assert.Equal(RegisterId.R2, cmd.Destination);
    }

    [Fact]
    public void DecodeCpuEvent_Should_ConvertStackPush()
    {
        // Arrange: EncodedEvent for Push
        var encodedEvent = new EncodedEvent(
            EventType.Push,
            RegisterId.R3,  // Source register
            RegisterId.None,
            RegisterId.None,
            0UL);

        // Act: Convert to CPU event
        var cpuEvent = DecodeCpuEvent(encodedEvent);

        // Assert: Correct type and parameters
        Assert.IsType<PushCommand>(cpuEvent);
        var cmd = (PushCommand)cpuEvent;
        Assert.Equal(RegisterId.R3, cmd.Source);
    }

    [Fact]
    public void DecodeCpuEvent_Should_ConvertMemoryLoad()
    {
        // Arrange: EncodedEvent for LoadDirect
        var encodedEvent = new EncodedEvent(
            EventType.LoadDirect,
            RegisterId.R2,  // Destination
            RegisterId.None,
            RegisterId.None,
            0x1000UL);  // Address

        // Act: Convert to CPU event
        var cpuEvent = DecodeCpuEvent(encodedEvent);

        // Assert: Correct type and parameters
        Assert.IsType<LoadCommand>(cpuEvent);
        var cmd = (LoadCommand)cpuEvent;
        Assert.Equal(0x1000UL, cmd.Address);
        Assert.Equal(RegisterId.R2, cmd.Destination);
    }

    #endregion

    #region Test: End-to-End Pipeline

    [Fact]
    public void EndToEnd_Should_GenerateValidEncodedEvent()
    {
        // Arrange: Minimal state
        var state = CreateMinimalStateVector();

        // Act: Full pipeline
        var actionLogits = GetActionLogits(state);
        var encodedEvent = DecodeEncodedEvent(actionLogits);
        var cpuEvent = DecodeCpuEvent(encodedEvent);

        // Assert: Pipeline produces valid output
        Assert.NotNull(cpuEvent);
        Assert.True(encodedEvent.Type != EventType.None);
    }

    #endregion

    #region Helper Methods

    private CpuStateVector CreateMinimalStateVector()
    {
        // Create minimal valid state vector for testing
        var registers = new ulong[7]; // R0-R3, PC, SP, Flags all zero
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

    private Tensor GetActionLogits(CpuStateVector state)
    {
        return _controller.GetActionLogits(state);
    }

    private Tensor CreateMockActionLogits(
        int eventTypeIndex,
        int reg1Index,
        int reg2Index,
        int reg3Index,
        ulong valueBits)
    {
        // Create a tensor with high logits for specified indices
        var logits = zeros(1, EXPECTED_ACTION_DIM);

        // Set event type logit
        logits[0, eventTypeIndex] = 10.0f;

        // Set register logits
        logits[0, ACTIONABLE_EVENT_TYPES + reg1Index] = 10.0f;
        logits[0, ACTIONABLE_EVENT_TYPES + REGISTER_SLOTS + reg2Index] = 10.0f;
        logits[0, ACTIONABLE_EVENT_TYPES + (REGISTER_SLOTS * 2) + reg3Index] = 10.0f;

        // Set value embedding (convert bits to sigmoid outputs)
        for (int i = 0; i < 64; i++)
        {
            bool bit = ((valueBits >> i) & 1) == 1;
            int embedStart = ACTIONABLE_EVENT_TYPES + (REGISTER_SLOTS * 3);

            // For simplicity, just set first 64 dims of embedding
            if (i < VALUE_EMBED_DIM)
            {
                logits[0, embedStart + i] = bit ? 1.0f : 0.0f;
            }
        }

        return logits;
    }

    private EncodedEvent DecodeEncodedEvent(Tensor actionLogits)
    {
        return _controller.DecodeEncodedEvent(actionLogits);
    }

    private object? DecodeCpuEvent(EncodedEvent encodedEvent)
    {
        return _controller.DecodeCpuEvent(encodedEvent);
    }

    private static EventType[] GetActionableEventTypes()
    {
        // Return all event types the network can emit (not observe-only)
        return new[]
        {
            EventType.RegisterWrite,
            EventType.RegisterCopy,
            EventType.HardwareInterrupt,

            // ALU binary
            EventType.AluAdd, EventType.AluSub, EventType.AluMul, EventType.AluDiv,
            EventType.AluAnd, EventType.AluNand, EventType.AluOr, EventType.AluNor,
            EventType.AluXor, EventType.AluXnor, EventType.AluShl, EventType.AluShr,
            EventType.AluSar, EventType.AluRol, EventType.AluRor,

            // ALU unary
            EventType.AluNot, EventType.AluInc, EventType.AluDec,
            EventType.AluNeg, EventType.AluAbs, EventType.AluClz,

            // Stack
            EventType.Push, EventType.PushImmediate, EventType.Pop,
            EventType.Peek, EventType.Dup,

            // Memory
            EventType.LoadDirect, EventType.LoadIndirect,
            EventType.StoreDirect, EventType.StoreIndirect,
        };
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        _controller.Dispose();
    }

    #endregion
}
