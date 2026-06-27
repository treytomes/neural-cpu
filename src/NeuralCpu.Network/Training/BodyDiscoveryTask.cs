using NeuralCpu.Core;
using NeuralCpu.Core.Components;
using NeuralCpu.Core.Events;
using Microsoft.Extensions.Logging;

namespace NeuralCpu.Network.Training;

/// <summary>
/// Body Discovery task for Phase 5.5.2: Embodied Learning.
/// No explicit goal - network explores CPU state space and receives intrinsic rewards
/// for discovering event types and observing state changes.
/// Goal: Learn "body schema" - how control signals affect CPU state.
/// </summary>
public sealed class BodyDiscoveryTask
{
    #region Fields

    private readonly IEventBus _eventBus;
    private readonly Clock _clock;
    private readonly RegisterFile _registerFile;
    private readonly Stack _stack;
    private readonly Memory _memory;
    private readonly INetworkController _controller;
    private readonly StateVectorBuilder _stateVectorBuilder;
    private readonly IntrinsicRewardTracker _rewardTracker;
    private readonly ILogger<BodyDiscoveryTask> _logger;
    private readonly int _maxCyclesPerEpisode;

    private ulong[] _previousRegisters = new ulong[7]; // R0-R3, PC, SP, Flags
    private int _previousStackSize;

    #endregion

    #region Constructors

