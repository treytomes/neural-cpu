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

**Network Architecture** (`NeuralCpu.Network/`)
- **Mock Network**: Rule-based controller for testing CPU-network interface
- **State Vector**: Structured CPU state snapshot (registers, event history, stack, memory)
- **Event Encoding**: Structured representation with separate type/register/value fields
- **Task Runner**: Orchestrates observe → decide → act loop with reward computation
- Three validation tasks: Echo (I/O), Increment (ALU), Reverse (Stack)

**Key Design Principles**
- Components are deterministic and observable
- Network observes CPU state and emits control signals (events)
- Event-driven rather than direct method calls
- CPU datapath (ALU, registers, memory) will be fixed; control signals learned by the network
- Modular MoE architecture planned: Router → Experts → Modules with selective freezing

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
  NeuralCpu.Network/           # Neural network controllers and task runners
    EventType.cs               # Event type enumeration for encoding
    EncodedEvent.cs            # Structured event representation
    CpuStateVector.cs          # Complete CPU state snapshot
    StateVectorBuilder.cs      # Builds state vectors from CPU components
    INetworkController.cs      # Interface for network controllers
    MockNetwork.cs             # Rule-based controller (Echo, Increment, Reverse tasks)
    TaskRunner.cs              # Orchestrates network-CPU execution loop
src/
  NeuralCpu.Core/              # Core library with event bus and components
    Events/                    # Event types and IEventBus interface
    Components/                # CPU components (Clock, RegisterFile, ALU, Stack, Memory)
    Services/                  # Hosted services for component lifecycle
    ServiceCollectionExtensions.cs  # DI registration extension methods
  NeuralCpu.Console/           # Test/debug TUI application with tabbed display
tests/
  NeuralCpu.Network.Tests/     # Unit tests for Network layer
    EventTypeGuardTests.cs     # Guard test ensuring EventType enum covers all Core events
.claude/
  journal/                     # Session journal entries documenting evolution
  skills/                      # Custom skills (journal, commit)
  settings.local.json          # Permission settings
