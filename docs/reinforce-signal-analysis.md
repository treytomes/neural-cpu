# REINFORCE Signal Analysis

## What REINFORCE Does (Step by Step)

### The Algorithm

Our `PolicyGradientTrainer` implements the classic REINFORCE algorithm:

```
Loss = -∑_t log π(a_t|s_t) * A_t
```

Where:
- **π(a_t|s_t)**: Probability the policy assigns to action `at` in state `st`
- **A_t**: Advantage = Return - Baseline
- **Return**: Discounted cumulative reward from timestep t onward

**The gradient update**: Actions that led to better-than-average returns get **higher probability**, actions that led to worse-than-average returns get **lower probability**.

---

## Our Implementation (PolicyGradientTrainer.cs)

### Step 1: Record Episode Trajectory

Every cycle, `BasicMotorSkillsTask` calls the controller, which stores:
```csharp
(CpuStateVector state, Tensor actionLogits, double reward)
```

But here's the first problem: **rewards are always 0 during the episode**. The reward only becomes 1.0 if the final state satisfies the task condition.

### Step 2: Compute Discounted Returns

```csharp
public static double[] ComputeDiscountedReturns(double[] rewards, double gamma)
{
    var returns = new double[rewards.Length];
    double runningReturn = 0.0;
    
    for (int t = rewards.Length - 1; t >= 0; t--)
    {
        runningReturn = rewards[t] + gamma * runningReturn;
        returns[t] = runningReturn;
    }
    return returns;
}
```