    public BodyDiscoveryTask(
        IEventBus eventBus,
        Clock clock,
        RegisterFile registerFile,
        Stack stack,
        Memory memory,
        INetworkController controller,
        StateVectorBuilder stateVectorBuilder,
        IntrinsicRewardTracker rewardTracker,
        ILogger<BodyDiscoveryTask> logger,
        int maxCyclesPerEpisode = 100)
    {
        _eventBus = eventBus;
        _clock = clock;
        _registerFile = registerFile;
        _stack = stack;
        _memory = memory;
        _controller = controller;
        _stateVectorBuilder = stateVectorBuilder;
        _rewardTracker = rewardTracker;
        _logger = logger;
        _maxCyclesPerEpisode = maxCyclesPerEpisode;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Runs a single body discovery episode.
    /// Network explores freely and receives intrinsic rewards for novel actions and state changes.
    /// </summary>
    public BodyDiscoveryResult RunEpisode()
    {
        // Reset CPU and controller
        _eventBus.Publish(new SystemReset());
        _controller.Reset();

        // Capture initial state
        CaptureCurrentState();

        double totalReward = 0.0;
        int cycleCount = 0;
        var eventTypesSeen = new HashSet<EventType>();

        // Exploration loop (no goal, just curiosity)
        while (cycleCount < _maxCyclesPerEpisode)
        {
            // Observe current state
            var state = _stateVectorBuilder.Build(_registerFile[RegisterId.PC]);

            // Network decides action
            var action = _controller.DecideNextAction(state);
            if (action == null)
            {
                _logger.LogWarning("Network returned null action at cycle {Cycle}", cycleCount);
                break;
            }

            // Decode event type for reward computation
            var encodedEvent = DecodeEventType(action);
            eventTypesSeen.Add(encodedEvent);

            // Publish action to CPU
            _eventBus.Publish(action);

            // Advance clock
            _clock.Step();
            cycleCount++;

            // Compute intrinsic reward based on action novelty
            double actionReward = _rewardTracker.ComputeReward(encodedEvent);

            // Bonus for state changes (register, stack, memory)
            double stateChangeReward = ComputeStateChangeReward();

            double stepReward = actionReward + stateChangeReward;
            totalReward += stepReward;

            // Capture new state for next comparison
            CaptureCurrentState();
        }

        var explorationStats = _rewardTracker.GetStats();

        _logger.LogInformation(
            "Body discovery episode complete: {Cycles} cycles, {TotalReward:F3} reward, {DiscoveredTypes}/{TotalTypes} event types, {Coverage:P1} coverage",
            cycleCount, totalReward, explorationStats.DiscoveredEventTypes, explorationStats.TotalEventTypeOptions, explorationStats.Coverage);

        return new BodyDiscoveryResult(
            CycleCount: cycleCount,
            TotalReward: totalReward,
            UniqueEventTypes: eventTypesSeen.Count,
            ExplorationStats: explorationStats);
    }

    /// <summary>
    /// Captures current CPU state for state-change reward computation.
    /// </summary>
    private void CaptureCurrentState()
    {
        _previousRegisters[0] = _registerFile[RegisterId.R0];
        _previousRegisters[1] = _registerFile[RegisterId.R1];
        _previousRegisters[2] = _registerFile[RegisterId.R2];
        _previousRegisters[3] = _registerFile[RegisterId.R3];
        _previousRegisters[4] = _registerFile[RegisterId.PC];
        _previousRegisters[5] = _registerFile[RegisterId.SP];
        _previousRegisters[6] = _registerFile[RegisterId.Flags];
        _previousStackSize = (int)_registerFile[RegisterId.SP]; // SP tracks stack size
    }

    private ulong GetRegisterValue(int index)
    {
        return index switch
        {
            0 => _registerFile[RegisterId.R0],
            1 => _registerFile[RegisterId.R1],
            2 => _registerFile[RegisterId.R2],
            3 => _registerFile[RegisterId.R3],
            4 => _registerFile[RegisterId.PC],
            5 => _registerFile[RegisterId.SP],
            6 => _registerFile[RegisterId.Flags],
            _ => 0
        };
    }

    /// <summary>
    /// Computes reward based on observable state changes.
    /// Small bonus for changing registers, stack, or memory.
    /// </summary>
    private double ComputeStateChangeReward()
    {
        double reward = 0.0;

        // Register changes (+0.01 per changed register)
        for (int i = 0; i < 7; i++)
        {
            var currentValue = GetRegisterValue(i);

            if (currentValue != _previousRegisters[i])
            {
                reward += 0.01; // Small bonus for state change
            }
        }

        // Stack size changes (+0.02 for growth/shrink)
        int currentStackSize = (int)_registerFile[RegisterId.SP];
        if (currentStackSize != _previousStackSize)
        {
            reward += 0.02;
        }

        return reward;
    }

    /// <summary>
    /// Extracts EventType from CPU event object.
    /// </summary>
    private static EventType DecodeEventType(object cpuEvent)
    {
        return cpuEvent switch
        {
            RegisterWriteCommand => EventType.RegisterWrite,
            RegisterCopyCommand => EventType.RegisterCopy,
            AluCommand cmd => MapAluOpToEventType(cmd.Operation, isBinary: true),
            AluUnaryCommand cmd => MapAluOpToEventType(cmd.Operation, isBinary: false),
            PushCommand => EventType.Push,
            PushImmediateCommand => EventType.PushImmediate,
            PopCommand => EventType.Pop,
            PeekCommand => EventType.Peek,
            DupCommand => EventType.Dup,
            LoadCommand => EventType.LoadDirect,
            LoadIndirectCommand => EventType.LoadIndirect,
            StoreCommand => EventType.StoreDirect,
            StoreIndirectCommand => EventType.StoreIndirect,
            InputReadRequest => EventType.InputRead,
            OutputWriteCommand => EventType.OutputWrite,
            HardwareInterrupt => EventType.HardwareInterrupt,
            _ => EventType.None
        };
    }

    private static EventType MapAluOpToEventType(AluOp operation, bool isBinary)
    {
        return operation switch
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
            _ => EventType.None
        };
    }

    #endregion
}

/// <summary>
/// Result of a body discovery episode.
/// </summary>
public record BodyDiscoveryResult(
    int CycleCount,
    double TotalReward,
    int UniqueEventTypes,
    ExplorationStats ExplorationStats);
