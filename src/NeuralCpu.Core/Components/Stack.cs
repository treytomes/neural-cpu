using Microsoft.Extensions.Logging;
using NeuralCpu.Core.Events;
using System.Collections.Concurrent;

namespace NeuralCpu.Core.Components;

/// <summary>
/// Stack component with push/pop/peek/dup operations.
/// Uses .NET Stack&lt;ulong&gt; with 64K item limit (512KB memory).
/// Operations synchronized to clock rising edge.
/// SP register reflects stack.Count.
/// </summary>
public sealed class Stack : IObserver<ClockPhaseChanged>,
                             IObserver<PushCommand>,
                             IObserver<PushImmediateCommand>,
                             IObserver<PopCommand>,
                             IObserver<PeekCommand>,
                             IObserver<DupCommand>,
                             IObserver<SystemReset>,
                             IDisposable
{
    #region Constants

    /// <summary>Default maximum stack size in items (64K × 8 bytes = 512KB).</summary>
    public const int DefaultMaxSize = 65536;

    #endregion

    #region Fields

    private readonly IEventBus _eventBus;
    private readonly RegisterFile _registerFile;
    private readonly ILogger<Stack> _logger;
    private readonly int _maxStackSize;
    private readonly IDisposable _clockSubscription;
    private readonly IDisposable _pushSubscription;
    private readonly IDisposable _pushImmediateSubscription;
    private readonly IDisposable _popSubscription;
    private readonly IDisposable _peekSubscription;
    private readonly IDisposable _dupSubscription;
    private readonly IDisposable _resetSubscription;

    private readonly Stack<ulong> _stack = new();
    private readonly ConcurrentQueue<PushCommand> _pendingPushCommands = new();
    private readonly ConcurrentQueue<PushImmediateCommand> _pendingPushImmediateCommands = new();
    private readonly ConcurrentQueue<PopCommand> _pendingPopCommands = new();
    private readonly ConcurrentQueue<PeekCommand> _pendingPeekCommands = new();
    private readonly ConcurrentQueue<DupCommand> _pendingDupCommands = new();

    #endregion

    #region Constructors

    public Stack(IEventBus eventBus, RegisterFile registerFile, ILogger<Stack> logger)
        : this(eventBus, registerFile, logger, DefaultMaxSize)
    {
    }

    public Stack(IEventBus eventBus, RegisterFile registerFile, ILogger<Stack> logger, int maxSize)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _registerFile = registerFile ?? throw new ArgumentNullException(nameof(registerFile));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (maxSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSize), "Max size must be positive");

        _maxStackSize = maxSize;

        // Subscribe to events
        _clockSubscription = _eventBus.Subscribe<ClockPhaseChanged>(this);
        _pushSubscription = _eventBus.Subscribe<PushCommand>(this);
        _pushImmediateSubscription = _eventBus.Subscribe<PushImmediateCommand>(this);
        _popSubscription = _eventBus.Subscribe<PopCommand>(this);
        _peekSubscription = _eventBus.Subscribe<PeekCommand>(this);
        _dupSubscription = _eventBus.Subscribe<DupCommand>(this);
        _resetSubscription = _eventBus.Subscribe<SystemReset>(this);

        _logger.LogDebug("Stack initialized with max size {MaxSize} items", _maxStackSize);
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the current stack depth (direct access for debugging).
    /// </summary>
    public int Count => _stack.Count;

    /// <summary>
    /// Gets a snapshot of the stack contents (top to bottom) for display purposes.
    /// </summary>
    public ulong[] GetSnapshot()
    {
        return _stack.ToArray();
    }

    #endregion

    #region Methods

    private void ProcessPendingOperations()
    {
        // Process push commands (register source)
        while (_pendingPushCommands.TryDequeue(out var cmd))
        {
            var value = _registerFile[cmd.Source];
            if (_stack.Count >= _maxStackSize)
            {
                _logger.LogWarning("Stack overflow: cannot push (max size {MaxSize} reached)", _maxStackSize);
                SetFlag(CpuFlags.StackOverflow, true);
            }
            else
            {
                _stack.Push(value);
                _logger.LogDebug("PUSH {Source}: 0x{Value:X16} (depth now {Depth})",
                    cmd.Source, value, _stack.Count);
            }
        }

        // Process push immediate commands
        while (_pendingPushImmediateCommands.TryDequeue(out var cmd))
        {
            if (_stack.Count >= _maxStackSize)
            {
                _logger.LogWarning("Stack overflow: cannot push immediate (max size {MaxSize} reached)", _maxStackSize);
                SetFlag(CpuFlags.StackOverflow, true);
            }
            else
            {
                _stack.Push(cmd.Value);
                _logger.LogDebug("PUSH immediate: 0x{Value:X16} (depth now {Depth})",
                    cmd.Value, _stack.Count);
            }
        }

        // Process pop commands
        while (_pendingPopCommands.TryDequeue(out var cmd))
        {
            if (_stack.Count == 0)
            {
                _logger.LogWarning("Stack underflow: cannot pop (stack is empty)");
                SetFlag(CpuFlags.StackUnderflow, true);
                // Write 0 to destination register
                _eventBus.Publish(new RegisterWriteCommand(cmd.Destination, 0));
            }
            else
            {
                var value = _stack.Pop();
                _eventBus.Publish(new RegisterWriteCommand(cmd.Destination, value));
                _logger.LogDebug("POP → {Dest}: 0x{Value:X16} (depth now {Depth})",
                    cmd.Destination, value, _stack.Count);
            }
        }

        // Process peek commands
        while (_pendingPeekCommands.TryDequeue(out var cmd))
        {
            if (_stack.Count == 0)
            {
                _logger.LogWarning("Stack underflow: cannot peek (stack is empty)");
                SetFlag(CpuFlags.StackUnderflow, true);
                // Write 0 to destination register
                _eventBus.Publish(new RegisterWriteCommand(cmd.Destination, 0));
            }
            else
            {
                var value = _stack.Peek();
                _eventBus.Publish(new RegisterWriteCommand(cmd.Destination, value));
                _logger.LogDebug("PEEK → {Dest}: 0x{Value:X16} (depth {Depth})",
                    cmd.Destination, value, _stack.Count);
            }
        }

        // Process dup commands
        while (_pendingDupCommands.TryDequeue(out var cmd))
        {
            if (_stack.Count == 0)
            {
                _logger.LogWarning("Stack underflow: cannot dup (stack is empty)");
                SetFlag(CpuFlags.StackUnderflow, true);
            }
            else if (_stack.Count >= _maxStackSize)
            {
                _logger.LogWarning("Stack overflow: cannot dup (max size {MaxSize} reached)", _maxStackSize);
                SetFlag(CpuFlags.StackOverflow, true);
            }
            else
            {
                var value = _stack.Peek();
                _stack.Push(value);
                _logger.LogDebug("DUP: 0x{Value:X16} (depth now {Depth})",
                    value, _stack.Count);
            }
        }

        // Update SP register to reflect current stack depth
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.SP, (ulong)_stack.Count));
    }

    private void SetFlag(CpuFlags flag, bool value)
    {
        var currentFlags = _registerFile[RegisterId.Flags];
        var newFlags = currentFlags.SetFlag(flag, value);
        if (currentFlags != newFlags)
        {
            _eventBus.Publish(new RegisterWriteCommand(RegisterId.Flags, newFlags));
        }
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

    #region IObserver<PushCommand> Implementation

    void IObserver<PushCommand>.OnNext(PushCommand command)
    {
        _pendingPushCommands.Enqueue(command);
        _logger.LogDebug("Queued PUSH {Source}", command.Source);
    }

    void IObserver<PushCommand>.OnError(Exception error)
    {
        _logger.LogError(error, "Error in push command observer");
    }

    void IObserver<PushCommand>.OnCompleted()
    {
        _logger.LogDebug("Push command observer completed");
    }

    #endregion

    #region IObserver<PushImmediateCommand> Implementation

    void IObserver<PushImmediateCommand>.OnNext(PushImmediateCommand command)
    {
        _pendingPushImmediateCommands.Enqueue(command);
        _logger.LogDebug("Queued PUSH immediate 0x{Value:X16}", command.Value);
    }

    void IObserver<PushImmediateCommand>.OnError(Exception error)
    {
        _logger.LogError(error, "Error in push immediate command observer");
    }

    void IObserver<PushImmediateCommand>.OnCompleted()
    {
        _logger.LogDebug("Push immediate command observer completed");
    }

    #endregion

    #region IObserver<PopCommand> Implementation

    void IObserver<PopCommand>.OnNext(PopCommand command)
    {
        _pendingPopCommands.Enqueue(command);
        _logger.LogDebug("Queued POP → {Dest}", command.Destination);
    }

    void IObserver<PopCommand>.OnError(Exception error)
    {
        _logger.LogError(error, "Error in pop command observer");
    }

    void IObserver<PopCommand>.OnCompleted()
    {
        _logger.LogDebug("Pop command observer completed");
    }

    #endregion

    #region IObserver<PeekCommand> Implementation

    void IObserver<PeekCommand>.OnNext(PeekCommand command)
    {
        _pendingPeekCommands.Enqueue(command);
        _logger.LogDebug("Queued PEEK → {Dest}", command.Destination);
    }

    void IObserver<PeekCommand>.OnError(Exception error)
    {
        _logger.LogError(error, "Error in peek command observer");
    }

    void IObserver<PeekCommand>.OnCompleted()
    {
        _logger.LogDebug("Peek command observer completed");
    }

    #endregion

    #region IObserver<DupCommand> Implementation

    void IObserver<DupCommand>.OnNext(DupCommand command)
    {
        _pendingDupCommands.Enqueue(command);
        _logger.LogDebug("Queued DUP");
    }

    void IObserver<DupCommand>.OnError(Exception error)
    {
        _logger.LogError(error, "Error in dup command observer");
    }

    void IObserver<DupCommand>.OnCompleted()
    {
        _logger.LogDebug("Dup command observer completed");
    }

    #endregion

    #region IObserver<SystemReset> Implementation

    void IObserver<SystemReset>.OnNext(SystemReset reset)
    {
        _logger.LogInformation("Stack system reset received");

        // Clear stack
        _stack.Clear();

        // Clear pending operations
        while (_pendingPushCommands.TryDequeue(out _)) { }
        while (_pendingPushImmediateCommands.TryDequeue(out _)) { }
        while (_pendingPopCommands.TryDequeue(out _)) { }
        while (_pendingPeekCommands.TryDequeue(out _)) { }
        while (_pendingDupCommands.TryDequeue(out _)) { }

        // SP will be reset by RegisterFile, but we should publish update to 0 for consistency
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.SP, 0));
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
        _pushSubscription.Dispose();
        _pushImmediateSubscription.Dispose();
        _popSubscription.Dispose();
        _peekSubscription.Dispose();
        _dupSubscription.Dispose();
        _resetSubscription.Dispose();
    }

    #endregion
}
