# TODO: Action Space Encoding & DPO Implementation

## Overview

Implement structured action space encoding where the network outputs EncodedEvent structures directly, with Direct Preference Optimization (DPO) to learn valid event generation.

**Key Insight**: `EncodedEvent` already defines our action representation as a fixed-length token structure. The network should output exactly this format.

---

## Phase 1: Network Output Structure Alignment

### 1.1 Update TransformerCpuController Action Decoder

- [ ] Update `ACTION_DIM` constant to match EncodedEvent structure:
  ```csharp
  EVENT_TYPE_DIM = 33    // Number of ACTIONABLE EventType values (not all 61 enum values)
                          // RegisterWrite, RegisterCopy, HardwareInterrupt,
                          // 15 ALU binary (Add-Ror), 6 ALU unary (Not-Clz),
                          // 5 Stack (Push-Dup), 4 Memory (LoadDirect-StoreIndirect)
  REGISTER_DIM = 8       // 7 RegisterId values + None
  VALUE_EMBED_DIM = 256  // Embedding for 64-bit value
  TOTAL = 33 + (8 × 3) + 256 = 313
  ```

- [ ] Create value decoder network:
  ```csharp
  _valueDecoder = Sequential(
      ("fc1", Linear(256, 128)),
      ("relu", ReLU()),
      ("fc2", Linear(128, 64)),
      ("sigmoid", Sigmoid())
  );
  ```

- [ ] Register value decoder as submodule in constructor

### 1.2 Implement EncodedEvent Decoding

- [ ] Add `DecodeEncodedEvent(Tensor actionLogits)` method:
  - Parse output tensor into sections (type, reg1, reg2, reg3, value_embed)
  - Apply argmax to categorical fields (event type, registers)
  - Decode value embedding to 64-bit ulong
  - Return `EncodedEvent` structure

- [ ] Add `DecodeValue(Tensor valueEmbed)` helper:
  - Forward through `_valueDecoder` to get 64 binary logits
  - Threshold at 0.5 to convert to bits
  - Pack bits into ulong

- [ ] Expose `GetActionLogits(CpuStateVector state)` method:
  - Run full forward pass
  - Return raw action logits (for DPO training)
  - Keep existing `DecideNextAction` as high-level API

### 1.3 Update DecideNextAction Flow

- [ ] Modify `DecideNextAction(CpuStateVector)`:
  1. Get action logits
  2. Decode to `EncodedEvent`
  3. Validate event (Phase 2)
  4. Convert to CPU event object
  5. Store in episode buffer if training
  6. Return CPU event

---

## Phase 2: Event Validation System

### 2.1 Create EventValidationDPO Class

- [ ] Create new file: `src/NeuralCpu.Network/Neural/EventValidationDPO.cs`

- [ ] Implement `Validate(EncodedEvent)` method:
  - [ ] RegisterWrite: requires Reg1, no Reg2/Reg3, value used
  - [ ] RegisterCopy: requires Reg1 (src) and Reg2 (dest), no Reg3, value ignored
  - [ ] AluBinary (Add, Sub, Mul, Div, And, Or, etc.): requires all three registers, value ignored
  - [ ] AluUnary (Not, Inc, Dec, Neg, Abs, Clz): requires Reg1 (src) and Reg2 (dest), no Reg3, value ignored
  - [ ] Push: requires Reg1, value ignored
  - [ ] PushImmediate: no registers, value used
  - [ ] Pop: requires Reg1 (dest), value ignored
  - [ ] Peek: requires Reg1 (dest), value ignored
  - [ ] Dup: no registers, no value
  - [ ] LoadDirect: requires Reg1 (dest), value = address
  - [ ] LoadIndirect: requires Reg1 (addr_reg) and Reg2 (dest), value ignored
  - [ ] StoreDirect: requires Reg1 (src), value = address
  - [ ] StoreIndirect: requires Reg1 (addr_reg) and Reg2 (src), value ignored
  - [ ] Interrupt: no registers, value = interrupt vector (byte)
  - Return `ValidationResult(bool IsValid, List<string> Errors)`

- [ ] Implement `Correct(EncodedEvent)` method:
  - For each event type, fix common errors:
    - Set RegisterId.None → RegisterId.R0 when register required
    - Clear unused registers to RegisterId.None
    - Clamp value to valid range if needed
  - Return corrected `EncodedEvent`

- [ ] Add `GetRequirements(EventType)` helper:
  - Returns which fields are required/optional/unused for each event type
  - Used by validation and correction

### 2.2 Implement EncodedEvent → CPU Event Conversion

