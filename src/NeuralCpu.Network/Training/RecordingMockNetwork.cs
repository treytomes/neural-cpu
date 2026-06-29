using NeuralCpu.Core.Components;
using NeuralCpu.Core.Events;
using NeuralCpu.Network.Neural;
using TorchSharp;

namespace NeuralCpu.Network.Training;

/// <summary>
/// Wraps CycleMockNetwork to record expert demonstrations.
/// Captures (state vector, encoded action) pairs during execution for behavior cloning training.
/// </summary>
public sealed class RecordingMockNetwork : INetworkController
{
    #region Fields

    private readonly CycleMockNetwork _expert;
    private readonly TransformerCpuController _encoder;
    private readonly List<(CpuStateVector state, EncodedEvent action)> _recordings = new();

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a recording wrapper around CycleMockNetwork.
    /// </summary>
    /// <param name="expert">The expert controller to record.</param>
    /// <param name="encoder">The transformer controller for state encoding.</param>
    public RecordingMockNetwork(CycleMockNetwork expert, TransformerCpuController encoder)
    {
        _expert = expert ?? throw new ArgumentNullException(nameof(expert));
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
    }

    #endregion

    #region INetworkController

    public object DecideNextAction(CpuStateVector state)
    {
        // Get expert's decision
        var action = _expert.DecideNextAction(state);

        // Record (state vector, encoded action) pair if action is not null
        if (action != null)
        {
            var encodedAction = EncodeAction(action);
            _recordings.Add((state, encodedAction));
        }

        return action;
    }

    public void OnEpisodeEnd(bool success, double reward)
    {
        // Pass through to expert (though CycleMockNetwork doesn't use this)
        _expert.OnEpisodeEnd(success, reward);
    }

    public void Reset()
    {
        _recordings.Clear();
        _expert.Reset();
    }

    #endregion

    #region Demonstration Extraction

    /// <summary>
    /// Extracts all recorded demonstrations as (state vector, encoded action) pairs.
    /// </summary>
    public List<Demonstration> GetDemonstrations()
    {
        return _recordings.Select(r => new Demonstration(r.state, r.action)).ToList();
    }

    /// <summary>
    /// Clears all recorded demonstrations without resetting the expert.
    /// </summary>
    public void ClearRecordings()
    {
        _recordings.Clear();
    }

    #endregion

    #region Action Encoding

    /// <summary>
    /// Converts CPU command objects to EncodedEvent format.
    /// </summary>
    private EncodedEvent EncodeAction(object action)
    {
        return action switch
        {
            RegisterWriteCommand cmd => new EncodedEvent(
                EventType.RegisterWrite,
                cmd.RegisterId,
                RegisterId.None,
                RegisterId.None,
                0,
                cmd.Value),

            RegisterCopyCommand cmd => new EncodedEvent(
                EventType.RegisterCopy,
                cmd.SourceRegister,
                cmd.DestinationRegister,
                RegisterId.None,
                0,
                0),

            AluCommand cmd => EncodeAluCommand(cmd),

            PushCommand cmd => new EncodedEvent(
                EventType.Push,
                cmd.Source,
                RegisterId.None,
                RegisterId.None,
                0,
                0),

            PushImmediateCommand cmd => new EncodedEvent(
                EventType.PushImmediate,
                RegisterId.None,
                RegisterId.None,
                RegisterId.None,
                0,
                cmd.Value),

            PopCommand cmd => new EncodedEvent(
                EventType.Pop,
                cmd.Destination,
                RegisterId.None,
                RegisterId.None,
                0,
                0),

            PeekCommand cmd => new EncodedEvent(
                EventType.Peek,
                cmd.Destination,
                RegisterId.None,
                RegisterId.None,
                0,
                0),

            DupCommand => new EncodedEvent(
                EventType.Dup,
                RegisterId.None,
                RegisterId.None,
                RegisterId.None,
                0,
                0),

            LoadCommand cmd => new EncodedEvent(
                EventType.LoadDirect,
                cmd.Destination,
                RegisterId.None,
                RegisterId.None,
                0,
                cmd.Address),

            LoadIndirectCommand cmd => new EncodedEvent(
                EventType.LoadIndirect,
                cmd.AddressRegister,
                cmd.Destination,
                RegisterId.None,
                0,
                0),

            StoreCommand cmd => new EncodedEvent(
                EventType.StoreDirect,
                cmd.Source,
                RegisterId.None,
                RegisterId.None,
                0,
                cmd.Address),

            StoreIndirectCommand cmd => new EncodedEvent(
                EventType.StoreIndirect,
                cmd.AddressRegister,
                cmd.Source,
                RegisterId.None,
                0,
                0),

            HardwareInterrupt cmd => new EncodedEvent(
                EventType.HardwareInterrupt,
                RegisterId.None,
                RegisterId.None,
                RegisterId.None,
                0,
                cmd.Vector),

            _ => throw new NotSupportedException($"Cannot encode action type: {action.GetType().Name}")
        };
    }

    /// <summary>
    /// Encodes ALU command with operation-specific event type.
    /// </summary>
    private EncodedEvent EncodeAluCommand(AluCommand cmd)
    {
        var eventType = cmd.Operation switch
        {
            AluOp.Add => EventType.AluAdd,
            AluOp.Sub => EventType.AluSub,
            AluOp.Mul => EventType.AluMul,
            AluOp.Div => EventType.AluDiv,
            AluOp.And => EventType.AluAnd,
            AluOp.Nand => EventType.AluNand,
            AluOp.Or => EventType.AluOr,
            AluOp.Nor => EventType.AluNor,
            AluOp.Xor => EventType.AluXor,
            AluOp.Xnor => EventType.AluXnor,
            AluOp.Shl => EventType.AluShl,
            AluOp.Shr => EventType.AluShr,
            AluOp.Sar => EventType.AluSar,
            AluOp.Rol => EventType.AluRol,
            AluOp.Ror => EventType.AluRor,
            AluOp.Not => EventType.AluNot,
            AluOp.Inc => EventType.AluInc,
            AluOp.Dec => EventType.AluDec,
            AluOp.Neg => EventType.AluNeg,
            AluOp.Abs => EventType.AluAbs,
            AluOp.Clz => EventType.AluClz,
            _ => throw new NotSupportedException($"ALU operation not mapped: {cmd.Operation}")
        };

        return new EncodedEvent(eventType, cmd.SourceA, cmd.SourceB, cmd.Destination, 0, 0);
    }

    #endregion
}
