using System.Collections.ObjectModel;
using NeuralCpu.Core.Components;
using NeuralCpu.Core.Events;
using Terminal.Gui;

namespace NeuralCpu.Console;

/// <summary>
/// Main TUI application for the Neural CPU simulator.
/// </summary>
internal sealed class NeuralCpuApp : Window,
                                     IObserver<ClockPhaseChanged>,
                                     IObserver<HardwareInterrupt>,
                                     IObserver<RegisterChanged>
{
    #region Fields

    private readonly IEventBus _eventBus;
    private readonly Clock _clock;
    private readonly RegisterFile _registerFile;
    private readonly Alu _alu;
    private readonly IDisposable _clockSubscription;
    private readonly IDisposable _interruptSubscription;
    private readonly IDisposable _registerSubscription;

    private readonly Label _statusLabel;
    private readonly Label _cycleLabel;
    private readonly Label _frequencyLabel;
    private readonly Label _phaseLabel;
    private readonly Label _interruptLabel;
    private readonly Label _r0Label;
    private readonly Label _r1Label;
    private readonly Label _r2Label;
    private readonly Label _r3Label;
    private readonly Label _pcLabel;
    private readonly Label _spLabel;
    private readonly Label _flagsLabel;
    private readonly ListView _eventLog;

    private readonly ObservableCollection<string> _eventLogItems = new();
    private const int MaxEventLogItems = 100;

    #endregion

    #region Constructors

    public NeuralCpuApp(IEventBus eventBus, Clock clock, RegisterFile registerFile, Alu alu)
    {
        Title = "Neural CPU - Clock Monitor (Esc to quit)";

        // Inject dependencies
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _registerFile = registerFile ?? throw new ArgumentNullException(nameof(registerFile));
        _alu = alu ?? throw new ArgumentNullException(nameof(alu));

        // Subscribe to events
        _clockSubscription = _eventBus.Subscribe<ClockPhaseChanged>(this);
        _interruptSubscription = _eventBus.Subscribe<HardwareInterrupt>(this);
        _registerSubscription = _eventBus.Subscribe<RegisterChanged>(this);

        // Create status panel (left side)
        var statusFrame = new FrameView()
        {
            Title = "Clock Status",
            X = 0,
            Y = 0,
            Width = Dim.Percent(50),
            Height = 9
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

        // Create register panel (right side)
        var registerFrame = new FrameView()
        {
            Title = "Registers",
            X = Pos.Right(statusFrame),
            Y = 0,
            Width = Dim.Fill(),
            Height = 9
        };

        _r0Label = new Label()
        {
            Text = "R0: 0x0000000000000000",
            X = 1,
            Y = 0
        };

        _r1Label = new Label()
        {
            Text = "R1: 0x0000000000000000",
            X = 1,
            Y = 1
        };

        _r2Label = new Label()
        {
            Text = "R2: 0x0000000000000000",
            X = 1,
            Y = 2
        };

        _r3Label = new Label()
        {
            Text = "R3: 0x0000000000000000",
            X = 1,
            Y = 3
        };

        _pcLabel = new Label()
        {
            Text = "PC: 0x0000000000000000",
            X = 1,
            Y = 4
        };

        _spLabel = new Label()
        {
            Text = "SP: 0x0000000000000000",
            X = 1,
            Y = 5
        };

        _flagsLabel = new Label()
        {
            Text = "Flags: ---- (0x0000000000000000)",
            X = 1,
            Y = 6
        };

        registerFrame.Add(_r0Label, _r1Label, _r2Label, _r3Label, _pcLabel, _spLabel, _flagsLabel);

        // Create control panel
        var controlFrame = new FrameView()
        {
            Title = "Controls",
            X = 0,
            Y = Pos.Bottom(statusFrame),
            Width = Dim.Fill(),
            Height = 4
        };

        // Define button style (distinct from blue background)
        var buttonColorScheme = new ColorScheme()
        {
            Normal = new Terminal.Gui.Attribute(Color.Black, Color.Gray),
            Focus = new Terminal.Gui.Attribute(Color.Black, Color.White),
            HotNormal = new Terminal.Gui.Attribute(Color.BrightCyan, Color.Gray),
            HotFocus = new Terminal.Gui.Attribute(Color.BrightCyan, Color.White)
        };

        var startButton = new Button()
        {
            Text = "_Start",
            X = 1,
            Y = 0,
            ColorScheme = buttonColorScheme
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
            Y = 0,
            ColorScheme = buttonColorScheme
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
            Y = 0,
            ColorScheme = buttonColorScheme
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
            Y = 0,
            ColorScheme = buttonColorScheme
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
            Y = 0,
            ColorScheme = buttonColorScheme
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
            Y = 0,
            ColorScheme = buttonColorScheme
        };
        haltButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new HardwareInterrupt(Clock.INTERRUPT_HALT, "HALT signal"));
            LogEvent("HALT interrupt sent (Vector 0x00)");
            UpdateStatus();
        };

        var resetButton = new Button()
        {
            Text = "_Reset",
            X = Pos.Right(haltButton) + 2,
            Y = 0,
            ColorScheme = buttonColorScheme
        };
        resetButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new HardwareInterrupt(Clock.INTERRUPT_RESET, "RESET signal"));
            LogEvent("RESET interrupt sent (Vector 0x01)");
            UpdateStatus();
        };

        controlFrame.Add(startButton, stopButton, stepButton, decButton, incButton, haltButton, resetButton);

        // Create register test panel
        var testFrame = new FrameView()
        {
            Title = "Register Tests",
            X = 0,
            Y = Pos.Bottom(controlFrame),
            Width = Dim.Fill(),
            Height = 6
        };

        var writeR0Button = new Button()
        {
            Text = "Write R0=0x42",
            X = 1,
            Y = 0,
            ColorScheme = buttonColorScheme
        };
        writeR0Button.Accepting += (s, e) =>
        {
            _eventBus.Publish(new RegisterWriteCommand(RegisterId.R0, 0x42));
            LogEvent("Write command: R0 = 0x0000000000000042");
        };

        var writeFlagsButton = new Button()
        {
            Text = "Write R1=0xFF (flags)",
            X = Pos.Right(writeR0Button) + 2,
            Y = 0,
            ColorScheme = buttonColorScheme
        };
        writeFlagsButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new RegisterWriteWithFlagsCommand(RegisterId.R1, 0xFF));
            LogEvent("Write with flags: R1 = 0x00000000000000FF");
        };

        var copyButton = new Button()
        {
            Text = "Copy R0→R2",
            X = Pos.Right(writeFlagsButton) + 2,
            Y = 0,
            ColorScheme = buttonColorScheme
        };
        copyButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new RegisterCopyCommand(RegisterId.R0, RegisterId.R2));
            LogEvent("Copy command: R0 → R2");
        };

        var writeZeroButton = new Button()
        {
            Text = "Write R3=0 (flags)",
            X = Pos.Right(copyButton) + 2,
            Y = 0,
            ColorScheme = buttonColorScheme
        };
        writeZeroButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new RegisterWriteWithFlagsCommand(RegisterId.R3, 0));
            LogEvent("Write with flags: R3 = 0 (should set Zero flag)");
        };

        var writeNegButton = new Button()
        {
            Text = "Write R3=MSB (flags)",
            X = 1,
            Y = 2,
            ColorScheme = buttonColorScheme
        };
        writeNegButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new RegisterWriteWithFlagsCommand(RegisterId.R3, 0x8000_0000_0000_0000));
            LogEvent("Write with flags: R3 = 0x8000000000000000 (should set Negative flag)");
        };

        testFrame.Add(writeR0Button, writeFlagsButton, copyButton, writeZeroButton, writeNegButton);

        // Create ALU test panel
        var aluTestFrame = new FrameView()
        {
            Title = "ALU Tests",
            X = 0,
            Y = Pos.Bottom(testFrame),
            Width = Dim.Fill(),
            Height = 6
        };

        var addButton = new Button()
        {
            Text = "ADD R0+R1→R2",
            X = 1,
            Y = 0,
            ColorScheme = buttonColorScheme
        };
        addButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new AluCommand(AluOp.Add, RegisterId.R0, RegisterId.R1, RegisterId.R2));
            LogEvent("ALU: ADD R0 + R1 → R2");
        };

        var mulButton = new Button()
        {
            Text = "MUL R0*R1→R3",
            X = Pos.Right(addButton) + 2,
            Y = 0,
            ColorScheme = buttonColorScheme
        };
        mulButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new AluCommand(AluOp.Mul, RegisterId.R0, RegisterId.R1, RegisterId.R3));
            LogEvent("ALU: MUL R0 * R1 → R3");
        };

        var divButton = new Button()
        {
            Text = "DIV R2/R0→R1",
            X = Pos.Right(mulButton) + 2,
            Y = 0,
            ColorScheme = buttonColorScheme
        };
        divButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new AluCommand(AluOp.Div, RegisterId.R2, RegisterId.R0, RegisterId.R1));
            LogEvent("ALU: DIV R2 / R0 → R1");
        };

        var xorButton = new Button()
        {
            Text = "XOR R0^R1→R2",
            X = Pos.Right(divButton) + 2,
            Y = 0,
            ColorScheme = buttonColorScheme
        };
        xorButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new AluCommand(AluOp.Xor, RegisterId.R0, RegisterId.R1, RegisterId.R2));
            LogEvent("ALU: XOR R0 ^ R1 → R2");
        };

        var notButton = new Button()
        {
            Text = "NOT R0→R1",
            X = 1,
            Y = 2,
            ColorScheme = buttonColorScheme
        };
        notButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new AluUnaryCommand(AluOp.Not, RegisterId.R0, RegisterId.R1));
            LogEvent("ALU: NOT R0 → R1");
        };

        var shlButton = new Button()
        {
            Text = "SHL R0<<4→R2",
            X = Pos.Right(notButton) + 2,
            Y = 2,
            ColorScheme = buttonColorScheme
        };
        shlButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new AluImmediateCommand(AluOp.Shl, RegisterId.R0, 4, RegisterId.R2));
            LogEvent("ALU: SHL R0 << 4 → R2");
        };

        var rolButton = new Button()
        {
            Text = "ROL R0<<<8→R3",
            X = Pos.Right(shlButton) + 2,
            Y = 2,
            ColorScheme = buttonColorScheme
        };
        rolButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new AluImmediateCommand(AluOp.Rol, RegisterId.R0, 8, RegisterId.R3));
            LogEvent("ALU: ROL R0 <<< 8 → R3");
        };

        var aluIncButton = new Button()
        {
            Text = "INC R2++→R2",
            X = Pos.Right(rolButton) + 2,
            Y = 2,
            ColorScheme = buttonColorScheme
        };
        aluIncButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new AluUnaryCommand(AluOp.Inc, RegisterId.R2, RegisterId.R2));
            LogEvent("ALU: INC R2++ → R2");
        };

        var aluDecButton = new Button()
        {
            Text = "DEC R3--→R3",
            X = Pos.Right(aluIncButton) + 2,
            Y = 2,
            ColorScheme = buttonColorScheme
        };
        aluDecButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new AluUnaryCommand(AluOp.Dec, RegisterId.R3, RegisterId.R3));
            LogEvent("ALU: DEC R3-- → R3");
        };

        aluTestFrame.Add(addButton, mulButton, divButton, xorButton, notButton, shlButton, rolButton, aluIncButton, aluDecButton);

        // Create event log
        var logFrame = new FrameView()
        {
            Title = "Event Log",
            X = 0,
            Y = Pos.Bottom(aluTestFrame),
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _eventLog = new ListView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            AllowsMarking = false,
            AllowsMultipleSelection = false
        };
        _eventLog.SetSource(_eventLogItems);

        // Add a simple vertical scrollbar indicator
        var scrollIndicator = new Label()
        {
            X = Pos.AnchorEnd(1),
            Y = 0,
            Width = 1,
            Height = Dim.Fill(),
            Text = "│"
        };

        logFrame.Add(_eventLog, scrollIndicator);

        Add(statusFrame, registerFrame, controlFrame, testFrame, aluTestFrame, logFrame);

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

            // Update register displays
            _r0Label.Text = $"R0: 0x{_registerFile[RegisterId.R0]:X16}";
            _r1Label.Text = $"R1: 0x{_registerFile[RegisterId.R1]:X16}";
            _r2Label.Text = $"R2: 0x{_registerFile[RegisterId.R2]:X16}";
            _r3Label.Text = $"R3: 0x{_registerFile[RegisterId.R3]:X16}";
            _pcLabel.Text = $"PC: 0x{_registerFile[RegisterId.PC]:X16}";
            _spLabel.Text = $"SP: 0x{_registerFile[RegisterId.SP]:X16}";

            var flagsValue = _registerFile[RegisterId.Flags];
            _flagsLabel.Text = $"Flags: {flagsValue.ToFlagString()} (0x{flagsValue:X16})";
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
        _registerSubscription.Dispose();
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

    #region IObserver<RegisterChanged> Implementation

    void IObserver<RegisterChanged>.OnNext(RegisterChanged value)
    {
        LogEvent($"Register {value.RegisterId}: 0x{value.OldValue:X16} → 0x{value.NewValue:X16}");
        UpdateStatus();
    }

    void IObserver<RegisterChanged>.OnError(Exception error)
    {
        LogEvent($"ERROR: {error.Message}");
    }

    void IObserver<RegisterChanged>.OnCompleted()
    {
        LogEvent("Register observer completed");
    }

    #endregion
}