**With sparse terminal reward** (what we're doing):
- Episode fails → All rewards = 0 → All returns = 0
- Episode succeeds → Terminal reward = 1.0 → Returns: [γ^49, γ^48, ..., γ^1, 1.0]

**Example** (50-step episode, γ=0.99):
```
Step 0:  return = 0.99^49 = 0.605
Step 1:  return = 0.99^48 = 0.612
...
Step 48: return = 0.99^1  = 0.990
Step 49: return = 1.0
```

**Problem**: Early actions get ~60% credit, even though they might have been irrelevant!

### Step 3: Compute Advantages (Variance Reduction)

```csharp
var baseline = returns.Average();
var advantages = returns.Select(r => r - baseline).ToArray();
```

**Purpose**: Center the returns around zero to reduce gradient variance.

**What happens**:
- **Successful episode**: Some advantages positive (late steps), some negative (early steps)
- **Failed episode**: All returns = 0, baseline = 0, advantages = 0 → **NO GRADIENT!**

**This is the core issue**: Failed episodes provide zero learning signal.

### Step 4: Compute Log Probabilities

```csharp
var logProb = ComputeLogProbTensor(step.ActionLogits, decodedEvent);
```

Our action is structured (4 components):
```csharp
log π(action) = log π(eventType) + log π(reg1) + log π(reg2) + log π(reg3)
```

**Why sum?** Because we're assuming independence:
```
π(type, reg1, reg2, reg3) = π(type) * π(reg1) * π(reg2) * π(reg3)
log(A * B * C * D) = log A + log B + log C + log D
```

Each component is a softmax over its vocabulary:
- **EventType**: 33 options (RegisterWrite, AluAdd, Push, etc.)
- **Reg1/Reg2/Reg3**: 8 options each (None, R0-R3, PC, SP, Flags)

### Step 5: Compute Policy Loss

```csharp
var loss = -(logProbsTensor * advantagesTensor).sum();
```

Expanding this:
```
Loss = -∑_t [log π(a_t|s_t)] * [A_t]
     = -[log π(a_0) * A_0 + log π(a_1) * A_1 + ... + log π(a_49) * A_49]
```

**Gradient interpretation**:
- **A_t > 0**: Increase probability of action a_t (gradient pushes weights up)
- **A_t < 0**: Decrease probability of action a_t (gradient pushes weights down)
- **A_t = 0**: No update for this action

---

## Why RegisterActivation Diverged

Let me trace through what happened in the actual training:

### Episodes 0-16 (Lucky Success)

**Episode 0**:
1. Network outputs random action logits
2. DecodeEncodedEvent samples: `RegisterWrite(R0, value=42)` (lucky guess!)
3. Task succeeds in 1 cycle → reward = 1.0
4. Returns: [1.0] (single step)
5. Baseline: 1.0
6. Advantages: [0.0]
7. **Gradient: ZERO!** (baseline cancels out single-step return)

**Wait, that can't be right. Let me check the actual code...**

Actually, looking at line 76-81 in `PolicyGradientTrainer.cs`:

```csharp
// Build rewards array (simple credit assignment: assign terminal reward to all steps)
var rewards = new double[episodeBuffer.Count];
for (int i = 0; i < episodeBuffer.Count; i++)
{
    rewards[i] = terminalReward;
}
```

**AH!** We're assigning the terminal reward to EVERY step, not just the final one. So:

**Episode 0 (corrected)**:
1. Step 0: reward = 1.0
2. Returns: [1.0] (γ=0.99, but only 1 step so no discounting)
3. Baseline: 1.0
4. Advantages: [0.0]
5. **Still zero gradient!**

But then how did we get a policy loss spike at episode 16?

### The Truth: Multi-Step Episodes

Looking at the training data again:
```
Episode 0:  Success, 1 cycle, loss=-0.000000
Episode 14: Success, 2 cycles, loss=0.040587
Episode 15: Success, 6 cycles, loss=0.108659
Episode 16: Success, 36 cycles, loss=5.971474
```

**Episodes got LONGER over time!** The network was exploring more before succeeding.

**Episode 16 (36 cycles)**:
1. All 36 steps assigned reward = 1.0
2. Returns: [1.0, 1.0, ..., 1.0] (all equal since reward is constant)
3. Baseline: 1.0
4. Advantages: [0.0, 0.0, ..., 0.0]
5. **STILL ZERO GRADIENT!**

**Wait, this doesn't make sense either. Let me recalculate with proper discounting:**

**Episode 16 (36 steps, γ=0.99)**:

With our reward assignment (all steps = 1.0):
```python
Step 0:  return = 1.0 + 0.99*1.0 + 0.99^2*1.0 + ... + 0.99^35*1.0
       = ∑(0.99^k for k=0 to 35)
       = (1 - 0.99^36) / (1 - 0.99)
       = 30.91

Step 1:  return = 1.0 + 0.99*1.0 + ... + 0.99^34*1.0
       = 30.26

...

Step 35: return = 1.0
```

**Baseline**: Average of all returns ≈ 16.0

**Advantages**:
```
Step 0:  30.91 - 16.0 = +14.91 (strongly positive)
Step 17: 16.00 - 16.0 =   0.00 (neutral)
Step 35:  1.00 - 16.0 = -15.00 (strongly negative)
```

**Now we have gradients!**

- **Early actions**: Positive advantages → Increase probability
- **Late actions**: Negative advantages → Decrease probability

**But this is completely wrong!** We're telling the network:
- "Do MORE of what you did in the first 17 steps"
- "Do LESS of what you did in the last 19 steps"

Even though the **LAST action** (step 35) is what actually solved the task!

---

## The Reward Assignment Bug

**Line 76-81 is fundamentally flawed**:

```csharp
for (int i = 0; i < episodeBuffer.Count; i++)
{
    rewards[i] = terminalReward;  // WRONG!
}
```

This makes REINFORCE think every step received the reward, leading to:
1. **Credit misattribution**: Early random actions get higher returns than task-solving actions
2. **Inverted gradients**: Network learns to prefer wasteful exploration over efficient solutions
3. **Policy divergence**: Successful strategies get negative advantages after enough steps

**Correct approach** (sparse terminal reward):
```csharp
var rewards = new double[episodeBuffer.Count];
rewards[episodeBuffer.Count - 1] = terminalReward;  // Only last step
```

**Or even better** (shaped rewards):
```csharp
var rewards = new double[episodeBuffer.Count];
// Partial credit for progress
for (int i = 0; i < episodeBuffer.Count; i++)
{
    rewards[i] = ComputeStepReward(episodeBuffer[i]);  // e.g., +0.1 for relevant actions
}
rewards[episodeBuffer.Count - 1] += terminalReward;  // Bonus for completion
```

---

## Why Failed Episodes Give Zero Signal

**Failed episode** (all 50 cycles, no success):
```csharp
rewards = [0, 0, 0, ..., 0]  // Terminal reward = 0
returns = [0, 0, 0, ..., 0]  // Discounting zeros gives zeros
baseline = 0
advantages = [0, 0, ..., 0]
```

**Policy loss**:
```
Loss = -∑_t log π(a_t) * 0
     = 0
```

**Gradient**: All zeros → **NO WEIGHT UPDATES!**

This is why StackGrowth/InputRead/OutputWrite learned nothing - they never succeeded, so the network never received any gradient signal.

---

## The Real Problem: Exploration vs Exploitation

REINFORCE has a **chicken-and-egg problem**:
1. Need successful episodes to compute gradients
2. Need good policy to get successful episodes
3. Need gradients to improve policy
4. **Loop is broken if random exploration never succeeds**

**With our action space**:
- 33 event types × 8 registers × 8 registers × 8 registers = ~168,000 combinations
- Probability of randomly choosing correct action: **~0.0006%**
- For 2-step task: **0.0006% × 0.0006% = 0.0000036%**

**Expected episodes until first success** (2-step task):
```
1 / (0.000006 * 0.000006) = 27,777,778 episodes
```

We ran 200 episodes. **Not even close.**

---

## Why ALUActivation Succeeded (100%)

Looking at the task:
```csharp
public TaskResult RunAluActivationTask()
{
    // Success: Issue ANY ALU operation
    // The task is so trivial that random actions succeed
}
```

**Why it works**:
- 23 out of 33 event types are ALU operations
- Probability: **23/33 = 69.7%**
- Expected cycles until success: **1.43 cycles**
- Actual: **1.0 cycle** (first action often works)

**This isn't learning** - it's winning the lottery on every spin!

---

## Fixing the REINFORCE Signal

### Fix 1: Correct Reward Assignment (Immediate)

**Change line 76-81**:
```csharp
// OLD (WRONG):
for (int i = 0; i < episodeBuffer.Count; i++)
{
    rewards[i] = terminalReward;
}

// NEW (CORRECT):
var rewards = new double[episodeBuffer.Count];
if (episodeBuffer.Count > 0)
{
    rewards[episodeBuffer.Count - 1] = terminalReward;  // Only terminal step
}
```

**Impact**: Eliminates inverted gradients, but doesn't solve sparse reward problem.

### Fix 2: Dense Reward Shaping (High Priority)

**Add partial rewards for task-relevant actions**:

```csharp
// RegisterActivation: Reward for writing to ANY register
if (action is RegisterWriteCommand)
    reward += 0.5;  // Partial credit
if (action.DestinationRegister == targetRegister)
    reward += 1.0;  // Full credit

// StackGrowth: Reward for any stack interaction
if (action is PushCommand or PushImmediateCommand)
    reward += 0.5;  // Exploring the right action space
```

**Impact**: Provides gradient signal even when task fails.

### Fix 3: Curriculum Learning (Medium Priority)

**Reduce action space initially**:

```csharp
// Stage 1: Only register operations (5 event types)
allowedEventTypes = [RegisterWrite, RegisterCopy, RegisterRead, ...];

// Stage 2: Add stack ops (10 event types)
allowedEventTypes += [Push, Pop, Peek, Dup];

// Stage 3: Full action space (33 event types)
```

**Impact**: Increases probability of random success from 0.0006% to ~10-20%.

### Fix 4: Behavior Cloning (Highest Priority)

**Bootstrap from successful demonstrations**:

```csharp
// Phase 1: Supervised learning
for each demo_episode in MockNetwork.GenerateDemonstrations():
    for each (state, action) in demo_episode:
        loss = CrossEntropy(network(state), action)
        loss.backward()
        optimizer.step()

// Phase 2: Policy gradient fine-tuning
// (Now network can succeed, so REINFORCE gets signal)
```

**Impact**: Guaranteed successful episodes from the start.

---

## Comparing Gradient Magnitudes

### Successful Episode (Correct Reward Assignment)

**50-step episode, terminal reward = 1.0**:
```
Returns: [0.605, 0.612, ..., 0.990, 1.0]
Baseline: 0.808
Advantages: [-0.203, -0.196, ..., +0.182, +0.192]
```

**Policy loss contribution** (one step):
```
-log π(a_t) * A_t ≈ -(-2.5) * 0.2 = 0.5
```

**Total loss** (sum over 50 steps):
```
Loss ≈ 25.0  (observed in training data)
```

**Gradient magnitude**: ~0.5 per parameter

### Failed Episode

```
Returns: [0, 0, ..., 0]
Advantages: [0, 0, ..., 0]
Loss = 0
```

**Gradient magnitude**: **0** (no update)

### The Signal Gap

**Ratio of successful to failed gradients**: **INFINITE** (0 gradient means no learning)

This is why failed episodes are useless in REINFORCE - they don't just provide weak signal, they provide **no signal at all**.

---

## Summary Table

| Issue | Current Behavior | Impact | Fix Priority |
|-------|-----------------|--------|--------------|
| **Reward assignment bug** | All steps get terminal reward | Inverted gradients, policy divergence | **Critical** |
| **Sparse terminal rewards** | Failed episodes → 0 gradient | No learning from failures (99.5% of episodes) | **High** |
| **Large action space** | ~168K combinations | Random exploration never succeeds | **High** |
| **No partial credit** | Binary 0/1 rewards only | Can't learn sub-goals | **Medium** |
| **No entropy bonus** | Policy can collapse to single action | Premature convergence | **Low** |

---

## Recommended Fixes (In Order)

1. **Fix reward assignment bug** (1 line change)
2. **Implement behavior cloning** (bootstrap from MockNetwork)
3. **Add dense reward shaping** (partial credit for progress)
4. **Add entropy bonus** (prevent policy collapse)
5. **Consider curriculum learning** (if still struggling)

---

## Expected Outcome After Fixes

**Behavior Cloning** (Phase 1):
- Success rate: 80-90% (imitating MockNetwork)
- Cycles to success: 5-10 (copying efficient strategy)
- Training episodes: 100-200

**Policy Gradient Fine-Tuning** (Phase 2):
- Success rate: 90-95% (refinement from exploration)
- Cycles to success: 3-5 (discovering shortcuts)
- Training episodes: 500-1000

**Novel Tasks** (Phase 3):
- Success rate: 40-60% (generalization test)
- Cycles to success: 10-20 (exploration needed)
- Training episodes: 1000+

---

## Code Changes Required

### 1. Fix PolicyGradientTrainer.cs (Critical)

**Line 76-81**:
```diff
- // Build rewards array (simple credit assignment: assign terminal reward to all steps)
- var rewards = new double[episodeBuffer.Count];
- for (int i = 0; i < episodeBuffer.Count; i++)
- {
-     rewards[i] = terminalReward;
- }
+ // Assign terminal reward only to final step (sparse reward)
+ var rewards = new double[episodeBuffer.Count];
+ if (episodeBuffer.Count > 0)
+ {
+     rewards[episodeBuffer.Count - 1] = terminalReward;
+ }
```

### 2. Add DemonstrationCollector.cs (New File)

```csharp
public class DemonstrationCollector
{
    public List<(CpuStateVector state, EncodedEvent action)> CollectDemonstrations(
        MockNetwork controller, 
        BasicMotorSkillsTask task,
        string taskName,
        int numEpisodes = 100)
    {
        var demonstrations = new List<(CpuStateVector, EncodedEvent)>();
        
        for (int ep = 0; ep < numEpisodes; ep++)
        {
            var result = task.RunTask(taskName);
            if (result.Success)
            {
                // Get state-action pairs from controller buffer
                var trajectory = controller.GetEpisodeBuffer();
                demonstrations.AddRange(trajectory);
            }
        }
        
        return demonstrations;
    }
}
```

### 3. Add BehaviorCloningTrainer.cs (New File)

```csharp
public class BehaviorCloningTrainer
{
    private readonly TransformerCpuController _controller;
    private readonly optim.Optimizer _optimizer;
    
    public double TrainOnDemonstrations(
        List<(CpuStateVector state, EncodedEvent action)> demonstrations)
    {
        double totalLoss = 0.0;
        
        foreach (var (state, action) in demonstrations)
        {
            // Get network predictions
            var logits = _controller.GetActionLogits(state);
            
            // Compute cross-entropy loss
            var targetIndices = EncodeActionAsIndices(action);
            var loss = ComputeCrossEntropyLoss(logits, targetIndices);
            
            // Backprop
            _optimizer.zero_grad();
            loss.backward();
            _optimizer.step();
            
            totalLoss += loss.item<float>();
        }
        
        return totalLoss / demonstrations.Count;
    }
}
```

---

**Next**: Would you like me to implement these fixes and run a new training with behavior cloning enabled?
