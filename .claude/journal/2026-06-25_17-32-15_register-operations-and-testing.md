# Journal Entry - Register Operations and Testing

**Date:** 2026-06-25 17:32:15
**Session:** Register file implementation, flag system, test UI, and architectural reflection

## What We Accomplished

This session brought the register system to life with multiple operation types and comprehensive testing capabilities:

**Register File Implementation:**
- 7 ulong (64-bit) registers: R0-R3 (general purpose), PC, SP, Flags
- Three operation types with distinct behaviors:
  - `RegisterWriteCommand` - Simple write, no flag updates
  - `RegisterWriteWithFlagsCommand` - Write with Zero/Negative flag updates
  - `RegisterCopyCommand` - Copy from source to destination (mimics physical CPU read-enable + write-enable)
- PC auto-increments every clock cycle to provide sense of time
- All operations synchronized to clock rising edge
- System reset (interrupt 0x01) zeros all registers

**CPU Flags System:**
- `CpuFlags` enum with bit positions for Zero, Carry, Negative, Overflow
- Extension methods: `HasFlag()`, `SetFlag()`, `ToFlagString()`
- Flag updates on `RegisterWriteWithFlagsCommand`:
  - Zero flag: set when value == 0
  - Negative flag: set when MSB is 1 (0x8000_0000_0000_0000)
  - Carry/Overflow: reserved for future ALU operations
- TUI displays flags as "ZCNO" or "----" plus hex value

**Test Interface:**
- "Register Tests" panel with 5 test buttons:
  - Write R0=0x42 (simple write)
  - Write R1=0xFF (flags) (with flag updates)
  - Copy R0→R2 (register copy operation)
  - Write R3=0 (flags) (tests Zero flag)
  - Write R3=MSB (flags) (tests Negative flag)
- Button styling with ColorScheme (gray background, distinct from blue terminal)
- Panel heights adjusted for button drop shadows
- Event log shows all operations in real-time

**UI Polish:**
- Shared button color scheme (gray normal, white focus)
- Controls panel: height 3→4
- Register Tests panel: height 4→6 with 2-line spacing between rows
- Scroll indicator for event log
- Taller register/status panels (height 8→9)

## Key Design Decisions

**Three Operation Types:**
Separating write operations into three variants provides flexibility:
- Simple writes don't pollute flags (useful for setup/copying)
- Writes with flags mimic ALU behavior (will be used when ALU is added)
- Copy operations feel like physical CPU (read-enable source, write-enable dest, pulse clock)

This separation means the network (eventually) can choose whether an operation should affect flags.

**PC as Time Counter:**
Rather than traditional instruction pointer, PC increments every cycle automatically. This gives the neural network:
- Sense of time passing
- Continuity across inference cycles
- Observable "heartbeat" in the event stream

This is a departure from traditional CPU architecture but aligned with the goal of teaching the network temporal awareness.

**Flags Only Update on Explicit Command:**
`RegisterWriteCommand` doesn't touch flags, only `RegisterWriteWithFlagsCommand` does. This prevents surprising flag changes and makes flag updates explicit in the event stream. When we add the ALU, ALU operations will use the "with flags" variant.

**All Registers Reset to Zero:**
Including PC and SP. This is simpler than traditional CPU reset vectors and makes the reset state completely predictable. Since we're not executing traditional programs, starting PC at 0 and incrementing is fine.

**Event Bus as Universal Communication:**
Every operation goes through events:
- Buttons publish commands
- RegisterFile subscribes and processes
- Changes are published back as RegisterChanged events
- The network (future) will be just another event publisher/subscriber

This keeps components decoupled and makes the entire system observable.

## Insights & Observations

**The Interface Feels Right:**
User feedback: "I like the interface; it really feels like I'm interacting with a CPU." The test buttons, real-time register updates, and event log create a tactile sense of working with hardware. The copy operation especially captures the "read-enable + write-enable + clock pulse" feeling of physical CPU design.

**Event Stream for Network Input:**
During architectural discussion, we arrived at a compelling structure for network integration:
```
[CPU_STATE_TOKENS] + [EVENT_WINDOW]
```
- State tokens: Current register values, always fresh (9 tokens)
- Event window: Last N events as sliding history (64-256 events?)
- Network outputs event sequences (same format it reads)
- Symmetry: network is a peer component on the bus, not a controller

This means the network would:
- Experience time through PC incrementing
- See consequences of its actions in the event stream
- Learn timing patterns (command → clock → result)
- Potentially develop attention over the event sequence

**Think Frequency Evolution:**
Initial thought was "every clock cycle" for network inference. As the architecture crystallized (state + event stream), this still feels right - tight coupling between clock and network creates real feedback loops. But we acknowledged this might change as we get deeper into implementation.

**Terminal.Gui v2 Scrollbar Limitations:**
Terminal.Gui v2's ListView doesn't expose scrollbar APIs like v1. We added a visual scroll indicator (`│`) but couldn't implement a proper draggable scrollbar. Mouse wheel and keyboard still work, so functionality is preserved even if visual feedback is minimal.

