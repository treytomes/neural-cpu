# Journal Entry - Milestone 3: DPO Training Infrastructure

**Date:** 2026-06-26 15:00:00
**Session:** Implementing Direct Preference Optimization (DPO) training for neural CPU controller

## What We Accomplished

### Milestone 3: DPO Training Infrastructure

**DPOTrainer Class Implementation:**
```csharp
public sealed class DPOTrainer : IDisposable
{
    public DPOMetrics TrainStep(CpuStateVector state);
    public double ComputeDPOLoss(Tensor logits, EncodedEvent preferred, EncodedEvent rejected);
    public double ComputeLogProb(Tensor logits, EncodedEvent evt);
    private Tensor ComputeDPOLossTensor(Tensor logits, EncodedEvent preferred, EncodedEvent rejected);
    private Tensor ComputeLogProbTensor(Tensor logits, EncodedEvent evt);
}
```

**Key Components:**

1. **TrainStep(CpuStateVector)** - Single DPO training iteration:
   - Zero gradients
   - Forward pass: Get action logits from network
   - Decode to EncodedEvent
   - Validate event
   - If invalid: Create DPO pair (corrected vs original)
   - Compute DPO loss (tensor with gradients)
   - Backward pass
   - Optimizer step
   - Return metrics

2. **DPO Loss Computation**:
   - Formula: `-log(σ(β * (log π(preferred) - log π(rejected))))`
   - Implementation: `functional.softplus(-scaledRatio)` for numerical stability
   - `softplus(x) = log(1 + exp(x))`
   - When log probs are equal: loss = log(2) ≈ 0.693 (baseline)

3. **Log Probability Computation**:
   - Tensor version: `ComputeLogProbTensor()` - with gradients for backprop
   - Scalar version: `ComputeLogProb()` - detached, for metrics
   - Sum of log softmax for discrete fields (event type, reg1, reg2, reg3)
   - Value field ignored (focus on discrete correctness first)

4. **Hyperparameters**:
   - `beta` (default 0.1): DPO temperature, controls preference strength
   - `lambdaDpo` (default 1.0): Weight for DPO loss
   - `learningRate` (default 1e-4): Adam optimizer learning rate

5. **DPOMetrics Record**:
   ```csharp
   record DPOMetrics(
       double Loss,
       double LogProbPreferred,
       double LogProbRejected,
       bool WasInvalid,
       int ErrorCount);
   ```

6. **Mapping Functions** (must match TransformerCpuController):
   - `MapEventTypeToIndex()`: EventType enum → 0-32 action space index
   - `MapRegisterIdToIndex()`: RegisterId enum → 0-7 action space index

**Test Coverage:**
- 8 new tests (100% passing)
- `ComputeLogProb_ValidEvent_ReturnsLogProbability` - log prob is finite and negative
- `ComputeLogProb_DifferentEvents_DifferentLogProbs` - different events have different probs
- `ComputeDPOLoss_PreferredMoreLikely_PositiveLoss` - loss is positive when preferred is more likely
- `ComputeDPOLoss_IdenticalEvents_ConstantLoss` - loss = log(2) when events are identical
- `TrainStep_InvalidEvent_UpdatesNetwork` - parameters change after backward pass
- `Constructor_AcceptsHyperparameters` - can create trainer with custom params
- `Beta_AffectsDPOLoss` - different beta values produce different losses

**Test Results: 81/81 passing** (22 Core + 59 Network)

## Key Design Decisions

### Decision 1: Tensor-Based Loss Computation (Critical for Gradients)

**Problem:** Initial implementation computed loss as a scalar, then created a new tensor with `requires_grad=true`. This doesn't connect to the computational graph, so gradients don't flow.

**Solution:** Separate tensor and scalar versions:
- `ComputeDPOLossTensor()`: Returns tensor with gradients (for training)
- `ComputeDPOLoss()`: Returns scalar (for metrics/testing)
- Same for log probability computation

