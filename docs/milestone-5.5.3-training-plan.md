# Milestone 5.5.3 Training Plan

## Basic Motor Skills Training

**Goal**: Teach the neural network to control individual CPU components in isolation.

## Five Component Mastery Tasks

### 1. RegisterActivation
**Objective**: Make any general-purpose register (R0-R3) non-zero  
**Success Condition**: At least one of R0-R3 !=  0  
**Learning Challenge**: Network must discover `RegisterWriteCommand` event type  
**Expected Difficulty**: ⭐ Easy - simplest possible action  

### 2. StackGrowth
**Objective**: Increase stack size from 0  
**Success Condition**: Stack size (SP) > 0  
**Learning Challenge**: Network must discover `PushCommand` or `PushImmediateCommand`  
**Expected Difficulty**: ⭐ Easy - direct single action  

### 3. ALUActivation
**Objective**: Execute any ALU operation that changes a register  
**Success Condition**: Flags register changes (ALU ops set flags)  
**Learning Challenge**: Network must discover any of 21 ALU operations  
**Expected Difficulty**: ⭐⭐ Medium - requires valid ALU command structure  

### 4. InputRead
**Objective**: Read a value from input device 0  
**Success Condition**: `InputReadRequest` event issued to device 0  
**Learning Challenge**: Network must discover device I/O and deviceId=0  
**Expected Difficulty**: ⭐⭐⭐ Hard - new action space dimension (DeviceId)  

### 5. OutputWrite
**Objective**: Write any value to output device 0  
**Success Condition**: `OutputWriteCommand` event issued to device 0  
**Learning Challenge**: Network must discover output operations and deviceId=0  
**Expected Difficulty**: ⭐⭐⭐ Hard - device I/O coordination  

## Training Configuration

**Network Architecture**:
- Embedding Dim: 128
- Attention Heads: 4
- Encoder Layers: 2
- Dropout: 0.1

**Training Hyperparameters**:
- Episodes per task: 200
- Max cycles per episode: 50
- Learning rate: 1e-4
- Discount factor (γ): 0.99
- Optimizer: Adam

**Reward Structure**:
- Binary rewards: 1.0 for success, 0.0 for failure
- Success = task condition met within max cycles
- Early termination on success (saves cycles)

## Expected Learning Curves

**Optimistic Scenario** (Network learns quickly):
- RegisterActivation: 80%+ success by episode 50
- StackGrowth: 80%+ success by episode 50  
- ALUActivation: 70%+ success by episode 100
- InputRead: 60%+ success by episode 150
- OutputWrite: 60%+ success by episode 150

**Realistic Scenario** (Gradual learning):
- RegisterActivation: 80%+ success by episode 100
- StackGrowth: 80%+ success by episode 100
- ALUActivation: 60%+ success by episode 150
- InputRead: 40%+ success by episode 200
- OutputWrite: 40%+ success by episode 200

**Pessimistic Scenario** (Slow learning or plateau):
- All tasks: <30% success after 200 episodes
- Possible causes:
  - Learning rate too low
  - Network too small
  - Exploration insufficient
  - Need body discovery pre-training (Milestone 5.5.2)

## Success Metrics

**Per-Task Metrics**:
1. **Success Rate**: Percentage of episodes that achieved the goal
2. **First Success**: Episode number where task first succeeded
3. **Average Cycles**: Mean cycles to success (for successful episodes)
4. **Policy Loss**: Trend over episodes (should decrease)

**Overall Assessment**:
- **Complete Success**: All 5 tasks reach 60%+ success rate
- **Partial Success**: 3-4 tasks reach 60%+, others show improvement
- **Needs Refinement**: <3 tasks succeed, requires hyperparameter tuning or architecture changes

## Training Execution

### Quick Test (Validation)
```bash
# Run 3 episodes per task to verify integration
dotnet test tests/NeuralCpu.Network.Tests/NeuralCpu.Network.Tests.csproj \
  --filter "MotorSkillsTraining_ExecutesWithoutErrors"
```

### Full Training Run
```bash
# Run 200 episodes per task (long-running)
dotnet test tests/NeuralCpu.Network.Tests/NeuralCpu.Network.Tests.csproj \
  --filter "MotorSkillsTraining_FullRun_ShowsLearning"
```

Or use the console program:
```csharp
MotorSkillsTrainingProgram.Run(loggerFactory);
```

## Next Steps After Training

### If Training Succeeds (>60% on 3+ tasks):
1. **Milestone 5.5.4**: Coordination Tasks
   - Multi-step sequences (RegisterCopy, RegisterSwap, ALUCompute)
   - Device-to-register coordination (InputToRegister, RegisterToOutput)

2. **Milestone 5.5.5**: Goal-Oriented Tasks
   - Echo, Increment, Reverse (now achievable!)

### If Training Plateaus (<30% on most tasks):
1. **Milestone 5.5.2**: Body Discovery Training (intrinsic rewards)
   - Pre-train with curiosity-driven exploration
   - Then retry motor skills training

2. **Hyperparameter Tuning**:
   - Increase learning rate (1e-3)
   - Larger network (256 dim, 8 heads, 4 layers)
   - More episodes (500 per task)

## Analysis Plan

After training completes, analyze:

1. **Learning Curves**: Plot success rate over episodes per task
2. **Action Distribution**: What events did the network learn to generate?
3. **Failure Modes**: What went wrong in failed episodes?
4. **Generalization**: Does success in early tasks help later tasks?
5. **Cycle Efficiency**: Do successful episodes use fewer cycles over time?

## Experiment Log

**Run 1** (Quick Test - 3 episodes):
- Date: 2026-06-27
- Status: Running...
- Purpose: Validate integration, check for errors
- Expected: All 5 tasks execute without crashes

**Run 2** (Full Training - 200 episodes):
- Date: TBD
- Status: Pending Run 1 validation
- Purpose: Measure actual learning
- Expected: See learning curves, success rates

---

**Document Status**: Active training plan for Milestone 5.5.3  
**Last Updated**: 2026-06-27 18:30:00  
**Next Review**: After first full training run
