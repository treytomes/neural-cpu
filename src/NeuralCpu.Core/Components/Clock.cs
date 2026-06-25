using NeuralCpu.Core.Events;
using System.Collections.Concurrent;

namespace NeuralCpu.Core.Components;

/// <summary>
/// Clock module that generates timing signals for the CPU.
/// Supports two-phase operation (rising/falling edges) and hardware interrupts.
/// </summary>
public sealed class Clock : IObserver<HardwareInterrupt>, IDisposable
{
    #region Fields

    private readonly IEventBus _eventBus;
    private readonly ConcurrentQueue<HardwareInterrupt> _interruptQueue = new();
    private readonly IDisposable _interruptSubscription;

    private Task? _clockTask;
    private CancellationTokenSource? _cancellationTokenSource;

    private ClockPhase _currentPhase = ClockPhase.Low;
    private ulong _cycleCount = 0;
    private int _frequencyHz = 1;
    private bool _isRunning;

    #endregion

    #region Constructors

    public Clock(IEventBus eventBus)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _interruptSubscription = _eventBus.Subscribe<HardwareInterrupt>(this);
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the clock frequency in Hertz.
    /// Changes take effect on the next cycle.
    /// </summary>
    public int FrequencyHz
    {
        get => _frequencyHz;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Frequency must be positive");
            _frequencyHz = value;
        }
    }

    /// <summary>
    /// Gets whether the clock is currently running.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Gets the current clock phase.
    /// </summary>
    public ClockPhase CurrentPhase => _currentPhase;

    /// <summary>
    /// Gets the current cycle count (increments on rising edge).
    /// </summary>
    public ulong CycleCount => _cycleCount;

    /// <summary>
    /// Gets the number of queued hardware interrupts.
    /// </summary>
    public int PendingInterrupts => _interruptQueue.Count;

    #endregion

    #region Methods

    /// <summary>
    /// Starts the clock running at the configured frequency.
    /// </summary>
    public void Start()
    {
        if (_isRunning)
            return;

        _isRunning = true;
        _cancellationTokenSource = new CancellationTokenSource();
        _clockTask = Task.Run(() => RunClockLoop(_cancellationTokenSource.Token));
    }

    /// <summary>
    /// Stops the clock.
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
            return;

        _isRunning = false;
        _cancellationTokenSource?.Cancel();
        _clockTask?.Wait(TimeSpan.FromSeconds(1));
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        _clockTask = null;
    }

    /// <summary>
    /// Executes a single clock cycle (rising edge → falling edge).
    /// </summary>
    public void Step()
    {
        ExecuteCycle();
    }

    private async Task RunClockLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var cycleStart = DateTimeOffset.UtcNow;

            ExecuteCycle();

            // Calculate delay for target frequency
            // One cycle = rising + falling edge
            var cycleDurationMs = 1000.0 / _frequencyHz;
            var elapsed = (DateTimeOffset.UtcNow - cycleStart).TotalMilliseconds;
            var remaining = cycleDurationMs - elapsed;

            if (remaining > 0)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(remaining), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private void ExecuteCycle()
    {
        // Process any pending interrupts at the start of the cycle
        ProcessPendingInterrupts();

        // Rising edge
        _currentPhase = ClockPhase.High;
        _cycleCount++;
        _eventBus.Publish(new ClockPhaseChanged(ClockPhase.High, _cycleCount));

        // Falling edge
        _currentPhase = ClockPhase.Low;
        _eventBus.Publish(new ClockPhaseChanged(ClockPhase.Low, _cycleCount));
    }

    private void ProcessPendingInterrupts()
    {
        // For now, just dequeue and discard
        // Later, this will trigger interrupt handling logic
        while (_interruptQueue.TryDequeue(out _))
        {
            // Interrupt processing would go here
        }
    }

    // IObserver<HardwareInterrupt> implementation
    void IObserver<HardwareInterrupt>.OnNext(HardwareInterrupt interrupt)
    {
        _interruptQueue.Enqueue(interrupt);
    }

    void IObserver<HardwareInterrupt>.OnError(Exception error)
    {
        // Log error or handle as needed
    }

    void IObserver<HardwareInterrupt>.OnCompleted()
    {
        // Not used in this context
    }

    public void Dispose()
    {
        Stop();
        _interruptSubscription.Dispose();
    }

    #endregion
}