**Implementation:**
```csharp
// Tensor version (with gradients)
private Tensor ComputeDPOLossTensor(Tensor actionLogits, EncodedEvent preferred, EncodedEvent rejected)
{
    var logProbPreferred = ComputeLogProbTensor(actionLogits, preferred);
    var logProbRejected = ComputeLogProbTensor(actionLogits, rejected);
    var logRatio = logProbPreferred - logProbRejected;
    return functional.softplus(-logRatio * _beta) * _lambdaDpo;
}

// Scalar version (detached, for metrics)
public double ComputeDPOLoss(Tensor actionLogits, EncodedEvent preferred, EncodedEvent rejected)
{
    using var logits = actionLogits.detach();
    var lossTensor = ComputeDPOLossTensor(logits, preferred, rejected);
    return lossTensor.item<float>();
}
```

**Trade-offs:**
- ✅ Gradients flow correctly (parameters update)
- ✅ Scalar version safe (can't accidentally backprop through metrics)
- ⚠️ Code duplication (two versions of each function)
- ⚠️ Must remember to use tensor version in training, scalar in testing

**Alternative considered:** Single function returning tensor, call `.item()` when scalar needed
- Would risk backprop through metrics
- Harder to debug (is this tensor part of graph?)

### Decision 2: DPO Only When Invalid (Skip Valid Events)

**Rationale:**
- Valid events don't need correction (no DPO pair to create)
- Running DPO on identical events (preferred = rejected) gives constant loss = log(2)
- This wastes computation and provides no training signal

**Implementation:**
```csharp
if (!validation.IsValid)
{
    preferredEvent = EventValidationDPO.Correct(generatedEvent);
    rejectedEvent = generatedEvent;
}
else
{
    // Already valid - use same event for both (baseline loss)
    preferredEvent = generatedEvent;
    rejectedEvent = generatedEvent;
}
```

**Trade-offs:**
- ✅ Efficient (only compute DPO when needed)
- ✅ Clear training signal (valid vs invalid)
- ⚠️ Network might generate mostly valid events → little DPO signal
- ⚠️ Could add "always do DPO" mode for debugging

**Alternative considered:** Always run DPO (even on valid events)
- Would provide consistent training loop
- But loss = log(2) for valid events (no signal)
- Network learns nothing from valid events

### Decision 3: Use EventValidationDPO.Correct() for Preferred Events

**Rationale:**
- Already implemented in Milestone 2
- Deterministic (same invalid event → same correction)
- Minimal changes (fixes only what's wrong)
- Semantic preservation (keeps event type, valid fields)

**Correction Strategy:**
- Missing required register → set to R0 (default)
- Unused register is set → clear to None
- Read-only destination (PC, SP) → replace with R0
- Already valid → no changes

**Trade-offs:**
- ✅ Simple and deterministic
- ✅ Consistent training signal
- ✅ Reuses existing validation logic
- ⚠️ Always corrects to R0 (might bias network)
- ⚠️ Doesn't consider multiple valid corrections

**Alternatives explored:**
1. **Multiple corrections**: Generate K candidates, pick best
   - Would explore correction space
   - But which is "best"? Need heuristic.
2. **Stochastic correction**: Random valid register instead of always R0
   - Would prevent R0 bias
   - But nondeterministic (harder to debug)
3. **Learned discriminator**: Train network to score validity
   - Would learn subtle patterns
   - But adds complexity (two networks)

**Decision:** Start with deterministic (current), revisit if network shows R0 bias.

### Decision 4: Ignore Value Field in Log Probability

**Rationale:**
- Value field is 256-dim embedding → 64-bit value (complex)
- Value decoder is randomly initialized (produces garbage)
- Focus first on discrete correctness (event type, registers)
- Value training can come later (Milestone 4)

**Implementation:**
```csharp
// Sum log probs for discrete fields only
var totalLogProb = logProbEventType + logProbReg1 + logProbReg2 + logProbReg3;
// Note: Ignoring value field
return totalLogProb;
```

**Trade-offs:**
- ✅ Simpler (fewer moving parts)
- ✅ Faster convergence (fewer dimensions to learn)
- ⚠️ Value field isn't trained (will produce random values)
- ⚠️ Network might generate wrong values even with valid structure

**Future:** Milestone 4 will add value reconstruction loss.

### Decision 5: Softplus for Numerical Stability

**Problem:** DPO loss formula: `-log(sigmoid(x))`
- `sigmoid(x) = 1 / (1 + exp(-x))`
- When `x` is large negative, `sigmoid(x) ≈ 0` → `log(0)` → `-inf`

**Solution:** Use identity `-log(sigmoid(x)) = softplus(-x)`
- `softplus(x) = log(1 + exp(x))` (always positive, numerically stable)
- When `x = 0`: `softplus(0) = log(2) ≈ 0.693`

**Implementation:**
```csharp
var logRatio = logProbPreferred - logProbRejected;
var scaledRatio = logRatio * _beta;
var loss = functional.softplus(-scaledRatio);  // Stable
```

**Trade-offs:**
- ✅ No risk of `log(0)` → `-inf`
- ✅ Gradients well-behaved
- ⚠️ Loss is always positive (even when preferred is less likely)
- ⚠️ Baseline loss = log(2) when events are identical

**Test implication:** Test expected loss = 0 for identical events failed. Corrected to expect log(2).

### Decision 6: Adam Optimizer (Not SGD)

**Rationale:**
- Adam handles sparse gradients well (many action dimensions unused per event)
- Adaptive learning rate per parameter (different layers learn at different speeds)
- Momentum helps escape local minima
- Standard choice for transformer training

**Hyperparameters:**
- `lr = 1e-4`: Conservative, prevents instability
- Could tune: `lr = 1e-3` (faster) or `lr = 1e-5` (more stable)

**Trade-offs:**
- ✅ Robust (works for most architectures)
- ✅ Fast convergence (momentum + adaptive rates)
- ⚠️ More memory (stores momentum and variance)
- ⚠️ Requires tuning (lr, betas)

**Alternative considered:** SGD with momentum
- Simpler (fewer hyperparameters)
- But slower convergence on transformers

## Insights & Observations

### What Worked Well

**1. TDD Approach (Again)**
- Wrote 8 tests before implementation
- Tests caught several issues:
  - Tensor vs scalar confusion (gradients not flowing)
  - Expected loss = 0 for identical events (wrong, should be log(2))
  - Parameter snapshot method used wrong TorchSharp API
- Refactored with confidence once tests passed

**2. Separation of Tensor and Scalar Versions**
- Clear distinction: tensor = training, scalar = metrics
- Prevents accidental backprop through metrics
- Easy to test: scalar versions use detached tensors

**3. Reusing EventValidationDPO.Correct()**
- No new logic needed for correction
- Deterministic and debuggable
- Consistent with validation rules from Milestone 2

**4. Softplus Identity**
- Elegant solution to numerical stability
- Test revealed baseline loss = log(2) (not 0)
- Led to deeper understanding of DPO loss behavior

### Challenges Faced

**1. Tensor Gradients Not Flowing**
- **Problem:** Initial implementation created new tensor with `requires_grad=true`
- **Symptom:** Parameters didn't change after training step
- **Cause:** New tensor not connected to computational graph
- **Fix:** Compute loss as tensor from logits (part of graph), then backward

**2. TorchSharp API Quirks**
- `TensorAccessor<float>.NumberOfElements` doesn't exist
- Had to use `param.mean().item<float>()` instead
- Documentation sparse - trial and error

**3. Test Expected Zero Loss for Identical Events**
- **Problem:** Test asserted `loss < 1e-5` for identical events
- **Reality:** `softplus(0) = log(2) ≈ 0.693`
- **Fix:** Updated test to expect log(2) with tolerance
- **Insight:** DPO loss is always positive (baseline when indifferent)

**4. Mapping Functions Must Match**
- **Critical:** DPOTrainer mapping must match TransformerCpuController
- **Risk:** If they diverge, log probs computed for wrong events
- **Mitigation:** Copied switch statements, added comment "must match"
- **Future:** Extract to shared static class or codegen

### Interesting Patterns

**1. DPO as "Learning from Mistakes"**
- Network generates invalid event (missing Reg1)
- Validation detects it
- Correction fixes it (set Reg1 = R0)
- DPO compares: "You generated this, but you should have generated that"
- Network learns: "Next time, set Reg1"

**2. Log Probability as "Confidence"**
- High log prob (near 0): Network is confident
- Low log prob (very negative): Network is uncertain
- DPO maximizes ratio: Prefer confident valid over uncertain invalid

**3. Beta as "Strictness"**
- High beta (1.0): Strong preference for valid over invalid
  - Fast learning, might overfit to corrections
- Low beta (0.01): Weak preference
  - Slow learning, might not converge
- Default (0.1): Middle ground

**4. Baseline Loss = Log(2)**
- When network is indifferent (equal probs), loss = log(2)
- This is the "no information" baseline
- Any loss < log(2): Network prefers one over the other
- Any loss > log(2): Impossible (softplus is convex)

**5. Discrete Correctness Before Value Correctness**
- Event type and registers are categorical (33 + 8*3 dims)
- Value is continuous (64 bits = 2^64 states)
- Easier to learn discrete first, add value later
- Curriculum learning: Simple → Complex

## Next Steps

### Immediate (Current Session - If Continuing)

**TaskRunner Integration (Phase 3.2 in TODO.md):**
1. Create `DPOWarmupConfig` with parameters:
   - `MaxEpisodes`: Cap for warmup (e.g., 100)
   - `InvalidRateThreshold`: End warmup when invalid rate < 5%
   - `MetricsInterval`: Log metrics every N episodes
2. Modify `TaskRunner.RunEpisode()` to accept optional DPOTrainer
3. Add `TaskRunner.RunDPOWarmup()`:
   - Loop: Generate state → TrainStep → Track metrics
   - Stop when: InvalidRate < threshold OR episodes >= max
   - Return: WarmupMetrics (final invalid rate, episodes, loss curve)
4. Test: Verify invalid rate decreases over warmup

**Expected effort:** 2-3 hours
**Expected outcome:** DPO warmup runs, invalid event rate drops from ~50% to <10%

### Short-Term (Next Session)

**Mixed Training (Phase 3.2 in TODO.md):**
1. Combine DPO loss + task reward:
   ```csharp
   totalLoss = lambdaTask * taskLoss + lambdaDpo * dpoLoss;
   ```
2. Add policy gradient training:
   - REINFORCE or PPO for task reward
   - DPO for validity
3. Track metrics:
   - Task success rate
   - Invalid event rate
   - DPO loss vs task loss
4. Test on Echo/Increment/Reverse tasks

**Metrics Tracking (Phase 3.2 in TODO.md):**
1. Create `TrainingMetrics` class:
   - Invalid event rate (sliding window)
   - Most common errors (histogram)
   - Loss curves (DPO, task, combined)
   - Event type distribution (does network overuse R0?)
2. Visualization:
   - Plot invalid rate over time (should decrease)
   - Plot loss curves (should converge)
   - Heatmap of event types (diversity)

**Expected effort:** 5-7 hours
**Expected outcome:** Network learns valid events AND solves tasks

### Medium-Term (Next 1-2 Weeks)

**Milestone 4: Value Encoding (TODO.md Phase 4)**
1. Add value reconstruction loss:
   ```csharp
   var predictedValue = DecodeValue(valueEmbed);
   var actualValue = groundTruth.Value;
   var valueLoss = MSE(predictedValue, actualValue);
   totalLoss += lambdaValue * valueLoss;
   ```
2. Experiment with value representations:
   - Binary (64 sigmoid outputs) ← current
   - Logarithmic (exponent + mantissa)
   - Learned embedding (VAE-style)
3. Add value-aware validation:
   - Memory addresses < capacity
   - Interrupt vectors < 256

**Milestone 5: End-to-End Validation (TODO.md Phase 5)**
1. Integration tests: Run full tasks with neural controller
2. Measure success rates (goal: 100% on Echo/Increment/Reverse)
3. Compare to CycleMockNetwork (rule-based baseline)
4. Benchmark training time and convergence speed

**Expected effort:** 10-15 hours
**Expected outcome:** Neural controller matches or exceeds CycleMockNetwork performance

### Long-Term Research Directions

**Stochastic Correction:**
- Phase 1: Deterministic (R0 always) ← current
- Phase 2: Random valid register after basics learned
- Prevents R0 bias, encourages generalization

**Learned Validity Discriminator (TODO.md Phase 6):**
- Train discriminator: EncodedEvent → P(valid)
- Replace hand-coded rules with learned constraints
- Study what patterns it discovers (interpretability)

**Multi-Way DPO:**
- Instead of single preferred vs rejected
- Generate K corrections, rank by validity score
- DPO on full ranking: `π(best) > π(2nd) > π(3rd) > ... > π(invalid)`

**Counterfactual DPO:**
- Fix ONE error at a time (instead of all errors)
- Network learns: "Changing Reg1 made it valid, but changing Reg2 didn't"
- More granular training signal

## Questions & Uncertainties

### Training Questions

**Q1: What's the optimal DPO warmup duration?**
- Too short: Network doesn't learn validity
- Too long: Wastes time when already learned
- **Approach:** Track invalid rate, end when <5% OR cap at 100 episodes
- **Uncertain:** Is 5% the right threshold? Should be 1%? 10%?

**Q2: Should we train with DPO throughout, or only during warmup?**
- Option A: DPO warmup (pure validity) → task training (no DPO)
  - Cleaner separation of concerns
  - But network might "forget" validity when learning task
- Option B: Mixed training (DPO + task) throughout
  - Maintains validity while learning task
  - But two competing objectives might interfere
- **Approach:** Try both, ablate

**Q3: What's the right beta value?**
- Standard range: 0.01 - 1.0 (from DPO paper)
- Default: 0.1 (middle of log scale)
- **Uncertain:** Optimal for this task? Try grid search: [0.01, 0.1, 0.5, 1.0]

**Q4: How to balance DPO loss vs task loss?**
- `totalLoss = lambdaTask * taskLoss + lambdaDpo * dpoLoss`
- If lambdaDpo too high: Network focuses on validity, ignores task
- If lambdaDpo too low: Network solves task, ignores validity
- **Approach:** Start with equal weights (1.0, 1.0), tune based on metrics

### Implementation Questions

**Q5: Should we anneal beta during training?**
- Start high (strong preference) → anneal to low (weak preference)
- Hypothesis: Strong early signal helps bootstrap, then relax
- **Alternative:** Start low, anneal high (opposite hypothesis)
- **Decision:** Start fixed, add annealing if needed

**Q6: How to handle multi-error events?**
- Event with 3 errors (missing Reg1, wrong Reg2, PC as destination)
- Current correction fixes all 3 at once
- **Alternative:** Fix one error per step (curriculum)
- **Uncertain:** Does fixing all at once overwhelm network?

**Q7: Should DPO loss be normalized by error count?**
- Event with 1 error vs event with 3 errors
- Should they have same loss, or should 3-error be higher?
- Current: Same loss (correction distance not considered)
- **Alternative:** `loss *= errorCount` (penalize more errors)

### Architectural Questions

**Q8: Should we share optimizer between DPO and task training?**
- Current: DPOTrainer has its own optimizer
- Alternative: TaskRunner creates optimizer, passes to both trainers
- Trade-off: Separate optimizers allow different learning rates

**Q9: How to prevent catastrophic forgetting?**
- Network learns validity (DPO warmup)
- Then learns task (task training)
- Risk: Task training overwrites validity knowledge
- **Mitigation:** Keep DPO loss during task training (mixed)
- **Alternative:** Freeze some layers after warmup

**Q10: Should we use a reference model for DPO?**
- Standard DPO uses reference model π_ref (frozen copy)
- Loss: `log(π(preferred) / π(rejected)) - log(π_ref(preferred) / π_ref(rejected))`
- Prevents policy from drifting too far
- Current: No reference model (simpler)
- **Decision:** Start without, add if policy becomes unstable

## Reflections on Session

### Evolution of Understanding

This session deepened understanding of **DPO as a training paradigm**:

1. **DPO = Comparative Learning**
   - Not "this is right, this is wrong" (supervised)
   - But "this is better than that" (preference)
   - Network learns relative quality, not absolute correctness

2. **Loss Function Behavior**
   - Baseline (indifferent): log(2) ≈ 0.693
   - Preferred more likely: loss < log(2)
   - Preferred less likely: loss > log(2) (gradient pushes toward preferred)
   - Loss is always positive (softplus)

3. **Gradients Flow Through Actions**
   - Network doesn't see "invalid" label
   - Sees: "You assigned prob X to this event, but it should be higher"
   - Gradients adjust logits to increase prob of preferred

4. **Correction as Oracle**
   - EventValidationDPO.Correct() is the "teacher"
   - Deterministic teacher → consistent lessons
   - Network learns to mimic teacher's corrections

### What I'd Do Differently

**1. Start with Tensor Versions**
- Should have written tensor-based loss first
- Scalar versions are just wrappers (detach + item)
- Would have avoided gradient flow bug

**2. Test Loss Behavior Earlier**
- Should have verified `softplus(0) = log(2)` before writing test
- Test assumption (loss = 0 for identical events) was wrong
- Could have read DPO paper more carefully

**3. Extract Mapping Functions**
- Having same switch statements in two files is brittle
- Should create `ActionSpaceMapping` static class
- Used by both TransformerCpuController and DPOTrainer

### What Went Really Well

**1. Clean Separation of Concerns**
- DPOTrainer owns training logic
- EventValidationDPO owns correction logic
- TransformerCpuController owns inference logic
- Each class has single responsibility

**2. Metrics as First-Class Citizen**
- DPOMetrics record captures all relevant info
- Can track training progress
- Can debug: "Why did this step have high loss?"

**3. Test Coverage**
- 8 tests cover all critical paths
- Tests caught subtle bugs (gradients, loss baseline)
- Can refactor with confidence

### Excitement for Next Steps

The DPO infrastructure is complete! The next phase (TaskRunner integration) will be where we **see the network actually learn**. Until now:
- Milestone 1: Network can decode actions (but randomly)
- Milestone 2: Validation system (but doesn't train network)
- Milestone 3: DPO trainer (but hasn't run on real data yet)

**Next session will close the loop:**
- Generate many invalid events
- Train on corrected versions
- Watch invalid rate drop from 50% → 10% → 5% → 1%
- First time seeing "intelligence emerge" from randomness

This is the most exciting milestone yet. The network will go from producing garbage to producing structured, valid CPU events - without any hand-coded rules, just by learning to prefer valid over invalid.

---
*This journal will be read by future Claude instances to understand project evolution.*

**Status:** Milestone 3 complete. DPO training infrastructure implemented and tested. Ready for TaskRunner integration and warmup experiments.

**Commits this session**:
- (This commit): Milestone 3 - DPO training infrastructure with tensor-based gradients
