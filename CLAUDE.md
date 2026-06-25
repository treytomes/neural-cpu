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
- Centralized task lifecycle management

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
  NeuralCpu.Core/           # Core library with event bus and components
    Events/                 # Event types and IEventBus interface
    Components/             # CPU components (Clock, future: ALU, Memory, etc.)
  NeuralCpu.Console/        # Test/debug console application
```

## Current Implementation Status

**Implemented:**
- Event bus with observer pattern
- Clock module with two-phase operation (rising/falling edges)
- Hardware interrupt queuing
- Configurable frequency control
- Start/Stop/Step controls

**Not Yet Implemented:**
- Registers (accumulator, general-purpose, program counter, flags)
- ALU (arithmetic/logic operations)
- Memory (RAM)
- Bus architecture (data/address buses)
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

Components should subscribe to `ClockPhaseChanged` and react based on `IsRisingEdge`/`IsFallingEdge` properties.

## Future Direction

The next major milestone is integrating a neural network as the **control plane**:
- Network reads CPU state (registers, flags, memory)
- Network outputs control signals (ALU operation, register enables, memory read/write)
- Network observes results and decides next control action
- This creates a learned instruction set rather than hardcoded decode logic

This architecture enables genuine continuity and self-reflection in the neural network.
