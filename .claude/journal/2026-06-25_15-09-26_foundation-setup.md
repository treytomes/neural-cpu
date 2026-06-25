# Journal Entry - Foundation Setup

**Date:** 2026-06-25 15:09:26
**Session:** Initial project architecture and clock module implementation

## What We Accomplished

Built the foundational architecture for the Neural CPU project:

1. **Project Structure**: Created a clean .NET 9.0 solution with two projects:
   - `NeuralCpu.Core` - Core library with event bus and components
   - `NeuralCpu.Console` - Interactive test application for debugging

2. **Event Bus System**: Implemented a thread-safe message-passing architecture using the Observer pattern:
   - `IEventBus` interface with subscribe/publish operations
   - `EventBus` implementation using `IObserver<T>` and concurrent collections
   - Type-safe event subscriptions with disposable handles

3. **Clock Module**: Built the first CPU component with:
   - Two-phase operation (rising/falling edges) via `ClockPhaseChanged` events
   - Configurable frequency control (Hz)
   - Hardware interrupt queue processing at cycle boundaries
   - Start/Stop/Step controls for debugging
   - Managed task lifecycle with proper `CancellationToken` support

4. **Event Message Types**: Created strongly-typed event records:
   - `ClockPhaseChanged` - Signals clock transitions with cycle count
   - `HardwareInterrupt` - Queued interrupt signals with vector and data
   - `ClockPhase` enum - High/Low states

5. **Development Tools**: Set up Claude Code skills for project continuity:
   - `/journal` - Create reflective documentation of sessions
   - `/commit` - Combine journaling with git commits

6. **Documentation**: Created comprehensive `CLAUDE.md` with architecture overview, commands, and coding standards

## Key Design Decisions

**Microkernel Message-Passing Architecture**: Chose an event bus pattern over direct method calls to enable:
- Complete decoupling between components (no shared mutable state)
- Observable, deterministic behavior (all interactions are events)
- Clean component isolation (each runs on its own task)
- Future neural network control plane (network can observe/publish to same bus)

**Record Types for Events**: Using C# `record` with primary constructors for event messages because:
- Immutability by default (important for concurrent event processing)
- Structural equality (useful for testing and deduplication)
- Concise syntax (reduces boilerplate significantly)
- Clear intent (these are data carriers, not behavioral objects)

**Two-Phase Clock**: Publishing both rising and falling edges separately allows:
- Components to react on specific edges (like real hardware)
- More granular timing control
- Future multi-phase clock schemes (setup/hold timing)

**Region-Based Code Organization**: Standardized on `#region` blocks (Constants → Fields → Constructors → Properties → Methods) to:
- Maintain consistency across components
- Make code easier to navigate
- Group related members logically

**Threading Model**: Each component gets its own managed task:
- Prevents blocking between components
- Enables true parallel execution when hardware allows
- Clean shutdown via `CancellationToken`
- Aligns with microkernel philosophy

## Insights & Observations

**The CPU Metaphor Works**: The message-passing + event bus architecture naturally maps to how CPU components interact via buses and control signals. This validates the core architectural choice.

**Records Are Perfect for Events**: Converting from classes to records reduced event definitions by 50%+ while adding immutability guarantees. The primary constructor syntax is exactly what we needed.

**Threading Feels Right**: The microkernel-style task-per-component approach feels correct. It's more overhead than a single-threaded simulation, but it maps better to the eventual neural control plane (which needs to orchestrate concurrent components).

**Testing via Console First**: Starting with an interactive console app (not tests) was the right call. Being able to manually step the clock and see events in real-time provided immediate feedback about the architecture's feel.

**The Bus Is the Key**: The event bus is the critical architectural element. Everything else (components, messages, control signals) flows from having a clean, observable communication channel. The neural network will eventually "speak" this same event language.

## Next Steps

Immediate priorities:

1. **Registers**: Implement the register file (accumulator, general-purpose, flags, PC)
   - Subscribe to clock edges
   - Read/write via bus events
   - Provide observable state

2. **ALU**: Arithmetic Logic Unit component
   - Operations: ADD, SUB, AND, OR, XOR, shifts
   - Input/output via register events
   - Flag updates (zero, carry, overflow, negative)

3. **Memory**: Byte-addressable RAM
   - Read/write operations via address/data bus events
   - Consider memory-mapped I/O pattern

4. **Control Signals**: Define the event types that will eventually come from the neural network:
   - Register enable signals
   - ALU operation selection
   - Memory read/write enables
   - Bus routing control

5. **Integration Test**: Build a simple hard-coded program (e.g., add two numbers) to verify all components work together before adding the neural control plane.

6. **Terminal.Gui TUI**: Replace the basic console with a proper TUI showing:
   - Clock state and frequency
   - Register values
   - Memory contents
   - Event stream
   - Component status

## Questions & Uncertainties

**Bus Architecture**: Should we model separate address/data buses explicitly, or keep the event bus abstraction? Real CPUs have physical wire constraints; our virtual CPU doesn't. The event bus is cleaner, but might we lose educational value?

**Memory Size**: What's a reasonable RAM size for initial implementation? 64KB (like 8-bit systems) or smaller for faster visualization? Need to balance "feels like a real CPU" with "easy to observe and debug."

**Interrupt Handling**: The clock currently queues interrupts but doesn't process them meaningfully. When do we build the interrupt handler? Does it need a stack? An interrupt vector table? This could get complex quickly.

**Neural Network Integration Point**: At what stage do we introduce the neural control plane? Should we:
- First build a hardcoded control unit to validate the datapath?
- Start with neural control immediately and let it learn from scratch?
- Use imitation learning on hardcoded examples?

**Timing Granularity**: Two-phase clock is good, but is it enough? Real CPUs have multi-cycle instructions. Do we need micro-ops and multiple phases per instruction?

**State Serialization**: How will we snapshot CPU state for the neural network? JSON? Binary? Need efficient serialization for training.

---
*This journal will be read by future Claude instances to understand project evolution.*