**Button Drop Shadows:**
Terminal.Gui buttons have built-in drop shadows that were being clipped. Increasing panel heights and adding blank lines solved this. Small detail but important for polish.

## Next Steps

Immediate priorities:

1. **Commit This Work:**
   - Journal entry documenting register system
   - All register files, events, TUI updates
   - CLAUDE.md documentation updates

2. **Event Encoding Design:**
   - How to convert events to numeric tokens?
   - Type ID system for different event types?
   - Parameter encoding (register IDs, values, etc.)
   - Token vocabulary size considerations

3. **State Vector Definition:**
   - Formalize the 9 CPU state tokens
   - Positional encoding for state vs. event contexts
   - Value normalization/scaling for network input

4. **Event Window Implementation:**
   - Circular buffer for last N events
   - Event serialization to token sequences
   - Window size experimentation (start with 64?)

5. **Network Output Decoding:**
   - Parse network output tokens back to events
   - Validation (can't issue invalid operations)
   - Rate limiting (can network issue multiple commands per cycle?)

6. **Mock Network Integration:**
   - Simple rule-based "network" that reads state and issues commands
   - Test the full loop: state → inference → commands → execution → new state
   - Validate event encoding/decoding pipeline

7. **Transformer Selection:**
   - Which architecture? (GPT-style decoder-only? Encoder-decoder?)
   - Size/capacity considerations
   - Training infrastructure (PyTorch? JAX? C# ML.NET?)

## Questions & Uncertainties

**Event Encoding Strategy:**
How granular should event tokens be?
- Option A: Single token per event type + parameter tokens: `[REG_WRITE, R0, 0x42]` (3 tokens)
- Option B: Composite token encoding type + common params: `[REG_WRITE_R0]` + value (2 tokens)
- Option C: Fully flattened: event type and all params as separate tokens (flexible but verbose)

Need to balance vocabulary size vs. token efficiency.

**Window Size vs. Context Length:**
If we use 256-event window and each event is 3-5 tokens, that's 768-1280 tokens just for events. Add 9 for state = ~1300 token context minimum. This constrains which transformer architectures we can use (need at least 2K context for headroom). Can we start smaller (64 events = ~200 tokens) and grow?

**Network Output Constraints:**
Should the network be allowed to:
- Issue multiple commands per cycle? (parallel operations)
- Issue invalid commands? (write to PC, copy Flags to R0, etc.)
- See its own commands immediately in next input? (they're in the event window)

Permissive vs. constrained - which teaches better?

**Think Frequency Flexibility:**
Every clock cycle means network inference is on the critical path. If the clock runs at 10Hz and inference takes 50ms, we're fine. If clock is 1KHz and inference takes 50ms, we're bottlenecked. Should we:
- Let clock speed adapt to network inference time?
- Run network inference async (might see stale state)?
- Have network "request" to think rather than automatic every cycle?

**Training Regime:**
What task do we train the network on?
- Imitation learning: watch human-issued commands, learn to replicate?
- Reinforcement learning: reward for... what? (counter implemented in registers? pattern generation?)
- Self-supervised: predict next event given current state + history?

This is the biggest open question.

**Event Stream as Language:**
We said events "should not be human-readable language." But they ARE a kind of structured language - event types are like verbs, register IDs like nouns, values like objects. Is this the right level of abstraction, or should we go lower (bit-level?) or higher (instruction-like?)?

**Multi-Component Future:**
Right now we have Clock and RegisterFile. When we add:
- ALU (arithmetic/logic operations)
- Memory (RAM reads/writes)
- I/O (external data streams)

...the event stream gets richer. Should we architect for this now, or add incrementally? The event bus handles it naturally, but token encoding might need extension.

**PC Overflow:**
PC is ulong, increments every cycle. At 1KHz clock, it overflows in ~600 million years. Probably fine. But should we wrap at some boundary (64K? 1M?) to keep values in a reasonable range for network input? Or is full 64-bit range fine?

---

**Reflection on Architecture Evolution:**

The conversation that led to "network reads events, network writes events" felt like a breakthrough. The symmetry is elegant:
- Network is not special, just another bus participant
- Event stream becomes the universal interface
- CPU state provides grounding/continuity
- Attention mechanism can learn temporal patterns

This architecture enables genuine self-reflection: the network sees the consequences of its own prior actions in the event stream window. If it writes R0=42, then on the next cycle it sees RegisterChanged(R0, old, 42) in its input. Over time, it can learn causality.

The "state prefix + event stream" structure also maps naturally to transformer attention:
- Early layers can attend to state (what's in registers NOW?)
- Middle layers can attend to recent events (what just happened?)
- Late layers can attend across the full window (patterns over time)

We're building a neural network that experiences time, has working memory (registers), and can reflect on its own actions. That's the original vision coming into focus.

---

*This journal will be read by future Claude instances to understand project evolution.*
