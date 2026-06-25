# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Neural CPU is an experimental architecture exploring continuity in neural networks by having a transformer learn to **control** a virtual CPU rather than generate text directly. The transformer outputs control signals that orchestrate CPU components (registers, ALU, memory) via a message-passing event bus, creating genuine state persistence and feedback loops across inference passes.

This enables:
- **Continuous state** maintained by the CPU between transformer invocations
- **Self-reflection** through reading CPU state and adjusting control strategy
- **Task-driven attention** based on current CPU state rather than fixed architectural patterns

## Architecture

The project follows a **microkernel message-passing architecture** with CPU semantics:

**Event Bus** (`NeuralCpu.Core/Events/`)
- Global message broker using the Observer pattern (`IObserver<T>`)
- Thread-safe event queue for inter-component communication
- Each component publishes/subscribes to typed events

**Components** (`NeuralCpu.Core/Components/`)
- Each major component runs on its own managed task
- Components communicate exclusively via the event bus
- Clock module is the first component (generates timing signals)

**Threading Model**
- Microkernel-style: each component gets its own task with `CancellationToken` for clean shutdown
- No shared mutable state between components (only via event messages)
- Centralized task lifecycle management via `Microsoft.Extensions.Hosting`

**Dependency Injection**
- Uses `Microsoft.Extensions.DependencyInjection` for component registration
- `AddNeuralCpu()` extension method registers core services (event bus, clock, hosted services)
- Components receive dependencies via constructor injection
- Lifecycle managed by `IHostedService` implementations

**Logging**
- Uses Serilog with `Microsoft.Extensions.Logging` integration
- Rolling file logs in `logs/` directory (excluded from git)
- Daily rolling interval with 10MB file size limit
- Retains 30 days of logs
- Logs to both file and console
- Timestamped format: `yyyy-MM-dd HH:mm:ss.fff zzz [LEVEL] Message`

**Key Design Principles**
- Components are deterministic and observable
- Neural network will eventually replace the control plane (not implemented yet)
- Event-driven rather than direct method calls
- CPU datapath (ALU, registers, memory) will be fixed; control signals learned by the network

## Development Commands

### Build
```bash
# Build entire solution
dotnet build neural-cpu.slnx

# Build specific project
dotnet build src/NeuralCpu.Core/NeuralCpu.Core.csproj
dotnet build src/NeuralCpu.Console/NeuralCpu.Console.csproj
```

### Run
```bash
# Run the console test application (interactive)
dotnet run --project src/NeuralCpu.Console/NeuralCpu.Console.csproj
```

**Console Controls:**
- `s` - Start clock
- `p` - Pause clock
- `SPACE` - Single step (manual cycle)
- `+`/`-` - Adjust frequency (Hz)
- `i` - Send test hardware interrupt
- `q` - Quit

### Project Structure
```
src/
  NeuralCpu.Core/              # Core library with event bus and components
    Events/                    # Event types and IEventBus interface
    Components/                # CPU components (Clock, RegisterFile, future: ALU, Memory)
    Services/                  # Hosted services for component lifecycle
    ServiceCollectionExtensions.cs  # DI registration extension methods
  NeuralCpu.Console/           # Test/debug TUI application
```

## Current Implementation Status

**Implemented:**
- Event bus with observer pattern
- Clock module with two-phase operation (rising/falling edges)
- Hardware interrupt queuing (HALT on 0x00, RESET on 0x01)
- Register file with 7 ulong registers (R0-R3, PC, SP, Flags)
- Register write queuing (synchronized to clock rising edge)
- Register read responses (immediate, no clock sync needed)
- Configurable frequency control
- Start/Stop/Step controls
- Microsoft.Extensions.Hosting integration
- Dependency injection via `AddNeuralCpu()` extension method
- Serilog logging with rolling file logs (daily rotation, 10MB limit, 30-day retention)
- Terminal.Gui v2 TUI with real-time monitoring of clock and registers

**Not Yet Implemented:**
- ALU (arithmetic/logic operations)
- Memory (RAM)
- Neural network control plane (the key innovation)

## Coding Standards

### Code Organization
- Use `#region` blocks to group code sections in this order:
  1. Constants
  2. Fields
  3. Constructors
  4. Properties
  5. Methods

### Event Messages
- **Use `record` types for all event bus messages** - they're immutable, concise, and provide structural equality
- Use primary constructors for brevity
- Example:
```csharp
public sealed record ClockPhaseChanged(
    ClockPhase Phase,
    ulong CycleCount,
    DateTimeOffset Timestamp)
{
    public bool IsRisingEdge => Phase == ClockPhase.High;
    public bool IsFallingEdge => Phase == ClockPhase.Low;
}
```

### Components
- Use primary constructors when appropriate to reduce boilerplate
- Group related members with `#region` for clarity
- Always implement `IDisposable` for proper cleanup
- Subscribe to events in constructor, unsubscribe in `Dispose()`

