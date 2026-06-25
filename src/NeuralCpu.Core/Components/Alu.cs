using Microsoft.Extensions.Logging;
using NeuralCpu.Core.Events;
using System.Collections.Concurrent;

namespace NeuralCpu.Core.Components;

/// <summary>
/// Arithmetic Logic Unit - performs arithmetic, bitwise, shift, and rotation operations.
/// All operations are single-cycle and update the Flags register.
/// Operations are queued and execute on the clock rising edge.
/// </summary>
public sealed class Alu : IObserver<ClockPhaseChanged>,
                          IObserver<AluCommand>,
                          IObserver<AluImmediateCommand>,
                          IObserver<AluUnaryCommand>,
                          IObserver<SystemReset>,
                          IDisposable
{
    #region Fields

    private readonly IEventBus _eventBus;
    private readonly RegisterFile _registerFile;
    private readonly ILogger<Alu> _logger;
    private readonly IDisposable _clockSubscription;
    private readonly IDisposable _aluSubscription;
    private readonly IDisposable _aluImmediateSubscription;
    private readonly IDisposable _aluUnarySubscription;
    private readonly IDisposable _resetSubscription;

    private readonly ConcurrentQueue<AluCommand> _pendingCommands = new();
    private readonly ConcurrentQueue<AluImmediateCommand> _pendingImmediateCommands = new();
    private readonly ConcurrentQueue<AluUnaryCommand> _pendingUnaryCommands = new();

    #endregion

    #region Constructors

    public Alu(IEventBus eventBus, RegisterFile registerFile, ILogger<Alu> logger)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _registerFile = registerFile ?? throw new ArgumentNullException(nameof(registerFile));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to events
        _clockSubscription = _eventBus.Subscribe<ClockPhaseChanged>(this);
        _aluSubscription = _eventBus.Subscribe<AluCommand>(this);
        _aluImmediateSubscription = _eventBus.Subscribe<AluImmediateCommand>(this);
        _aluUnarySubscription = _eventBus.Subscribe<AluUnaryCommand>(this);
        _resetSubscription = _eventBus.Subscribe<SystemReset>(this);

        _logger.LogDebug("ALU initialized");
    }

    #endregion

    #region Methods

    private void ProcessPendingOperations()
    {
        // Process binary register operations
        while (_pendingCommands.TryDequeue(out var cmd))
        {
            var a = _registerFile[cmd.SourceA];
            var b = _registerFile[cmd.SourceB];
            var (result, flags) = ExecuteBinaryOp(cmd.Operation, a, b);

            _eventBus.Publish(new RegisterWriteWithFlagsCommand(cmd.Destination, result));
            UpdateFlags(flags);

            _logger.LogDebug("ALU {Op}: {A:X16} {Op} {B:X16} = {Result:X16}, Flags={Flags}",
                cmd.Operation, a, cmd.Operation, b, result, flags.ToFlagString());
        }

        // Process immediate operations
        while (_pendingImmediateCommands.TryDequeue(out var cmd))
        {
            var a = _registerFile[cmd.Source];
            var b = cmd.Immediate;
            var (result, flags) = ExecuteBinaryOp(cmd.Operation, a, b);

            _eventBus.Publish(new RegisterWriteWithFlagsCommand(cmd.Destination, result));
            UpdateFlags(flags);

            _logger.LogDebug("ALU {Op}: {A:X16} {Op} {B:X16} = {Result:X16}, Flags={Flags}",
                cmd.Operation, a, cmd.Operation, b, result, flags.ToFlagString());
        }

        // Process unary operations
        while (_pendingUnaryCommands.TryDequeue(out var cmd))
        {
            var value = _registerFile[cmd.Source];
            var (result, flags) = ExecuteUnaryOp(cmd.Operation, value);

            _eventBus.Publish(new RegisterWriteWithFlagsCommand(cmd.Destination, result));
            UpdateFlags(flags);

            _logger.LogDebug("ALU {Op}: {Op} {Value:X16} = {Result:X16}, Flags={Flags}",
                cmd.Operation, cmd.Operation, value, result, flags.ToFlagString());
        }
    }

    private (ulong result, ulong flags) ExecuteBinaryOp(AluOp op, ulong a, ulong b)
    {
        ulong result = 0;
        ulong flags = 0;

        switch (op)
        {
            // Arithmetic operations
            case AluOp.Add:
                result = a + b;
                flags = ComputeArithmeticFlags(a, b, result, isSubtraction: false);
                break;

            case AluOp.Sub:
                result = a - b;
                flags = ComputeArithmeticFlags(a, b, result, isSubtraction: true);
                break;

            case AluOp.Mul:
                result = a * b;
                // Check for overflow: if result of 128-bit multiply doesn't fit in 64 bits
                var mulHigh = MultiplyHigh(a, b);
                flags = ComputeBasicFlags(result);
                if (mulHigh != 0)
                {
                    flags = flags.SetFlag(CpuFlags.Carry, true);
                    flags = flags.SetFlag(CpuFlags.Overflow, true);
                }
                break;

            case AluOp.Div:
                if (b == 0)
                {
                    result = 0;
                    flags = flags.SetFlag(CpuFlags.DivideByZero, true);
                }
                else
                {
                    result = a / b;
                    flags = ComputeBasicFlags(result);
                }
                break;

            // Bitwise logic operations
            case AluOp.And:
                result = a & b;
                flags = ComputeBasicFlags(result);
                break;

            case AluOp.Nand:
                result = ~(a & b);
                flags = ComputeBasicFlags(result);
                break;

            case AluOp.Or:
                result = a | b;
                flags = ComputeBasicFlags(result);
                break;

            case AluOp.Nor:
                result = ~(a | b);
                flags = ComputeBasicFlags(result);
                break;

            case AluOp.Xor:
                result = a ^ b;
                flags = ComputeBasicFlags(result);
                break;

            case AluOp.Xnor:
                result = ~(a ^ b);
                flags = ComputeBasicFlags(result);
                break;

            // Shift operations (b = shift amount)
            case AluOp.Shl:
                {
                    var shiftAmount = (int)(b & 0x3F); // Mask to 0-63
                    result = a << shiftAmount;
                    flags = ComputeBasicFlags(result);
                    // Carry = last bit shifted out
                    if (shiftAmount > 0 && shiftAmount <= 64)
                    {
                        var lastShiftedBit = (a >> (64 - shiftAmount)) & 1;
                        flags = flags.SetFlag(CpuFlags.Carry, lastShiftedBit != 0);
                    }
                }
                break;

            case AluOp.Shr:
                {
                    var shiftAmount = (int)(b & 0x3F);
                    result = a >> shiftAmount;
                    flags = ComputeBasicFlags(result);
                    // Carry = last bit shifted out
                    if (shiftAmount > 0)
                    {
                        var lastShiftedBit = (a >> (shiftAmount - 1)) & 1;
                        flags = flags.SetFlag(CpuFlags.Carry, lastShiftedBit != 0);
                    }
                }
                break;

            case AluOp.Sar:
                {
                    var shiftAmount = (int)(b & 0x3F);
                    var signBit = (long)a < 0;
                    result = (ulong)((long)a >> shiftAmount); // Cast to long for sign extension
                    flags = ComputeBasicFlags(result);
                    // Carry = last bit shifted out
                    if (shiftAmount > 0)
                    {
                        var lastShiftedBit = (a >> (shiftAmount - 1)) & 1;
                        flags = flags.SetFlag(CpuFlags.Carry, lastShiftedBit != 0);
                    }
                }
                break;

            // Rotation operations (b = rotation amount)
            case AluOp.Rol:
                {
                    var rotateAmount = (int)(b & 0x3F);
                    result = (a << rotateAmount) | (a >> (64 - rotateAmount));
                    flags = ComputeBasicFlags(result);
                }
                break;

            case AluOp.Ror:
                {
                    var rotateAmount = (int)(b & 0x3F);
                    result = (a >> rotateAmount) | (a << (64 - rotateAmount));
                    flags = ComputeBasicFlags(result);
                }
                break;

            default:
                _logger.LogWarning("Unknown ALU operation: {Op}", op);
                break;
        }

        return (result, flags);
    }

    private (ulong result, ulong flags) ExecuteUnaryOp(AluOp op, ulong value)
    {
        ulong result = 0;
        ulong flags = 0;

        switch (op)
        {
            case AluOp.Not:
                result = ~value;
                flags = ComputeBasicFlags(result);
                break;

            case AluOp.Inc:
                result = value + 1;
                flags = ComputeArithmeticFlags(value, 1, result, isSubtraction: false);
                break;

            case AluOp.Dec:
                result = value - 1;
                flags = ComputeArithmeticFlags(value, 1, result, isSubtraction: true);
                break;

            default:
                _logger.LogWarning("Unknown unary ALU operation: {Op}", op);
                break;
        }

        return (result, flags);
    }

    private ulong ComputeBasicFlags(ulong result)
    {
        ulong flags = 0;
        flags = flags.SetFlag(CpuFlags.Zero, result == 0);
        flags = flags.SetFlag(CpuFlags.Negative, (result & 0x8000_0000_0000_0000) != 0);
        return flags;
    }

    private ulong ComputeArithmeticFlags(ulong a, ulong b, ulong result, bool isSubtraction)
    {
        var flags = ComputeBasicFlags(result);

        // Carry flag (unsigned overflow)
        if (isSubtraction)
        {
            // Carry is set if borrow occurred (a < b)
            flags = flags.SetFlag(CpuFlags.Carry, a < b);
        }
        else
        {
            // Carry is set if result wrapped around (overflow in unsigned addition)
            flags = flags.SetFlag(CpuFlags.Carry, result < a);
        }

        // Overflow flag (signed overflow)
        // For addition: overflow if signs of operands are same but result sign differs
        // For subtraction: overflow if signs of operands differ and result sign differs from a
        var aSign = (long)a < 0;
        var bSign = (long)b < 0;
        var resultSign = (long)result < 0;

        bool overflow;
        if (isSubtraction)
        {
            overflow = (aSign != bSign) && (resultSign != aSign);
        }
        else
        {
            overflow = (aSign == bSign) && (resultSign != aSign);
        }
        flags = flags.SetFlag(CpuFlags.Overflow, overflow);

        return flags;
    }

    private ulong MultiplyHigh(ulong a, ulong b)
    {
        // Compute high 64 bits of 128-bit multiplication result
        // Split into 32-bit parts for calculation
        var a_lo = a & 0xFFFFFFFF;
        var a_hi = a >> 32;
        var b_lo = b & 0xFFFFFFFF;
        var b_hi = b >> 32;

        var p0 = a_lo * b_lo;
        var p1 = a_lo * b_hi;
        var p2 = a_hi * b_lo;
        var p3 = a_hi * b_hi;

        var carry = ((p0 >> 32) + (p1 & 0xFFFFFFFF) + (p2 & 0xFFFFFFFF)) >> 32;

        return p3 + (p1 >> 32) + (p2 >> 32) + carry;
    }

    private void UpdateFlags(ulong newFlags)
    {
        // Merge new flags with existing flags register
        // We preserve other bits and only update the flag bits we computed
        var currentFlags = _registerFile[RegisterId.Flags];

        // For now, replace all flags (we compute all relevant flags in each operation)
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.Flags, newFlags));
    }

    #endregion

    #region IObserver<ClockPhaseChanged> Implementation

    void IObserver<ClockPhaseChanged>.OnNext(ClockPhaseChanged clock)
    {
        // Process operations on rising edge only
        if (clock.IsRisingEdge)
        {
            ProcessPendingOperations();
        }
    }

    void IObserver<ClockPhaseChanged>.OnError(Exception error)
    {
        _logger.LogError(error, "Error in clock observer");
    }

    void IObserver<ClockPhaseChanged>.OnCompleted()
    {
        _logger.LogDebug("Clock observer completed");
    }

    #endregion

    #region IObserver<AluCommand> Implementation

    void IObserver<AluCommand>.OnNext(AluCommand command)
    {
        _pendingCommands.Enqueue(command);
        _logger.LogDebug("Queued ALU command: {Op} {A}, {B} -> {Dest}",
            command.Operation, command.SourceA, command.SourceB, command.Destination);
    }

    void IObserver<AluCommand>.OnError(Exception error)
    {
        _logger.LogError(error, "Error in ALU command observer");
    }

    void IObserver<AluCommand>.OnCompleted()
    {
        _logger.LogDebug("ALU command observer completed");
    }

    #endregion

    #region IObserver<AluImmediateCommand> Implementation

    void IObserver<AluImmediateCommand>.OnNext(AluImmediateCommand command)
    {
        _pendingImmediateCommands.Enqueue(command);
        _logger.LogDebug("Queued ALU immediate command: {Op} {Source}, 0x{Immediate:X16} -> {Dest}",
            command.Operation, command.Source, command.Immediate, command.Destination);
    }

    void IObserver<AluImmediateCommand>.OnError(Exception error)
    {
        _logger.LogError(error, "Error in ALU immediate command observer");
    }

    void IObserver<AluImmediateCommand>.OnCompleted()
    {
        _logger.LogDebug("ALU immediate command observer completed");
    }

    #endregion

    #region IObserver<AluUnaryCommand> Implementation

    void IObserver<AluUnaryCommand>.OnNext(AluUnaryCommand command)
    {
        _pendingUnaryCommands.Enqueue(command);
        _logger.LogDebug("Queued ALU unary command: {Op} {Source} -> {Dest}",
            command.Operation, command.Source, command.Destination);
    }

    void IObserver<AluUnaryCommand>.OnError(Exception error)
    {
        _logger.LogError(error, "Error in ALU unary command observer");
    }

    void IObserver<AluUnaryCommand>.OnCompleted()
    {
        _logger.LogDebug("ALU unary command observer completed");
    }

    #endregion

    #region IObserver<SystemReset> Implementation

    void IObserver<SystemReset>.OnNext(SystemReset reset)
    {
        _logger.LogInformation("ALU system reset received");

        // Clear pending operations
        while (_pendingCommands.TryDequeue(out _)) { }
        while (_pendingImmediateCommands.TryDequeue(out _)) { }
        while (_pendingUnaryCommands.TryDequeue(out _)) { }
    }

    void IObserver<SystemReset>.OnError(Exception error)
    {
        _logger.LogError(error, "Error in system reset observer");
    }

    void IObserver<SystemReset>.OnCompleted()
    {
        _logger.LogDebug("System reset observer completed");
    }

    #endregion

    #region IDisposable Implementation

    public void Dispose()
    {
        _clockSubscription.Dispose();
        _aluSubscription.Dispose();
        _aluImmediateSubscription.Dispose();
        _aluUnarySubscription.Dispose();
        _resetSubscription.Dispose();
    }

    #endregion
}