- [ ] Add `DecodeCpuEvent(EncodedEvent)` method in TransformerCpuController:
  - Pattern match on `evt.Type`
  - Construct appropriate CPU event (RegisterWriteCommand, AluCommand, etc.)
  - Return `null` for invalid events (post-validation)
  - Handle all event types defined in `EventType` enum

- [ ] Add fallback behavior for invalid events:
  - Option A: Return no-op (RegisterWriteCommand(R0, 0))
  - Option B: Return null and skip cycle
  - Option C: Throw exception (fail-fast during training)
  - Document chosen approach

---

## Phase 3: DPO Training Infrastructure

### 3.1 Create DPOTrainer Class

- [ ] Create new file: `src/NeuralCpu.Network/Training/DPOTrainer.cs`

- [ ] Implement core DPO algorithm:
  - [ ] `TrainStep(CpuStateVector state)`:
    1. Generate action from current policy
    2. Decode to EncodedEvent
    3. Validate event
    4. If valid: skip (no DPO update needed)
    5. If invalid: generate corrected version
    6. Compute log probabilities for both
    7. Compute DPO loss: `-log(σ(β * (log π(valid) - log π(invalid))))`
    8. Backpropagate

- [ ] Implement `ComputeLogProb(Tensor logits, EncodedEvent evt)`:
  - Extract log probabilities for each field:
    - `log_softmax` over event type logits, index by evt.Type
    - `log_softmax` over register logits, index by evt.Reg1/2/3
    - (Optional) value likelihood
  - Return sum of log probs

- [ ] Add hyperparameters:
  - `beta`: Temperature for DPO (default 0.1)
  - `lambda_dpo`: Weight for DPO loss vs task loss
  - `warmup_steps`: Pure DPO before task training

### 3.2 Integrate DPO with Task Training

- [ ] Modify TaskRunner to use DPOTrainer:
  - Add DPO warmup phase (train only on validity)
  - Add mixed training phase (task reward + DPO regularization)
  - Combined loss: `λ_task * L_policy_gradient + λ_dpo * L_dpo`

- [ ] Add metrics:
  - Track validation accuracy over time
  - Track most common validation errors
  - Track DPO loss vs task loss
  - Log invalid event examples for debugging

---

## Phase 4: Value Encoding/Decoding

### 4.1 Improve Value Decoder

- [ ] Experiment with value representation:
  - Option A: Binary (64 sigmoid outputs)
  - Option B: Logarithmic scale (exponent + mantissa)
  - Option C: Learned embedding space with nearest-neighbor lookup
  - Option D: Autoencoder (encoder during training, decoder during inference)

- [ ] Add value reconstruction loss:
  - During training, also output predicted value
  - Compute MSE between predicted and actual value
  - Auxiliary loss helps learn meaningful value embeddings

### 4.2 Handle Value Ranges

- [ ] Add value normalization:
  - Small values (0-1000): common for counters, indices
  - Medium values (1K-1M): typical memory addresses in tasks
  - Large values (2^32-2^64): rare but possible
  - Network should learn to generate appropriate ranges

- [ ] Add value-aware validation:
  - Memory addresses must be < capacity
  - Interrupt vectors must be < 256
  - Stack values typically < 64K

---

## Phase 5: Testing & Validation

### 5.1 Unit Tests for Event Validation

- [ ] Create `tests/NeuralCpu.Network.Tests/EventValidationDPOTests.cs`

- [ ] Test cases:
  - [ ] All valid events pass validation
  - [ ] Invalid events are correctly identified
  - [ ] Correction produces valid events
  - [ ] Edge cases (None registers, extreme values, etc.)

### 5.2 Integration Tests

- [ ] Test `DecodeEncodedEvent` produces valid structures
- [ ] Test `DecodeCpuEvent` handles all event types
- [ ] Test DPO training reduces invalid event rate
- [ ] Test full pipeline: state → logits → EncodedEvent → CPU event → execution

### 5.3 Visualization & Debugging

- [ ] Add `EncodedEvent.ToString()` with human-readable format
- [ ] Log invalid events with correction suggestions
- [ ] Track invalid event frequency by type
- [ ] Create test harness that generates random states and validates outputs

---

## Phase 6: Advanced Features (Future)

### 6.1 Learned Validity Discriminator

- [ ] Create `ValidityDiscriminator` network:
  - Input: EncodedEvent fields (one-hot + value)
  - Output: Probability event is valid
  - Train on valid/invalid examples

- [ ] Replace hand-coded validation with learned discriminator
- [ ] Use discriminator score in DPO loss

### 6.2 Hierarchical Event Generation