## Adding New Components

When adding a new CPU component:

1. Create the component class in `NeuralCpu.Core/Components/`
2. Implement `IDisposable` for cleanup
3. Subscribe to relevant events via `IEventBus.Subscribe<T>()`
4. Run component logic on its own task (use `Task.Run` with `CancellationToken`)
5. Publish events to communicate with other components
6. Observe the clock phases (rising/falling edge) if timing-sensitive

Example pattern:
```csharp
public sealed class MyComponent : IObserver<ClockPhaseChanged>, IDisposable
{
    private readonly IEventBus _eventBus;
    private readonly IDisposable _clockSubscription;
    
    public MyComponent(IEventBus eventBus)
    {
        _eventBus = eventBus;
        _clockSubscription = _eventBus.Subscribe<ClockPhaseChanged>(this);
    }
    
    void IObserver<ClockPhaseChanged>.OnNext(ClockPhaseChanged clock)
    {
        if (clock.IsRisingEdge)
        {
            // React to rising edge
        }
    }
    
    public void Dispose()
    {
        _clockSubscription.Dispose();
    }
}
```

## Clock Module Details

The Clock ([Clock.cs](src/NeuralCpu.Core/Components/Clock.cs)) generates timing signals with:
- **Two-phase operation**: Publishes `ClockPhaseChanged` for both rising and falling edges
- **Cycle counting**: Increments on each rising edge
- **Frequency control**: Adjustable Hz (changes take effect next cycle)
- **Hardware interrupts**: Queues `HardwareInterrupt` events and processes at cycle boundaries
- **Manual stepping**: `Step()` method for single-cycle execution (debugging)
- **HALT interrupt**: Vector 0x00 stops the clock if it is currently running
- **RESET interrupt**: Vector 0x01 publishes `SystemReset` to all components

Components should subscribe to `ClockPhaseChanged` and react based on `IsRisingEdge`/`IsFallingEdge` properties.

### Interrupt Vectors

- `0x00` - **HALT**: Stops the clock if running (constant: `Clock.INTERRUPT_HALT`)
- `0x01` - **RESET**: System-wide reset (constant: `Clock.INTERRUPT_RESET`)

## Register File Details

The RegisterFile ([RegisterFile.cs](src/NeuralCpu.Core/Components/RegisterFile.cs)) contains all CPU registers:

**Registers (all `ulong`, 64-bit):**
- **R0, R1, R2, R3** - General purpose registers (reset to 0)
- **PC** (Program Counter) - Points to next instruction (reset to 0)
- **SP** (Stack Pointer) - Points to top of stack (reset to 0)
- **Flags** - Status flags (Zero, Carry, Negative, Overflow - reset to 0)

**Operation:**
- **Simple writes**: `RegisterWriteCommand` - writes value without updating flags
- **Writes with flags**: `RegisterWriteWithFlagsCommand` - writes value and updates Zero/Negative flags
- **Register copy**: `RegisterCopyCommand` - copies value from source to destination (like physical CPU read-enable + write-enable)
- **Reads**: Immediate response via `RegisterReadRequest`/`RegisterReadResponse` (no clock sync needed)
- **PC auto-increment**: PC increments every clock cycle to give network sense of time passing
- **Change notification**: Publishes `RegisterChanged` after each write with old/new values
- **Reset**: Subscribes to `SystemReset` event, restores all registers to zero

All write operations are queued and execute on the clock rising edge (synchronized).

**Direct access** (for debugging only): `registerFile[RegisterId.R0]` returns current value

## CPU Flags

The Flags register ([CpuFlags.cs](src/NeuralCpu.Core/Components/CpuFlags.cs)) uses individual bits for status flags:

**Flag Bits:**
- **Bit 0 - Zero (Z)**: Set when result is zero
- **Bit 1 - Carry (C)**: Set when unsigned overflow occurs
- **Bit 2 - Negative (N)**: Set when result is negative (MSB is 1)
- **Bit 3 - Overflow (O)**: Set when signed overflow occurs

**Helper Methods:**
- `flagsValue.HasFlag(CpuFlags.Zero)` - Check if flag is set
- `flagsValue.SetFlag(CpuFlags.Carry, true)` - Set or clear flag
- `flagsValue.ToFlagString()` - Format as "ZCNO" or "z-n-" (uppercase = set, dash = clear)

**TUI Display:** Shows both symbolic (`ZCNO` or `----`) and hex representations

## Future Direction

The next major milestone is integrating a neural network as the **control plane**:
- Network reads CPU state (registers, flags, memory)
- Network outputs control signals (ALU operation, register enables, memory read/write)
- Network observes results and decides next control action
- This creates a learned instruction set rather than hardcoded decode logic

This architecture enables genuine continuity and self-reflection in the neural network.