```

## Current Implementation Status

**Implemented:**
- Event bus with observer pattern
- Clock module with two-phase operation (rising/falling edges)
- Hardware interrupt queuing (HALT on 0x00, RESET on 0x01)
- Register file with 7 ulong registers (R0-R3, PC, SP, Flags)
- Register write queuing (synchronized to clock rising edge)
- Register read responses (immediate, no clock sync needed)
- **ALU with 21 operations** (binary, unary, bit operations)
- **Stack with 5 operations** (push, pop, peek, dup) and 64K item limit
- **Memory with 4 operations** (load/store, direct/indirect addressing) and configurable capacity (default 1MB)
- PC write protection (read-only time counter)
- RegisterChanged optimization (publishes only on actual value changes)
- Configurable frequency control
- Start/Stop/Step controls
- Microsoft.Extensions.Hosting integration
- Dependency injection via `AddNeuralCpu()` extension method
- Serilog logging with rolling file logs (daily rotation, 10MB limit, 30-day retention)
- Terminal.Gui v2 TUI with tabbed display (Event Log, Stack, Memory), real-time monitoring

**Recently Added:**
- **Mock Network** with rule-based controller (Echo, Increment, Reverse tasks)
- **State Vector Builder** for network observation (registers, events, stack, memory)
- **Task Runner** with episode orchestration and reward computation
- **Event Encoding** with structured type/register/value fields
- **Memory-mapped I/O** at 0x80000000 (input) / 0x80001000 (output)
- **Unit Tests** with EventType guard (fails if Core events added without enum coverage)
- **Comprehensive Configuration** via YAML (network state vector sizes, task runner params)

**Not Yet Implemented:**
- Real neural network control plane (TorchSharp integration)
- ValueEncoder/ValueDecoder for 64-bit precise embeddings
- Modular MoE architecture with selective freezing

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
- **PC** (Program Counter) - **READ-ONLY time counter** (reset to 0, auto-increments every cycle)
- **SP** (Stack Pointer) - Stack depth counter (reflects `stack.Count`, reset to 0)
- **Flags** - Status flags (7 bits, reset to 0)

**Operation:**
- **Simple writes**: `RegisterWriteCommand` - writes value without updating flags
- **Writes with flags**: `RegisterWriteWithFlagsCommand` - writes value and updates Zero/Negative flags
- **Register copy**: `RegisterCopyCommand` - copies value from source to destination (like physical CPU read-enable + write-enable)
- **Reads**: Immediate response via `RegisterReadRequest`/`RegisterReadResponse` (no clock sync needed)
- **PC auto-increment**: PC increments every clock cycle to give network sense of time passing
- **PC write protection**: Attempts to write PC are logged as warnings and silently ignored - PC is a temporal counter, not an instruction pointer
- **Change notification**: Publishes `RegisterChanged` only when value actually changes (optimized to reduce event stream noise)
- **Reset**: Subscribes to `SystemReset` event, restores all registers to zero

All write operations are queued and execute on the clock rising edge (synchronized).

**Direct access** (for debugging only): `registerFile[RegisterId.R0]` returns current value

### PC Architecture Decision

**PC is NOT an instruction pointer** - it's a **time counter** that monotonically increases with each clock cycle. This architectural choice:
- Provides the network with temporal awareness (how many cycles have passed)
- Prevents spatial jumps (no traditional call/return or goto)
- Forces the network to learn temporal patterns rather than spatial programs
- Eliminates the concept of "program counter" in favor of "time counter"

Writes to PC are rejected at the RegisterFile level to enforce this semantic.

## CPU Flags

The Flags register ([CpuFlags.cs](src/NeuralCpu.Core/Components/CpuFlags.cs)) uses individual bits for status flags:

**Flag Bits:**
- **Bit 0 - Zero (Z)**: Set when result is zero
- **Bit 1 - Carry (C)**: Set when unsigned overflow occurs
- **Bit 2 - Negative (N)**: Set when result is negative (MSB is 1)
- **Bit 3 - Overflow (O)**: Set when signed overflow occurs
- **Bit 4 - DivideByZero (D)**: Set when division by zero is attempted
- **Bit 5 - StackUnderflow (S)**: Set when popping/peeking from empty stack
- **Bit 6 - StackOverflow (U)**: Set when pushing to full stack (64K limit)
- **Bit 7 - MemoryFault (F)**: Set when accessing out-of-bounds memory address

**Helper Methods:**
- `flagsValue.HasFlag(CpuFlags.Zero)` - Check if flag is set
- `flagsValue.SetFlag(CpuFlags.Carry, true)` - Set or clear flag
- `flagsValue.ToFlagString()` - Format as "ZCNODSUF" (uppercase = set, dash = clear)

**TUI Display:** Shows both symbolic (`ZCNODSUF` or `--------`) and hex representations

**Error Handling Philosophy:** Flags use "forgiving failure" - errors set flags and allow execution to continue rather than halting. This enables the network to observe consequences and learn from mistakes.

## ALU Details

The ALU ([Alu.cs](src/NeuralCpu.Core/Components/Alu.cs)) performs arithmetic and logic operations with **21 total operations**:

**Binary Operations (require two operands):**
- **Add** - Addition with carry/overflow detection
- **Sub** - Subtraction with borrow/overflow detection
- **Mul** - Multiplication (truncates high bits, sets carry on overflow)
- **Div** - Division (sets DivideByZero flag if divisor is 0, returns 0)
- **And** - Bitwise AND
- **Nand** - Bitwise NAND (NOT AND)
- **Or** - Bitwise OR
- **Nor** - Bitwise NOR (NOT OR)
- **Xor** - Bitwise XOR (exclusive OR)
- **Xnor** - Bitwise XNOR (NOT XOR)
- **Shl** - Shift left logical (zero-fill)
- **Shr** - Shift right logical (zero-fill)
- **Sar** - Shift right arithmetic (sign-extend)
- **Rol** - Rotate left (wrap bits)
- **Ror** - Rotate right (wrap bits)

**Unary Operations (single operand):**
- **Not** - Bitwise NOT (one's complement)
- **Inc** - Increment by 1
- **Dec** - Decrement by 1
- **Neg** - Two's complement negation (sets Overflow if negating most negative value 0x8000...0000)
- **Abs** - Absolute value (sets Overflow if input is most negative value)
- **Clz** - Count leading zeros (returns 0-64)

**Operation:**
- **Binary ops**: `AluCommand(operation, sourceA, sourceB, destination)` - reads two registers, writes result
- **Unary ops**: `AluUnaryCommand(operation, source, destination)` - reads one register, writes result
- All operations execute on clock rising edge (single-cycle)
- Results always update destination register and Flags register
- Flags updated: Zero, Carry, Negative, Overflow, DivideByZero (context-dependent)

**Error Handling:**
- Division by zero: Returns 0, sets DivideByZero flag
- Multiply overflow: Truncates high bits, sets Carry flag
- Negate/Abs overflow: Sets Overflow flag for 0x8000...0000 (most negative 64-bit value)

**Direct access** (for debugging): ALU can read register values directly via RegisterFile indexer

### ALU Design Philosophy

All operations are **single-cycle** - even multiply and divide complete in one clock tick. This is intentional:
- Simplifies the event stream (network doesn't wait on multi-cycle ops)
- 64-bit operations are computationally cheap in software
- Network learns temporal patterns from other sources (memory latency, stack depth)
- Keeps control plane focused on higher-level coordination

Real hardware would use multi-cycle for MUL/DIV, but this is a learning architecture, not a performance-optimized CPU.

## Stack Details

The Stack ([Stack.cs](src/NeuralCpu.Core/Components/Stack.cs)) provides LIFO storage using .NET `Stack<ulong>`:

**Operations:**
- **PUSH (register)**: `PushCommand(source)` - pushes value from source register onto stack
- **PUSH (immediate)**: `PushImmediateCommand(value)` - pushes literal value onto stack
- **POP**: `PopCommand(destination)` - pops top value into destination register
- **PEEK**: `PeekCommand(destination)` - copies top value to destination register (non-destructive)
- **DUP**: `DupCommand()` - duplicates top value (push another copy)

**Characteristics:**
- **Capacity**: 64K items (65,536 ulongs = 512KB memory)
- **SP synchronization**: SP register automatically reflects `stack.Count` after each operation
- **Error handling**: Underflow/overflow set flags and continue (forgiving failure)
- **Thread-safety**: All operations queued and execute on clock rising edge
- **Observable state**: `GetSnapshot()` returns array of stack contents for display

**Error Conditions:**
- **Stack underflow**: POP/PEEK on empty stack returns 0 to destination, sets StackUnderflow flag
- **Stack overflow**: PUSH when at 64K limit is discarded, sets StackOverflow flag
- **DUP underflow**: DUP on empty stack sets StackUnderflow flag
- **DUP overflow**: DUP when at 64K limit sets StackOverflow flag

**SP Semantics:**
- SP is a **counter** (stack depth), NOT a memory address pointer
- SP always equals `stack.Count` (0 when empty, 64K when full)
- This differs from traditional CPUs where SP points to a RAM location
- Simpler model: SP reflects stack state directly rather than pointing to memory

**System Reset:**
- Clears all stack contents
- Clears all pending operations
- Resets SP to 0

**Direct access** (for debugging only): `stack.Count` property and `stack.GetSnapshot()` method

### Stack Architecture Decision

Using .NET's `Stack<ulong>` instead of custom memory-backed stack:
- **Simpler**: No manual array management or pointer arithmetic
- **SP as counter**: `stack.Count` is the natural representation
- **Thread-safe via clock sync**: All ops execute on rising edge, no concurrent mutation
- **Snapshot for display**: `ToArray()` provides complete view when needed

Trade-off: Can't directly address stack slots (no "peek at offset N"). Stack is purely LIFO. If network needs random access, use general-purpose registers or Memory component.

## Memory Details

The Memory ([Memory.cs](src/NeuralCpu.Core/Components/Memory.cs)) provides random-access word-addressable RAM:

**Operations:**
- **LOAD (direct)**: `LoadCommand(address, destination)` - reads from address to register
- **LOAD (indirect)**: `LoadIndirectCommand(addressReg, destination)` - reads from address stored in register
- **STORE (direct)**: `StoreCommand(address, source)` - writes register to address
- **STORE (indirect)**: `StoreIndirectCommand(addressReg, source)` - writes register to address stored in register

**Characteristics:**
- **Capacity**: Default 128K words (1MB), configurable via constructor parameter
- **Addressing**: Word-addressable (address 0 = first ulong, address 1 = second ulong)
- **Bus width**: 64-bit ulongs match register width
- **Error handling**: Out-of-bounds sets MemoryFault flag, returns 0
- **Thread-safety**: All operations queued and execute on clock rising edge (single-cycle)
- **Observable state**: `GetSnapshot(startAddr, count)` returns memory range for display
- **Highlight tracking**: `WasRecentlyChanged(address)` returns true if written within last 5 seconds

**Error Conditions:**
- **Out-of-bounds access**: LOAD/STORE beyond capacity returns 0 or ignores write, sets MemoryFault flag
- **Bounds checking**: No modulo wrap - explicit errors are observable to network

**Addressing Modes:**
- **Direct**: Address literal in command (e.g., LOAD [0x100] → R1)
- **Register-indirect**: Address stored in register (e.g., LOAD [R2] → R3 reads from address in R2)

**System Reset:**
- Clears all memory contents (zeros)
- Clears pending operations
- Clears recent write tracking

**Direct access** (for debugging only): `memory[address]` indexer property

### Memory Architecture Decision

Using word-addressable RAM instead of byte-addressable:
- **Simpler**: Address points directly to ulong, no alignment concerns
- **Bus width match**: 64-bit addresses for 64-bit data
- **Single-cycle ops**: Load/Store complete in one clock cycle
- **Configurable capacity**: Constructor parameter allows different memory sizes

Trade-offs:
- Can't address individual bytes within a word (use bit operations if needed)
- Less realistic than byte addressing but simpler for learning architecture
- Network learns memory patterns at word granularity

**Recent Write Tracking:**
Memory maintains a dictionary of `(address, timestamp)` for highlighting recently modified locations in the TUI. Entries older than 5 seconds are considered "not recent" but remain in the dictionary (cleared on reset).

## Mock Network (Rule-Based Controller)

The MockNetwork ([MockNetwork.cs](src/NeuralCpu.Network/MockNetwork.cs)) implements a rule-based controller to validate the CPU-network interface:

**Three Validation Tasks:**

1. **Echo Task**: Copy INPUT_BUFFER to OUTPUT_BUFFER
   - Tests: Basic LOAD/STORE, memory-mapped I/O
   - Algorithm: Loop through input, read each value, write to output

2. **Increment Task**: Read numbers, write N+1
   - Tests: ALU operations (INC), arithmetic
   - Algorithm: Loop through input, increment each value, write result

3. **Reverse Task**: Read sequence, write reversed
   - Tests: Stack operations (PUSH/POP), LIFO behavior
   - Algorithm: Push all inputs onto stack, pop all to output

**Memory-Mapped I/O Regions:**
```
0x80000000 - INPUT_BUFFER   (read-only, task input data)
0x80000FF8 - INPUT_LENGTH   (number of input items)
0x80001000 - OUTPUT_BUFFER  (write-only, task output data)
0x80001FF8 - OUTPUT_LENGTH  (number of output items)
```

**State Vector Structure** ([CpuStateVector.cs](src/NeuralCpu.Network/CpuStateVector.cs)):
- **Registers**: All 7 registers (R0-R3, PC, SP, Flags)
- **EventHistory**: 64-event sliding window
- **StackSnapshot**: Top 8 stack items
- **MemorySnapshot**: 16 sampled locations (hot regions + fixed I/O addresses)
- **CycleCount**: Temporal awareness

**Event Encoding** ([EncodedEvent.cs](src/NeuralCpu.Network/EncodedEvent.cs)):
```csharp
struct EncodedEvent {
    EventType Type;      // What operation (RegisterWrite, LoadDirect, AluAdd, etc.)
    RegisterId Reg1;     // First register operand (or None)
    RegisterId Reg2;     // Second register operand (or None)
    RegisterId Reg3;     // Third register operand (or None)
    ulong Value;         // Immediate value, address, or data
}
```

Structured encoding allows the network to learn relationships: "I issued RegisterWrite{R0, 42}, now R0 contains 42."

**Task Runner** ([TaskRunner.cs](src/NeuralCpu.Network/TaskRunner.cs)):
Orchestrates the execution loop:
1. Reset CPU and controller
2. Load task input into memory
3. Loop until HALT or max cycles:
   - Build state vector
   - Controller decides next action
   - Publish event to CPU
   - Tick clock
4. Evaluate output buffer against expected result
5. Compute reward (1.0 = perfect, partial credit for correctness)

## Future Direction: Neural Control Plane

**Planned Architecture (Modular Mixture of Experts):**

```
Event Stream → Tokenizer → Router
                            ├─ Expert 1 (ALU operations)    [Module 1, Module 2, ...]
                            ├─ Expert 2 (Memory management) [Module 1, Module 2, ...]
                            ├─ Expert 3 (Stack operations)  [Module 1, Module 2, ...]
                            └─ Expert 4 (Control flow)      [Module 1, Module 2, ...]
                                → Control Signals → Event Bus
```

**Key Features:**
- **Horizontal specialization**: 4 experts, each learns a domain (compute, memory, stack, control)
- **Vertical depth**: Modules stack within each expert (vocabulary → semantics → strategy)
- **Selective freezing**: Freeze lower modules when vocabulary is learned, add deeper modules for strategic thinking
- **Top-K routing**: Activate top-2 experts per cycle for redundancy
- **Structured I/O**: `ValueEncoder`/`ValueDecoder` for precise 64-bit values (no floating point errors)

**Technology Stack:**
- **TorchSharp**: C# bindings to LibTorch for full PyTorch feature set
- Custom MoE architecture with per-module freezing
- Serialization: Network weights (TorchSharp `save`/`load`) + CPU state (custom binary format)

**Training Tasks:**
Start with sequence completion via memory-mapped I/O, then progress to:
- Pattern recognition
- Arithmetic sequences
- Control flow learning
- Self-modification experiments

The mock network validates that the CPU-network interface works. The next step is replacing the rule-based controller with a learned neural network.
