using Microsoft.Extensions.Logging;
using NeuralCpu.Core.Events;
using System.Collections.Concurrent;

namespace NeuralCpu.Core.Components;

/// <summary>
/// Register file containing all CPU registers (R0-R3, PC, SP, Flags).
/// Processes write commands on clock rising edges and responds to read requests immediately.
/// PC auto-increments on every clock cycle to track time.
/// </summary>
public sealed class RegisterFile : IObserver<ClockPhaseChanged>,
                                    IObserver<RegisterWriteCommand>,
                                    IObserver<RegisterWriteWithFlagsCommand>,
                                    IObserver<RegisterCopyCommand>,
                                    IObserver<RegisterReadRequest>,
                                    IObserver<SystemReset>,
                                    IDisposable
{
    #region Constants

    // All registers reset to zero
    private const ulong DefaultRegisterValue = 0x0000_0000_0000_0000;

    #endregion

    #region Fields

    private readonly IEventBus _eventBus;
    private readonly ILogger<RegisterFile> _logger;
    private readonly IDisposable _clockSubscription;
    private readonly IDisposable _writeSubscription;
    private readonly IDisposable _writeFlagsSubscription;
    private readonly IDisposable _copySubscription;
    private readonly IDisposable _readSubscription;
    private readonly IDisposable _resetSubscription;

    private readonly Dictionary<RegisterId, ulong> _registers = new();
    private readonly ConcurrentQueue<RegisterWriteCommand> _pendingWrites = new();
    private readonly ConcurrentQueue<RegisterWriteWithFlagsCommand> _pendingWritesWithFlags = new();
    private readonly ConcurrentQueue<RegisterCopyCommand> _pendingCopies = new();

    #endregion

    #region Constructors

    public RegisterFile(IEventBus eventBus, ILogger<RegisterFile> logger)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to events
        _clockSubscription = _eventBus.Subscribe<ClockPhaseChanged>(this);
        _writeSubscription = _eventBus.Subscribe<RegisterWriteCommand>(this);
        _writeFlagsSubscription = _eventBus.Subscribe<RegisterWriteWithFlagsCommand>(this);
        _copySubscription = _eventBus.Subscribe<RegisterCopyCommand>(this);
        _readSubscription = _eventBus.Subscribe<RegisterReadRequest>(this);
        _resetSubscription = _eventBus.Subscribe<SystemReset>(this);

        // Initialize registers to default values
        ResetRegisters();

        _logger.LogDebug("RegisterFile initialized with {Count} registers", _registers.Count);
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the current value of a register (direct access for debugging).
    /// </summary>
    public ulong this[RegisterId registerId] => _registers[registerId];

    #endregion

    #region Methods

    private void ResetRegisters()
    {
        _registers[RegisterId.R0] = DefaultRegisterValue;
        _registers[RegisterId.R1] = DefaultRegisterValue;
        _registers[RegisterId.R2] = DefaultRegisterValue;
        _registers[RegisterId.R3] = DefaultRegisterValue;
        _registers[RegisterId.PC] = DefaultRegisterValue;
        _registers[RegisterId.SP] = DefaultRegisterValue;
        _registers[RegisterId.Flags] = DefaultRegisterValue;

        _logger.LogInformation("All registers reset to 0x{Value:X16}", DefaultRegisterValue);
    }

    private void ProcessPendingWrites(ulong cycleCount)
    {
        // Process regular writes (no flag updates)
        while (_pendingWrites.TryDequeue(out var write))
        {
            // PC is read-only (auto-increments as time counter)
            if (write.RegisterId == RegisterId.PC)
            {
                _logger.LogWarning("Attempted write to PC ignored (PC is read-only time counter)");
                continue;
            }

            var oldValue = _registers[write.RegisterId];

            // Only write and publish if value actually changed
            if (oldValue != write.Value)
            {
                _registers[write.RegisterId] = write.Value;

                _logger.LogDebug("Register {RegisterId} changed: 0x{Old:X16} -> 0x{New:X16}",
                    write.RegisterId, oldValue, write.Value);

                _eventBus.Publish(new RegisterChanged(
                    write.RegisterId,
                    oldValue,
                    write.Value,
                    cycleCount));
            }
        }

        // Process writes with flag updates
        while (_pendingWritesWithFlags.TryDequeue(out var write))
        {
            // PC is read-only (auto-increments as time counter)
            if (write.RegisterId == RegisterId.PC)
            {
                _logger.LogWarning("Attempted write (with flags) to PC ignored (PC is read-only time counter)");
                continue;
            }

            var oldValue = _registers[write.RegisterId];

            // Only write and publish if value actually changed
            if (oldValue != write.Value)
            {
                _registers[write.RegisterId] = write.Value;

                _logger.LogDebug("Register {RegisterId} changed (with flags): 0x{Old:X16} -> 0x{New:X16}",
                    write.RegisterId, oldValue, write.Value);

                _eventBus.Publish(new RegisterChanged(
                    write.RegisterId,
                    oldValue,
                    write.Value,
                    cycleCount));
            }

            // Always update flags based on the written value (even if register didn't change)
            UpdateFlags(write.Value);
        }

        // Process register copy operations
        while (_pendingCopies.TryDequeue(out var copy))
        {
            // PC is read-only (auto-increments as time counter)
            if (copy.DestinationRegister == RegisterId.PC)
            {
                _logger.LogWarning("Attempted copy to PC ignored (PC is read-only time counter)");
                continue;
            }

            var sourceValue = _registers[copy.SourceRegister];
            var oldDestValue = _registers[copy.DestinationRegister];

            // Only copy and publish if value actually changed
            if (oldDestValue != sourceValue)
            {
                _registers[copy.DestinationRegister] = sourceValue;

                _logger.LogDebug("Register copy: {Source} (0x{Value:X16}) -> {Dest}",
                    copy.SourceRegister, sourceValue, copy.DestinationRegister);

                _eventBus.Publish(new RegisterChanged(
                    copy.DestinationRegister,
                    oldDestValue,
                    sourceValue,
                    cycleCount));
            }
        }

        // Auto-increment PC every cycle (gives network sense of time)
        var oldPc = _registers[RegisterId.PC];
        _registers[RegisterId.PC]++;

        if (oldPc != _registers[RegisterId.PC] - 1) // Only log if it's not sequential
        {
            _eventBus.Publish(new RegisterChanged(
                RegisterId.PC,
                oldPc,
                _registers[RegisterId.PC],
                cycleCount));
        }
    }

    private void UpdateFlags(ulong value)
    {
        var oldFlags = _registers[RegisterId.Flags];
        var newFlags = oldFlags;

        // Zero flag: set if value is zero
        newFlags = newFlags.SetFlag(CpuFlags.Zero, value == 0);

        // Negative flag: set if MSB is 1
        newFlags = newFlags.SetFlag(CpuFlags.Negative, (value & 0x8000_0000_0000_0000) != 0);

        // Carry and Overflow are only meaningful for ALU operations, leave unchanged for now

        if (oldFlags != newFlags)
        {
            _registers[RegisterId.Flags] = newFlags;
            _logger.LogDebug("Flags updated: {Old} -> {New}",
                oldFlags.ToFlagString(), newFlags.ToFlagString());
        }
    }

    #endregion

    #region IObserver<ClockPhaseChanged> Implementation

    void IObserver<ClockPhaseChanged>.OnNext(ClockPhaseChanged clock)
    {
        // Process writes on rising edge only
        if (clock.IsRisingEdge)
        {
            ProcessPendingWrites(clock.CycleCount);
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

    #region IObserver<RegisterWriteCommand> Implementation

    void IObserver<RegisterWriteCommand>.OnNext(RegisterWriteCommand command)
    {
        _pendingWrites.Enqueue(command);
        _logger.LogDebug("Queued write to {RegisterId}: 0x{Value:X16}",
            command.RegisterId, command.Value);
    }

    void IObserver<RegisterWriteCommand>.OnError(Exception error)
    {
        _logger.LogError(error, "Error in register write observer");
    }

    void IObserver<RegisterWriteCommand>.OnCompleted()
    {
        _logger.LogDebug("Register write observer completed");
    }

    #endregion

    #region IObserver<RegisterWriteWithFlagsCommand> Implementation

    void IObserver<RegisterWriteWithFlagsCommand>.OnNext(RegisterWriteWithFlagsCommand command)
    {
        _pendingWritesWithFlags.Enqueue(command);
        _logger.LogDebug("Queued write (with flags) to {RegisterId}: 0x{Value:X16}",
            command.RegisterId, command.Value);
    }

    void IObserver<RegisterWriteWithFlagsCommand>.OnError(Exception error)
    {
        _logger.LogError(error, "Error in register write with flags observer");
    }

    void IObserver<RegisterWriteWithFlagsCommand>.OnCompleted()
    {
        _logger.LogDebug("Register write with flags observer completed");
    }

    #endregion

    #region IObserver<RegisterCopyCommand> Implementation

    void IObserver<RegisterCopyCommand>.OnNext(RegisterCopyCommand command)
    {
        _pendingCopies.Enqueue(command);
        _logger.LogDebug("Queued register copy: {Source} -> {Dest}",
            command.SourceRegister, command.DestinationRegister);
    }

    void IObserver<RegisterCopyCommand>.OnError(Exception error)
    {
        _logger.LogError(error, "Error in register copy observer");
    }

    void IObserver<RegisterCopyCommand>.OnCompleted()
    {
        _logger.LogDebug("Register copy observer completed");
    }

    #endregion

    #region IObserver<RegisterReadRequest> Implementation

    void IObserver<RegisterReadRequest>.OnNext(RegisterReadRequest request)
    {
        // Respond immediately with current value (reads don't need clock sync)
        var value = _registers[request.RegisterId];
        _eventBus.Publish(new RegisterReadResponse(
            request.RegisterId,
            value,
            request.RequestId));

        _logger.LogDebug("Read {RegisterId}: 0x{Value:X16} (RequestId={RequestId})",
            request.RegisterId, value, request.RequestId);
    }

    void IObserver<RegisterReadRequest>.OnError(Exception error)
    {
        _logger.LogError(error, "Error in register read observer");
    }

    void IObserver<RegisterReadRequest>.OnCompleted()
    {
        _logger.LogDebug("Register read observer completed");
    }

    #endregion

    #region IObserver<SystemReset> Implementation

    void IObserver<SystemReset>.OnNext(SystemReset reset)
    {
        _logger.LogInformation("System reset received");

        // Clear any pending operations
        while (_pendingWrites.TryDequeue(out _)) { }
        while (_pendingWritesWithFlags.TryDequeue(out _)) { }
        while (_pendingCopies.TryDequeue(out _)) { }

        // Reset all registers to defaults
        ResetRegisters();
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
        _writeSubscription.Dispose();
        _writeFlagsSubscription.Dispose();
        _copySubscription.Dispose();
        _readSubscription.Dispose();
        _resetSubscription.Dispose();
    }

    #endregion
}
