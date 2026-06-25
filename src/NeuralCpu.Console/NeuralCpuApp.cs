using System.Collections.ObjectModel;
using NeuralCpu.Core.Components;
using NeuralCpu.Core.Events;
using Terminal.Gui;

namespace NeuralCpu.Console;

/// <summary>
/// Main TUI application for the Neural CPU simulator.
/// </summary>
internal sealed class NeuralCpuApp : Window, IObserver<ClockPhaseChanged>, IObserver<HardwareInterrupt>
{
    #region Fields

    private readonly IEventBus _eventBus;
    private readonly Clock _clock;
    private readonly IDisposable _clockSubscription;
    private readonly IDisposable _interruptSubscription;

    private readonly Label _statusLabel;
    private readonly Label _cycleLabel;
    private readonly Label _frequencyLabel;
    private readonly Label _phaseLabel;
    private readonly Label _interruptLabel;
    private readonly ListView _eventLog;

    private readonly ObservableCollection<string> _eventLogItems = new();
    private const int MaxEventLogItems = 100;

    #endregion

    #region Constructors

    public NeuralCpuApp(IEventBus eventBus, Clock clock)
    {
        Title = "Neural CPU - Clock Monitor (Esc to quit)";

        // Inject dependencies
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        // Subscribe to events
        _clockSubscription = _eventBus.Subscribe<ClockPhaseChanged>(this);
        _interruptSubscription = _eventBus.Subscribe<HardwareInterrupt>(this);

        // Create status panel
        var statusFrame = new FrameView()
        {
            Title = "Clock Status",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 8
        };

        _statusLabel = new Label()
        {
            Text = "Status: Stopped",
            X = 1,
            Y = 0
        };

        _cycleLabel = new Label()
        {
            Text = "Cycle: 0",
            X = 1,
            Y = 1
        };

        _phaseLabel = new Label()
        {
            Text = "Phase: Low",
            X = 1,
            Y = 2
        };

        _frequencyLabel = new Label()
        {
            Text = "Frequency: 1 Hz",
            X = 1,
            Y = 3
        };

        _interruptLabel = new Label()
        {
            Text = "Pending Interrupts: 0",
            X = 1,
            Y = 4
        };

        statusFrame.Add(_statusLabel, _cycleLabel, _phaseLabel, _frequencyLabel, _interruptLabel);

        // Create control panel
        var controlFrame = new FrameView()
        {
            Title = "Controls",
            X = 0,
            Y = Pos.Bottom(statusFrame),
            Width = Dim.Fill(),
            Height = 3
        };

        var startButton = new Button()
        {
            Text = "_Start",
            X = 1,
            Y = 0
        };
        startButton.Accepting += (s, e) =>
        {
            _clock.Start();
            UpdateStatus();
        };

        var stopButton = new Button()
        {
            Text = "Sto_p",
            X = Pos.Right(startButton) + 2,
            Y = 0
        };
        stopButton.Accepting += (s, e) =>
        {
            _clock.Stop();
            UpdateStatus();
        };

        var stepButton = new Button()
        {
            Text = "Step",
            X = Pos.Right(stopButton) + 2,
            Y = 0
        };
        stepButton.Accepting += (s, e) =>
        {
            _clock.Step();
            UpdateStatus();
        };

        var decButton = new Button()
        {
            Text = "-",
            X = Pos.Right(stepButton) + 4,
            Y = 0
        };
        decButton.Accepting += (s, e) =>
        {
            _clock.FrequencyHz = Math.Max(_clock.FrequencyHz - 1, 1);
            UpdateStatus();
        };

        var incButton = new Button()
        {
            Text = "+",
            X = Pos.Right(decButton) + 1,
            Y = 0
        };
        incButton.Accepting += (s, e) =>
        {
            _clock.FrequencyHz = Math.Min(_clock.FrequencyHz + 1, 100);
            UpdateStatus();
        };

        var haltButton = new Button()
        {
            Text = "_Halt",
            X = Pos.Right(incButton) + 4,
            Y = 0
        };
        haltButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new HardwareInterrupt(Clock.INTERRUPT_HALT, "HALT signal"));
            LogEvent("HALT interrupt sent (Vector 0x00)");
            UpdateStatus();
        };

        var interruptButton = new Button()
        {
            Text = "_Interrupt",
            X = Pos.Right(haltButton) + 2,
            Y = 0
        };
        interruptButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new HardwareInterrupt(0x10, "Manual interrupt"));
            LogEvent("Interrupt sent (Vector 0x10)");
            UpdateStatus();
        };

        controlFrame.Add(startButton, stopButton, stepButton, decButton, incButton, haltButton, interruptButton);

        // Create event log
        var logFrame = new FrameView()
        {
            Title = "Event Log",
            X = 0,
            Y = Pos.Bottom(controlFrame),
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _eventLog = new ListView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        _eventLog.SetSource(_eventLogItems);

        logFrame.Add(_eventLog);

        Add(statusFrame, controlFrame, logFrame);

        // Hook into window closing to ensure cleanup
        Closing += OnClosing;

        LogEvent("Neural CPU initialized");
        UpdateStatus();
    }

    #endregion

    #region Event Handlers

    private void OnClosing(object? sender, ToplevelClosingEventArgs e)
    {
        Shutdown();
    }

    #endregion

    #region Methods

    private void UpdateStatus()
    {
        Application.Invoke(() =>
        {
            _statusLabel.Text = $"Status: {(_clock.IsRunning ? "Running" : "Stopped")}";
            _cycleLabel.Text = $"Cycle: {_clock.CycleCount}";
            _phaseLabel.Text = $"Phase: {_clock.CurrentPhase}";
            _frequencyLabel.Text = $"Frequency: {_clock.FrequencyHz} Hz";
            _interruptLabel.Text = $"Pending Interrupts: {_clock.PendingInterrupts}";
        });
    }

    private void LogEvent(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var logEntry = $"[{timestamp}] {message}";

        Application.Invoke(() =>
        {
            _eventLogItems.Add(logEntry);

            // Keep log size manageable
            while (_eventLogItems.Count > MaxEventLogItems)
            {
                _eventLogItems.RemoveAt(0);
            }

            if (_eventLogItems.Count > 0)
            {
                _eventLog.SelectedItem = _eventLogItems.Count - 1;
                _eventLog.EnsureSelectedItemVisible();
            }
        });
    }

    public void Shutdown()
    {
        // Stop the clock (hosted service will dispose it)
        _clock.Stop();

        // Dispose subscriptions
        _clockSubscription.Dispose();
        _interruptSubscription.Dispose();
    }

    #endregion

    #region IObserver<ClockPhaseChanged> Implementation

    void IObserver<ClockPhaseChanged>.OnNext(ClockPhaseChanged value)
    {
        var phaseSymbol = value.IsRisingEdge ? "↑" : "↓";
        LogEvent($"{phaseSymbol} Clock {value.Phase} (Cycle {value.CycleCount})");
        UpdateStatus();
    }

    void IObserver<ClockPhaseChanged>.OnError(Exception error)
    {
        LogEvent($"ERROR: {error.Message}");
    }

    void IObserver<ClockPhaseChanged>.OnCompleted()
    {
        LogEvent("Clock observer completed");
    }

    #endregion

    #region IObserver<HardwareInterrupt> Implementation

    void IObserver<HardwareInterrupt>.OnNext(HardwareInterrupt value)
    {
        LogEvent($"Interrupt received: Vector={value.Vector:X2}");
        UpdateStatus();
    }

    void IObserver<HardwareInterrupt>.OnError(Exception error)
    {
        LogEvent($"ERROR: {error.Message}");
    }

    void IObserver<HardwareInterrupt>.OnCompleted()
    {
        LogEvent("Interrupt observer completed");
    }

    #endregion
}
