using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeuralCpu.Core.Components;
using NeuralCpu.Core.Configuration;
using NeuralCpu.Core.Events;
using NeuralCpu.Network;
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
    private readonly Stack _stack;
    private readonly Memory _memory;
    private readonly MockNetwork _mockNetwork;
    private readonly StateVectorBuilder _stateVectorBuilder;
    private readonly TaskRunner _taskRunner;
    private readonly NeuralCpuOptions _options;
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
    private readonly ListView _stackView;
    private readonly ListView _memoryView;
    private ListView _networkView = null!;

    private readonly ObservableCollection<string> _eventLogItems = new();
    private readonly ObservableCollection<string> _stackItems = new();
    private readonly ObservableCollection<string> _memoryItems = new();
    private readonly ObservableCollection<string> _networkLogItems = new();
    private ulong _memoryViewAddress;

    private bool _networkEnabled = false;
    private CheckBox? _networkEnabledCheckbox;
    private RadioGroup? _taskRadioGroup;
    private Label? _networkStatusLabel;

    #endregion

    #region Constructors

    public NeuralCpuApp(
        IEventBus eventBus,
        Clock clock,
        RegisterFile registerFile,
        Alu alu,
        Stack stack,
        Memory memory,
        MockNetwork mockNetwork,
        StateVectorBuilder stateVectorBuilder,
        TaskRunner taskRunner,
        IOptions<NeuralCpuOptions> options)
    {
        Title = "Neural CPU - Clock Monitor (Esc to quit)";

        // Inject dependencies
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _registerFile = registerFile ?? throw new ArgumentNullException(nameof(registerFile));
        _alu = alu ?? throw new ArgumentNullException(nameof(alu));
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _mockNetwork = mockNetwork ?? throw new ArgumentNullException(nameof(mockNetwork));
        _stateVectorBuilder = stateVectorBuilder ?? throw new ArgumentNullException(nameof(stateVectorBuilder));
        _taskRunner = taskRunner ?? throw new ArgumentNullException(nameof(taskRunner));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        // Initialize memory view address from configuration
        _memoryViewAddress = _options.UI.MemoryView.InitialAddress;

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
            Text = "Flags: -------- (0x0000000000000000)",
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
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
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
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
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

        var negButton = new Button()
        {
            Text = "NEG R0→R1",
            X = 1,
            Y = 4,
            ColorScheme = buttonColorScheme
        };
        negButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new AluUnaryCommand(AluOp.Neg, RegisterId.R0, RegisterId.R1));
            LogEvent("ALU: NEG R0 → R1");
        };

        var absButton = new Button()
        {
            Text = "ABS R1→R2",
            X = Pos.Right(negButton) + 2,
            Y = 4,
            ColorScheme = buttonColorScheme
        };
        absButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new AluUnaryCommand(AluOp.Abs, RegisterId.R1, RegisterId.R2));
            LogEvent("ALU: ABS R1 → R2");
        };

        var clzButton = new Button()
        {
            Text = "CLZ R0→R3",
            X = Pos.Right(absButton) + 2,
            Y = 4,
            ColorScheme = buttonColorScheme
        };
        clzButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new AluUnaryCommand(AluOp.Clz, RegisterId.R0, RegisterId.R3));
            LogEvent("ALU: CLZ R0 → R3");
        };

        aluTestFrame.Add(addButton, mulButton, divButton, xorButton, notButton, shlButton, rolButton, aluIncButton, aluDecButton, negButton, absButton, clzButton);

        // Create stack test panel
        var stackTestFrame = new FrameView()
        {
            Title = "Stack Tests",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        var pushR0Button = new Button()
        {
            Text = "PUSH R0",
            X = 1,
            Y = 0,
            ColorScheme = buttonColorScheme
        };
        pushR0Button.Accepting += (s, e) =>
        {
            _eventBus.Publish(new PushCommand(RegisterId.R0));
            LogEvent("STACK: PUSH R0");
        };

        var pushImmButton = new Button()
        {
            Text = "PUSH 0x1234",
            X = Pos.Right(pushR0Button) + 2,
            Y = 0,
            ColorScheme = buttonColorScheme
        };
        pushImmButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new PushImmediateCommand(0x1234));
            LogEvent("STACK: PUSH immediate 0x1234");
        };

        var popR1Button = new Button()
        {
            Text = "POP R1",
            X = Pos.Right(pushImmButton) + 2,
            Y = 0,
            ColorScheme = buttonColorScheme
        };
        popR1Button.Accepting += (s, e) =>
        {
            _eventBus.Publish(new PopCommand(RegisterId.R1));
            LogEvent("STACK: POP → R1");
        };

        var peekR2Button = new Button()
        {
            Text = "PEEK R2",
            X = Pos.Right(popR1Button) + 2,
            Y = 0,
            ColorScheme = buttonColorScheme
        };
        peekR2Button.Accepting += (s, e) =>
        {
            _eventBus.Publish(new PeekCommand(RegisterId.R2));
            LogEvent("STACK: PEEK → R2");
        };

        var dupButton = new Button()
        {
            Text = "DUP",
            X = Pos.Right(peekR2Button) + 2,
            Y = 0,
            ColorScheme = buttonColorScheme
        };
        dupButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new DupCommand());
            LogEvent("STACK: DUP top value");
        };

        stackTestFrame.Add(pushR0Button, pushImmButton, popR1Button, peekR2Button, dupButton);

        // Create memory test panel
        var memoryTestFrame = new FrameView()
        {
            Title = "Memory Tests",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        var storeDirectButton = new Button()
        {
            Text = "STORE R0→[0x100]",
            X = 1,
            Y = 0,
            ColorScheme = buttonColorScheme
        };
        storeDirectButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new StoreCommand(0x100, RegisterId.R0));
            LogEvent("MEMORY: STORE R0 → [0x100]");
        };

        var loadDirectButton = new Button()
        {
            Text = "LOAD [0x100]→R1",
            X = Pos.Right(storeDirectButton) + 2,
            Y = 0,
            ColorScheme = buttonColorScheme
        };
        loadDirectButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new LoadCommand(0x100, RegisterId.R1));
            LogEvent("MEMORY: LOAD [0x100] → R1");
        };

        var storeIndirectButton = new Button()
        {
            Text = "STORE R1→[R2]",
            X = Pos.Right(loadDirectButton) + 2,
            Y = 0,
            ColorScheme = buttonColorScheme
        };
        storeIndirectButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new StoreIndirectCommand(RegisterId.R2, RegisterId.R1));
            LogEvent("MEMORY: STORE indirect R1 → [R2]");
        };

        var loadIndirectButton = new Button()
        {
            Text = "LOAD [R2]→R3",
            X = Pos.Right(storeIndirectButton) + 2,
            Y = 0,
            ColorScheme = buttonColorScheme
        };
        loadIndirectButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new LoadIndirectCommand(RegisterId.R2, RegisterId.R3));
            LogEvent("MEMORY: LOAD indirect [R2] → R3");
        };

        memoryTestFrame.Add(storeDirectButton, loadDirectButton, storeIndirectButton, loadIndirectButton);

        // Create TabView for test panels (Register, ALU, Stack, Memory)
        var testTabView = new TabView()
        {
            X = 0,
            Y = Pos.Bottom(controlFrame),
            Width = Dim.Fill(),
            Height = 6
        };

        var registerTestTab = new Tab()
        {
            DisplayText = "Register Tests",
            View = testFrame
        };

        var aluTestTab = new Tab()
        {
            DisplayText = "ALU Tests",
            View = aluTestFrame
        };

        var stackTestTab = new Tab()
        {
            DisplayText = "Stack Tests",
            View = stackTestFrame
        };

        var memoryTestTab = new Tab()
        {
            DisplayText = "Memory Tests",
            View = memoryTestFrame
        };

        testTabView.AddTab(registerTestTab, false);
        testTabView.AddTab(aluTestTab, false);
        testTabView.AddTab(stackTestTab, false);
        testTabView.AddTab(memoryTestTab, false);

        // Create TabView for logs, stack, and memory display
        var tabView = new TabView()
        {
            X = 0,
            Y = Pos.Bottom(testTabView),
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        // Event Log tab (ListView handles scrolling automatically)
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

        var eventLogTab = new Tab()
        {
            DisplayText = "Event Log",
            View = _eventLog
        };

        // Stack display tab (ListView handles scrolling automatically)
        _stackView = new ListView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            AllowsMarking = false,
            AllowsMultipleSelection = false
        };
        _stackView.SetSource(_stackItems);

        var stackTab = new Tab()
        {
            DisplayText = "Stack",
            View = _stackView
        };

        // Memory display tab (ListView handles scrolling automatically)
        _memoryView = new ListView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            AllowsMarking = false,
            AllowsMultipleSelection = false
        };
        _memoryView.SetSource(_memoryItems);

        var memoryTab = new Tab()
        {
            DisplayText = "Memory",
            View = _memoryView
        };

        // Network control tab
        var networkTabView = CreateNetworkTab();
        var networkTab = new Tab()
        {
            DisplayText = "Network",
            View = networkTabView
        };

        // Add tabs to TabView
        tabView.AddTab(eventLogTab, false);
        tabView.AddTab(stackTab, false);
        tabView.AddTab(memoryTab, false);
        tabView.AddTab(networkTab, false);

        // Listen for tab changes to update displays
        tabView.SelectedTabChanged += (s, e) =>
        {
            if (tabView.SelectedTab == stackTab)
            {
                UpdateStackDisplay();
            }
            else if (tabView.SelectedTab == memoryTab)
            {
                UpdateMemoryDisplay();
            }
        };

        // Also update displays periodically when on respective tabs
        var displayUpdateTimer = new System.Timers.Timer(_options.UI.DisplayUpdateIntervalMs);
        displayUpdateTimer.Elapsed += (s, e) =>
        {
            if (tabView.SelectedTab == stackTab)
            {
                UpdateStackDisplay();
            }
            else if (tabView.SelectedTab == memoryTab)
            {
                UpdateMemoryDisplay();
            }
        };
        displayUpdateTimer.Start();

        Add(statusFrame, registerFrame, controlFrame, testTabView, tabView);

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
            while (_eventLogItems.Count > _options.UI.MaxEventLogItems)
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

    private void UpdateStackDisplay()
    {
        // Build the new list outside the UI thread to avoid race conditions
        var newItems = new List<string>();

        if (_stack.Count == 0)
        {
            newItems.Add("(Stack is empty)");
        }
        else
        {
            newItems.Add($"Stack depth: {_stack.Count} / 65536");
            newItems.Add(" ");  // Use space instead of empty string
            newItems.Add("Index  Value");
            newItems.Add("-----  ----------------");

            // Get stack snapshot (top to bottom)
            var snapshot = _stack.GetSnapshot();

            // Show top 50 items (most recent pushes first)
            var itemsToShow = Math.Min(snapshot.Length, 50);
            for (int i = 0; i < itemsToShow; i++)
            {
                newItems.Add($"{i,5}  0x{snapshot[i]:X16}");
            }

            if (snapshot.Length > 50)
            {
                newItems.Add(" ");  // Use space instead of empty string
                newItems.Add($"... and {snapshot.Length - 50} more items");
            }
        }

        // Now update the observable collection atomically on the UI thread
        Application.Invoke(() =>
        {
            // Replace items one by one to avoid Clear() causing ListView confusion
            for (int i = 0; i < newItems.Count; i++)
            {
                if (i < _stackItems.Count)
                {
                    _stackItems[i] = newItems[i];
                }
                else
                {
                    _stackItems.Add(newItems[i]);
                }
            }

            // Remove any extra items if new list is shorter
            while (_stackItems.Count > newItems.Count)
            {
                _stackItems.RemoveAt(_stackItems.Count - 1);
            }
        });
    }

    private void UpdateMemoryDisplay()
    {
        // Build the new list outside the UI thread to avoid race conditions
        var newItems = new List<string>();

        // Header
        newItems.Add($"Memory: {_memory.CapacityWords} words ({_memory.CapacityBytes} bytes)");
        newItems.Add($"Viewing from address: 0x{_memoryViewAddress:X8}");
        newItems.Add(" ");
        newItems.Add("Addr     +0               +1               +2               +3               ASCII");
        newItems.Add("-------  ----------------  ----------------  ----------------  ----------------  ----------------");

        // Get memory snapshot from configuration
        var wordsPerRow = _options.UI.MemoryView.WordsPerRow;
        var rowsToShow = _options.UI.MemoryView.RowsToShow;
        var totalWords = wordsPerRow * rowsToShow;

        var snapshot = _memory.GetSnapshot(_memoryViewAddress, totalWords);

        for (int row = 0; row < rowsToShow && row * wordsPerRow < snapshot.Length; row++)
        {
            var rowAddress = _memoryViewAddress + (ulong)(row * wordsPerRow);
            var line = new System.Text.StringBuilder();
            line.Append($"{rowAddress:X7}: ");

            // Hex values (4 ulongs per row)
            var asciiBytes = new List<byte>();
            for (int col = 0; col < wordsPerRow; col++)
            {
                var idx = row * wordsPerRow + col;
                if (idx < snapshot.Length)
                {
                    var value = snapshot[idx];
                    var highlight = _memory.WasRecentlyChanged(rowAddress + (ulong)col);
                    line.Append($" {value:X16}");

                    // Collect bytes for ASCII representation (little-endian)
                    for (int b = 0; b < 8; b++)
                    {
                        asciiBytes.Add((byte)((value >> (b * 8)) & 0xFF));
                    }
                }
                else
                {
                    line.Append(" ................");
                }
            }

            // ASCII representation (32 bytes for 4 ulongs)
            line.Append("  ");
            foreach (var b in asciiBytes)
            {
                // Printable ASCII or dot
                line.Append(b >= 32 && b <= 126 ? (char)b : '.');
            }

            newItems.Add(line.ToString());
        }

        newItems.Add(" ");
        newItems.Add("Controls: Use memory test buttons to modify memory");

        // Now update the observable collection atomically on the UI thread
        Application.Invoke(() =>
        {
            // Replace items one by one to avoid Clear() causing ListView confusion
            for (int i = 0; i < newItems.Count; i++)
            {
                if (i < _memoryItems.Count)
                {
                    _memoryItems[i] = newItems[i];
                }
                else
                {
                    _memoryItems.Add(newItems[i]);
                }
            }

            // Remove any extra items if new list is shorter
            while (_memoryItems.Count > newItems.Count)
            {
                _memoryItems.RemoveAt(_memoryItems.Count - 1);
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

    #region Network Tab

    private View CreateNetworkTab()
    {
        var container = new View()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        // Network controls frame
        var controlsFrame = new FrameView()
        {
            Title = "Network Controls",
            X = 0,
            Y = 0,
            Width = Dim.Percent(50),
            Height = 15
        };

        // Enable checkbox
        _networkEnabledCheckbox = new CheckBox()
        {
            X = 1,
            Y = 0,
            Text = "Enable Network Control"
        };
        // Note: CheckBox doesn't have simple events in Terminal.Gui v2 - manage state manually
        controlsFrame.Add(_networkEnabledCheckbox);

        // Task selection
        var taskLabel = new Label()
        {
            X = 1,
            Y = 2,
            Text = "Task:"
        };
        controlsFrame.Add(taskLabel);

        _taskRadioGroup = new RadioGroup()
        {
            X = 1,
            Y = 3,
            RadioLabels = new[] { "Echo (LOAD/STORE)", "Increment (ALU)", "Reverse (Stack)" },
            SelectedItem = 0
        };
        _taskRadioGroup.SelectedItemChanged += (s, e) =>
        {
            if (_taskRadioGroup != null)
                _mockNetwork.CurrentTask = (MockNetworkTask)_taskRadioGroup.SelectedItem;
            LogNetworkEvent($"Task changed to: {_mockNetwork.CurrentTask}");
        };
        controlsFrame.Add(_taskRadioGroup);

        // Run Episode button
        var runEpisodeButton = new Button()
        {
            X = 1,
            Y = 7,
            Text = "Run Episode"
        };
        runEpisodeButton.Accepting += (s, e) => OnRunEpisodeClicked();
        controlsFrame.Add(runEpisodeButton);

        // Stop button
        var stopButton = new Button()
        {
            X = 20,
            Y = 7,
            Text = "Stop"
        };
        stopButton.Accepting += (s, e) =>
        {
            _clock.Stop();
            _networkEnabled = false;
            UpdateNetworkStatus();
            LogNetworkEvent("Episode stopped manually");
        };
        controlsFrame.Add(stopButton);

        // Reset button
        var resetButton = new Button()
        {
            X = 30,
            Y = 7,
            Text = "Reset CPU"
        };
        resetButton.Accepting += (s, e) =>
        {
            _eventBus.Publish(new HardwareInterrupt(Clock.INTERRUPT_RESET));
            _stateVectorBuilder.Reset();
            _mockNetwork.Reset();
            LogNetworkEvent("CPU and network reset");
        };
        controlsFrame.Add(resetButton);

        // Network status
        _networkStatusLabel = new Label()
        {
            X = 1,
            Y = 9,
            Text = "Status: Disabled"
        };
        controlsFrame.Add(_networkStatusLabel);

        container.Add(controlsFrame);

        // Task info frame
        var infoFrame = new FrameView()
        {
            Title = "Task Information",
            X = Pos.Right(controlsFrame) + 1,
            Y = 0,
            Width = Dim.Fill(),
            Height = 15
        };

        var infoText = new TextView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            Text = GetTaskInfo()
        };
        infoFrame.Add(infoText);
        container.Add(infoFrame);

        // Network log frame
        var logFrame = new FrameView()
        {
            Title = "Network Log",
            X = 0,
            Y = 15,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _networkView = new ListView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            AllowsMarking = false,
            AllowsMultipleSelection = false
        };
        _networkView.SetSource(_networkLogItems);
        logFrame.Add(_networkView);
        container.Add(logFrame);

        return container;
    }

    private void OnRunEpisodeClicked()
    {
        if (_clock.IsRunning)
        {
            LogNetworkEvent("ERROR: Clock is already running. Stop it first.");
            return;
        }

        LogNetworkEvent($"Starting episode: {_mockNetwork.CurrentTask}");

        // Prepare task input
        var task = CreateTaskDefinition(_mockNetwork.CurrentTask);

        // Run episode in background
        Task.Run(() =>
        {
            try
            {
                var result = _taskRunner.RunEpisode(task);

                Application.Invoke(() =>
                {
                    LogNetworkEvent($"Episode complete!");
                    LogNetworkEvent($"  Success: {result.Success}");
                    LogNetworkEvent($"  Reward: {result.Reward:F2}");
                    LogNetworkEvent($"  Cycles: {result.CycleCount}");
                    LogNetworkEvent($"  Halted: {result.Halted}");
                    LogNetworkEvent($"  Output: [{string.Join(", ", result.Output.Take(10))}...]");
                });
            }
            catch (Exception ex)
            {
                Application.Invoke(() =>
                {
                    LogNetworkEvent($"ERROR: {ex.Message}");
                });
            }
        });
    }

    private TaskDefinition CreateTaskDefinition(MockNetworkTask task)
    {
        return task switch
        {
            MockNetworkTask.Echo => new TaskDefinition
            {
                Name = "Echo",
                Input = new ulong[] { 1, 2, 3, 4, 5 },
                ExpectedOutput = new ulong[] { 1, 2, 3, 4, 5 }
            },
            MockNetworkTask.Increment => new TaskDefinition
            {
                Name = "Increment",
                Input = new ulong[] { 10, 20, 30, 40, 50 },
                ExpectedOutput = new ulong[] { 11, 21, 31, 41, 51 }
            },
            MockNetworkTask.Reverse => new TaskDefinition
            {
                Name = "Reverse",
                Input = new ulong[] { 1, 2, 3, 4, 5 },
                ExpectedOutput = new ulong[] { 5, 4, 3, 2, 1 }
            },
            _ => throw new InvalidOperationException($"Unknown task: {task}")
        };
    }

    private string GetTaskInfo()
    {
        return @"Memory-Mapped I/O:
  0x80000000 - INPUT_BUFFER (task input data)
  0x80000FF8 - INPUT_LENGTH (number of items)
  0x80001000 - OUTPUT_BUFFER (task output)
  0x80001FF8 - OUTPUT_LENGTH (items written)

Tasks:
  Echo: Copy input to output
    Tests: LOAD/STORE operations

  Increment: Add 1 to each value
    Tests: ALU operations (INC)

  Reverse: Reverse input sequence
    Tests: Stack operations (PUSH/POP)

The network observes CPU state and emits
control signals (events) each cycle.";
    }

    private void UpdateNetworkStatus()
    {
        var status = _networkEnabled ? "Enabled" : "Disabled";
        var task = _mockNetwork.CurrentTask.ToString();
        if (_networkStatusLabel != null)
            _networkStatusLabel.Text = $"Status: {status} | Task: {task}";
    }

    private void LogNetworkEvent(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        _networkLogItems.Add($"[{timestamp}] {message}");

        // Keep last 100 items
        while (_networkLogItems.Count > 100)
        {
            _networkLogItems.RemoveAt(0);
        }
    }

    #endregion
}