- [ ] Explore autoregressive within network architecture:
  - Layer 1: Decode event type
  - Layer 2: Decode Reg1 (conditioned on type)
  - Layer 3: Decode Reg2 (conditioned on type + Reg1)
  - All in single forward pass via transformer decoder

### 6.3 Action Primitives & Macros

- [ ] Define high-level "macro" events:
  - CopyRegisterToMemory(reg, addr) → LoadIndirect + Store sequence
  - Swap(reg1, reg2) → Copy to temp + double copy back
  - Network learns when to use primitives vs macros

---

## Implementation Checklist

### Milestone 1: Basic Action Decoding ✅ COMPLETE
- [x] Write unit tests (TDD approach)
- [x] Update action decoder dimensions (Phase 1.1) - ACTION_DIM = 313
- [x] Implement GetActionLogits method (Phase 1.2)
- [x] Implement DecodeEncodedEvent (Phase 1.2)
- [x] Implement DecodeValue helper (Phase 1.2)
- [x] Implement DecodeCpuEvent (Phase 2.2) - all 33 event types supported
- [x] Update DecideNextAction pipeline (Phase 1.3)
- [x] Test: All 9 unit tests pass

### Milestone 2: Validation System ✅ COMPLETE
- [x] Create EventValidationDPO class (Phase 2.1)
- [x] Implement all validation rules (Phase 2.1) - 33 event types covered
- [x] Implement correction logic (Phase 2.1) - fixes missing/invalid registers
- [x] Add validation to action pipeline (Phase 1.3) - integrated into DecideNextAction
- [x] Test: All 22 validation tests pass

### Milestone 3: DPO Training ✅ COMPLETE
- [x] Create DPOTrainer class (Phase 3.1)
- [x] Implement DPO loss computation (Phase 3.1) - tensor-based with gradients
- [x] Implement ComputeLogProb (Phase 3.1) - log probability of events
- [x] Implement TrainStep (Phase 3.1) - single training iteration
- [x] Add Adam optimizer with learning rate
- [x] Test: All 8 DPO trainer tests pass
- [x] Integrate with TaskRunner (Phase 3.2) - integration tests written
- [x] Add DPO warmup phase (Phase 3.2) - tested with invalid rate reduction
- [x] Add metrics tracking (Phase 3.2) - DPOMetrics record with loss/log probs

### Milestone 4: Value Encoding
- [ ] Improve value decoder (Phase 4.1)
- [ ] Add value reconstruction loss (Phase 4.1)
- [ ] Add value range handling (Phase 4.2)
- [ ] Test: Network generates appropriate value ranges

### Milestone 5: End-to-End Validation
- [x] Write comprehensive unit tests (Phase 5.1) - TrainingOrchestratorTests (8 tests)
- [x] Create TrainingOrchestrator class (coordinates DPO + task training)
- [x] Write integration tests (Phase 5.2) - EndToEndTrainingTests (8 tests)
- [x] **CRITICAL**: Implement real policy gradient backpropagation - PolicyGradientTrainer (9 tests)
- [x] **CRITICAL**: Fix gradient flow - removed .detach() from episode buffer storage
- [x] Debug training - identified root cause: Echo task too hard for random init (PolicyLoss=-0.0, Output Length=0)
- [ ] **BLOCKER**: Implement curriculum learning or reward shaping (random network never writes to OUTPUT_BUFFER)
- [ ] Add visualization/debugging tools (Phase 5.3)
- [ ] Run simpler tasks first (register operations, no memory addresses)
- [ ] Measure: Task success rate, invalid event rate, training time

### Milestone 6: Network as Async Component
**Goal**: Implement network as asynchronous hosted service, aligned with event-driven microkernel architecture.

#### Phase 6.1: NetworkControllerService Implementation
- [ ] Create `NetworkControllerService : IHostedService, IObserver<ClockPhaseChanged>`
- [ ] Subscribe to `ClockPhaseChanged` events in `StartAsync`
- [ ] Implement `OnNext(ClockPhaseChanged)` to:
  - Build state vector on rising edge
  - Decide next action via controller
  - Publish control signal to event bus
- [ ] Implement proper cleanup in `StopAsync` (unsubscribe, dispose)
- [ ] Run network inference on background task with `CancellationToken`

#### Phase 6.2: Execution Mode Abstraction
- [ ] Define `NetworkExecutionMode` enum: `{ Synchronous, Asynchronous }`
- [ ] Add mode parameter to network controller configuration
- [ ] **Synchronous Mode**: Current TaskRunner behavior (training)
  - Direct method calls in TaskRunner loop
  - Deterministic episode execution
  - Full control over timing for gradient computation
