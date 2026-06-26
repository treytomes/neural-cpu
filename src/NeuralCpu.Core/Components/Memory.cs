using Microsoft.Extensions.Logging;
using NeuralCpu.Core.Events;
using System.Collections.Concurrent;

namespace NeuralCpu.Core.Components;

/// <summary>
/// Memory component providing random-access word-addressable RAM.
/// Supports direct and register-indirect addressing modes.
/// All operations execute on clock rising edge (single-cycle).
/// </summary>
public sealed class Memory : IObserver<ClockPhaseChanged>,
                              IObserver<LoadCommand>,
                              IObserver<LoadIndirectCommand>,
                              IObserver<StoreCommand>,
                              IObserver<StoreIndirectCommand>,
                              IObserver<SystemReset>,
                              IDisposable
{
    #region Constants

    /// <summary>Default memory capacity in words (128K ulongs = 1MB).</summary>
    public const int DefaultCapacityWords = 131072;

    /// <summary>Duration for which writes are highlighted (5 seconds).</summary>
    private static readonly TimeSpan HighlightDuration = TimeSpan.FromSeconds(5);

    #endregion

    #region Fields

    private readonly IEventBus _eventBus;
    private readonly RegisterFile _registerFile;
    private readonly ILogger<Memory> _logger;
    private readonly IDisposable _clockSubscription;
    private readonly IDisposable _loadSubscription;
    private readonly IDisposable _loadIndirectSubscription;
    private readonly IDisposable _storeSubscription;
    private readonly IDisposable _storeIndirectSubscription;
    private readonly IDisposable _resetSubscription;

    private readonly ulong[] _memory;
    private readonly Dictionary<ulong, DateTimeOffset> _recentWrites = new();
    private readonly ConcurrentQueue<LoadCommand> _pendingLoads = new();
    private readonly ConcurrentQueue<LoadIndirectCommand> _pendingLoadIndirects = new();
    private readonly ConcurrentQueue<StoreCommand> _pendingStores = new();
    private readonly ConcurrentQueue<StoreIndirectCommand> _pendingStoreIndirects = new();

    #endregion

    #region Constructors

    public Memory(IEventBus eventBus, RegisterFile registerFile, ILogger<Memory> logger)
        : this(eventBus, registerFile, logger, DefaultCapacityWords)
    {
    }

    public Memory(IEventBus eventBus, RegisterFile registerFile, ILogger<Memory> logger, int capacityWords)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _registerFile = registerFile ?? throw new ArgumentNullException(nameof(registerFile));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (capacityWords <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacityWords), "Capacity must be positive");

        _memory = new ulong[capacityWords];

        // Subscribe to events
        _clockSubscription = _eventBus.Subscribe<ClockPhaseChanged>(this);
        _loadSubscription = _eventBus.Subscribe<LoadCommand>(this);
        _loadIndirectSubscription = _eventBus.Subscribe<LoadIndirectCommand>(this);
        _storeSubscription = _eventBus.Subscribe<StoreCommand>(this);
        _storeIndirectSubscription = _eventBus.Subscribe<StoreIndirectCommand>(this);
        _resetSubscription = _eventBus.Subscribe<SystemReset>(this);

        _logger.LogDebug("Memory initialized: {Capacity} words ({Bytes} bytes)",
            capacityWords, capacityWords * sizeof(ulong));
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the memory capacity in words (ulongs).
    /// </summary>
    public int CapacityWords => _memory.Length;

    /// <summary>
    /// Gets the memory capacity in bytes.
    /// </summary>
    public int CapacityBytes => _memory.Length * sizeof(ulong);

    /// <summary>
    /// Direct memory access (for debugging only).
    /// </summary>
    public ulong this[ulong address]
    {
        get => IsValidAddress(address) ? _memory[address] : 0;
        set
        {
            if (IsValidAddress(address))
                _memory[address] = value;
        }
    }

    #endregion

    #region Methods

    /// <summary>
    /// Checks if an address is within valid memory bounds.
    /// </summary>
    private bool IsValidAddress(ulong address)
    {
        return address < (ulong)_memory.Length;
    }

    /// <summary>
    /// Checks if an address was recently written (within highlight duration).
    /// </summary>
    public bool WasRecentlyChanged(ulong address)
    {
        return _recentWrites.TryGetValue(address, out var time)
            && DateTimeOffset.UtcNow - time < HighlightDuration;
    }

    /// <summary>
    /// Gets a snapshot of memory contents for display purposes.
    /// </summary>
    public ulong[] GetSnapshot(ulong startAddress, int count)
    {
        if (!IsValidAddress(startAddress))
            return Array.Empty<ulong>();

        var actualCount = Math.Min(count, _memory.Length - (int)startAddress);
        var snapshot = new ulong[actualCount];
        Array.Copy(_memory, (long)startAddress, snapshot, 0, actualCount);
        return snapshot;
    }

    private void ProcessPendingOperations()
    {
        // Process direct loads
        while (_pendingLoads.TryDequeue(out var cmd))
        {
            if (!IsValidAddress(cmd.Address))
            {
                _logger.LogWarning("LOAD: Out-of-bounds address 0x{Address:X16} (capacity: {Capacity} words)",
                    cmd.Address, _memory.Length);
                SetFlag(CpuFlags.MemoryFault, true);
                _eventBus.Publish(new RegisterWriteCommand(cmd.Destination, 0));
            }
            else
            {
                var value = _memory[cmd.Address];
                _eventBus.Publish(new RegisterWriteCommand(cmd.Destination, value));
                _logger.LogDebug("LOAD [0x{Address:X16}] → {Dest}: 0x{Value:X16}",
                    cmd.Address, cmd.Destination, value);
            }
        }

        // Process register-indirect loads
        while (_pendingLoadIndirects.TryDequeue(out var cmd))
        {
            var address = _registerFile[cmd.AddressRegister];
            if (!IsValidAddress(address))
            {
                _logger.LogWarning("LOAD indirect [{AddrReg}=0x{Address:X16}] → {Dest}: Out-of-bounds (capacity: {Capacity} words)",
                    cmd.AddressRegister, address, cmd.Destination, _memory.Length);
                SetFlag(CpuFlags.MemoryFault, true);
                _eventBus.Publish(new RegisterWriteCommand(cmd.Destination, 0));
            }
            else
            {
                var value = _memory[address];
                _eventBus.Publish(new RegisterWriteCommand(cmd.Destination, value));
                _logger.LogDebug("LOAD [{AddrReg}=0x{Address:X16}] → {Dest}: 0x{Value:X16}",
                    cmd.AddressRegister, address, cmd.Destination, value);
            }
        }

        // Process direct stores
        while (_pendingStores.TryDequeue(out var cmd))
        {
            var value = _registerFile[cmd.Source];
            if (!IsValidAddress(cmd.Address))
            {
                _logger.LogWarning("STORE {Source} → [0x{Address:X16}]: Out-of-bounds (capacity: {Capacity} words)",
                    cmd.Source, cmd.Address, _memory.Length);
                SetFlag(CpuFlags.MemoryFault, true);
            }
            else
            {
                _memory[cmd.Address] = value;
                _recentWrites[cmd.Address] = DateTimeOffset.UtcNow;
                _logger.LogDebug("STORE {Source} (0x{Value:X16}) → [0x{Address:X16}]",
                    cmd.Source, value, cmd.Address);
            }
        }

        // Process register-indirect stores
        while (_pendingStoreIndirects.TryDequeue(out var cmd))
        {
            var address = _registerFile[cmd.AddressRegister];
            var value = _registerFile[cmd.Source];
            if (!IsValidAddress(address))
            {
                _logger.LogWarning("STORE indirect {Source} → [{AddrReg}=0x{Address:X16}]: Out-of-bounds (capacity: {Capacity} words)",
                    cmd.Source, cmd.AddressRegister, address, _memory.Length);
                SetFlag(CpuFlags.MemoryFault, true);
            }
            else
            {
                _memory[address] = value;
                _recentWrites[address] = DateTimeOffset.UtcNow;
                _logger.LogDebug("STORE {Source} (0x{Value:X16}) → [{AddrReg}=0x{Address:X16}]",
                    cmd.Source, value, cmd.AddressRegister, address);
            }
        }
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

    #region IObserver<LoadCommand> Implementation

    void IObserver<LoadCommand>.OnNext(LoadCommand command)
    {
        _pendingLoads.Enqueue(command);
        _logger.LogDebug("Queued LOAD [0x{Address:X16}] → {Dest}", command.Address, command.Destination);
    }

    void IObserver<LoadCommand>.OnError(Exception error)
    {
        _logger.LogError(error, "Error in load command observer");
    }

    void IObserver<LoadCommand>.OnCompleted()
    {
        _logger.LogDebug("Load command observer completed");
    }

    #endregion

    #region IObserver<LoadIndirectCommand> Implementation

    void IObserver<LoadIndirectCommand>.OnNext(LoadIndirectCommand command)
    {
        _pendingLoadIndirects.Enqueue(command);
        _logger.LogDebug("Queued LOAD [{AddrReg}] → {Dest}",
            command.AddressRegister, command.Destination);
    }

    void IObserver<LoadIndirectCommand>.OnError(Exception error)
    {
        _logger.LogError(error, "Error in load indirect command observer");
    }

    void IObserver<LoadIndirectCommand>.OnCompleted()
    {
        _logger.LogDebug("Load indirect command observer completed");
    }

    #endregion

    #region IObserver<StoreCommand> Implementation

    void IObserver<StoreCommand>.OnNext(StoreCommand command)
    {
        _pendingStores.Enqueue(command);
        _logger.LogDebug("Queued STORE {Source} → [0x{Address:X16}]",
            command.Source, command.Address);
    }

    void IObserver<StoreCommand>.OnError(Exception error)
    {
        _logger.LogError(error, "Error in store command observer");
    }

    void IObserver<StoreCommand>.OnCompleted()
    {
        _logger.LogDebug("Store command observer completed");
    }

    #endregion

    #region IObserver<StoreIndirectCommand> Implementation

    void IObserver<StoreIndirectCommand>.OnNext(StoreIndirectCommand command)
    {
        _pendingStoreIndirects.Enqueue(command);
        _logger.LogDebug("Queued STORE {Source} → [{AddrReg}]",
            command.Source, command.AddressRegister);
    }

    void IObserver<StoreIndirectCommand>.OnError(Exception error)
    {
        _logger.LogError(error, "Error in store indirect command observer");
    }

    void IObserver<StoreIndirectCommand>.OnCompleted()
    {
        _logger.LogDebug("Store indirect command observer completed");
    }

    #endregion

    #region IObserver<SystemReset> Implementation

    void IObserver<SystemReset>.OnNext(SystemReset reset)
    {
        _logger.LogInformation("Memory system reset received");

        // Clear memory
        Array.Clear(_memory, 0, _memory.Length);

        // Clear recent writes tracking
        _recentWrites.Clear();

        // Clear pending operations
        while (_pendingLoads.TryDequeue(out _)) { }
        while (_pendingLoadIndirects.TryDequeue(out _)) { }
        while (_pendingStores.TryDequeue(out _)) { }
        while (_pendingStoreIndirects.TryDequeue(out _)) { }
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
        _loadSubscription.Dispose();
        _loadIndirectSubscription.Dispose();
        _storeSubscription.Dispose();
        _storeIndirectSubscription.Dispose();
        _resetSubscription.Dispose();
    }

    #endregion
}