- [ ] **Asynchronous Mode**: Event-driven behavior (inference/deployment)
  - Network reacts to clock events independently
  - No TaskRunner orchestration
  - Continuous operation as hosted service

#### Phase 6.3: Integration with Hosting Infrastructure
- [ ] Register `NetworkControllerService` in `ServiceCollectionExtensions.AddNeuralCpu()`
- [ ] Add configuration option to enable/disable async mode
- [ ] Ensure network service starts/stops with other CPU components
- [ ] Test: Network service responds to clock events without TaskRunner

#### Phase 6.4: Testing and Validation
- [ ] Unit tests for `NetworkControllerService`:
  - Service starts and subscribes correctly
  - Responds to `ClockPhaseChanged` events
  - Publishes control signals on rising edge
  - Cleans up resources on stop
- [ ] Integration tests:
  - Run CPU + Network together as hosted services
  - Verify control signals reach CPU components
  - Measure event latency (clock → decision → action)
- [ ] Compare async vs sync behavior:
  - Same network should produce similar actions
  - Async mode should not introduce race conditions

#### Phase 6.5: Console Application Update
- [ ] Update `NeuralCpu.Console` to support async network mode
- [ ] Add command to toggle between sync/async execution
- [ ] Display network status in TUI (active, idle, decision count)
- [ ] Add real-time event stream view (show network decisions as they happen)

---

## Success Metrics (Updated)

**Phase 6 (Async Network Component):**
- ✅ Network runs as independent hosted service
- ✅ Responds to clock events with <10ms latency
- ✅ Produces equivalent actions in async vs sync modes
- ✅ No race conditions or event ordering issues
- ✅ Clean startup/shutdown with other components

---

## Success Metrics

**Phase 1-2 (Basic Decoding):**
- ✅ Network outputs parse to valid EncodedEvent structures
- ✅ All event types can be generated
- ✅ Validation system catches >95% of invalid events

**Phase 3 (DPO Training):**
- ✅ Invalid event rate drops from ~50% (random) to <5% after warmup
- ✅ DPO loss converges
- ✅ Network maintains low invalid rate during task training

**Phase 4 (Value Encoding):**
- ✅ Generated values are in appropriate ranges for their context
- ✅ Value reconstruction loss < threshold
- ✅ Memory addresses stay in bounds

**Phase 5 (Integration):**
- ✅ Echo task: 100% success rate with neural controller
- ✅ Increment task: 100% success rate
- ✅ Reverse task: 100% success rate
- ✅ Training converges in reasonable time (<1000 episodes per task)

---

## Notes & Design Decisions

### Why EncodedEvent as Action Space?
- Already defined in serialization
- Fixed-length structure (constant memory)
- Matches domain semantics
- Easy to validate and debug

### Why DPO over Hard Constraints?
- Soft learning vs hard rejection
- Network learns "why" events are invalid
- Handles edge cases gracefully
- Can generalize to new validation rules

### Value Encoding Challenges
- 64-bit space is enormous (2^64 possibilities)
- Most tasks use small subset of values
- Need balance between expressiveness and learnability
- Embedding space allows generalization

### Training Strategy
- Warmup with DPO only (learn valid events)
- Then mixed training (valid events + task objectives)
- Avoids wasting gradients on invalid actions
- Separate concerns: validity vs optimality

### Network as Async Component (Milestone 6)
**Philosophy**: The network is not a special privileged component - it's just another observer on the event bus, like Clock, ALU, Stack, etc.

**Two Execution Modes**:
1. **Synchronous (Training)**: TaskRunner calls network directly in loop
   - Deterministic episode execution
   - Full control for gradient computation
   - Batch collection of state/action/reward tuples
   
2. **Asynchronous (Inference/Deployment)**: Network as hosted service
   - Subscribes to `ClockPhaseChanged` events
   - Reacts to CPU state changes independently
   - True event-driven microkernel architecture
   - No central orchestrator

**Event-Driven Flow** (Async Mode):
```
Clock.Step() → ClockPhaseChanged event
             ↓
NetworkControllerService.OnNext()
             ↓
Build state vector
             ↓
Decide next action
             ↓
Publish control signal (e.g., RegisterWriteCommand)
             ↓
RegisterFile.OnNext() → execute write
```

**Why Clock-Based Initially?**
- Simpler: Network observes at discrete time steps (rising edge)
- Less noise: Don't overwhelm network with every RegisterChanged/StackPush/etc.
- Natural feedback loop: Decision → execution → observe next state
- Future: Can add full event stream observation as advanced feature
