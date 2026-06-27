# Neural CPU Hyperparameters

Complete reference for all hyperparameters in the TransformerCpuController and training system.

## Network Architecture Hyperparameters

### TransformerCpuController

#### Core Architecture
| Parameter | Default | Motor Skills | Integration Test | Description |
|-----------|---------|--------------|------------------|-------------|
| `embeddingDim` | 256 | **128** | 64 | Dimension of all embeddings and hidden states |
| `numHeads` | 8 | **4** | 2 | Number of attention heads in multi-head attention |
| `numEncoderLayers` | 4 | **2** | 1 | Number of transformer encoder layers for event/CPU streams |
| `device` | CPU | CPU | CPU | Torch device (CPU or CUDA) |
| `isTraining` | true | true | true | Whether to buffer episodes for training |

#### Derived Architecture Parameters
| Parameter | Formula | Motor Skills | Description |
|-----------|---------|--------------|-------------|
| FFN Hidden Dim | `embeddingDim * 4` | 512 | Feed-forward network hidden dimension |
| Event Sequence Length | 64 (fixed) | 64 | Max events in history |
| CPU State Length | 32 (fixed) | 32 | Registers (7) + stack (8) + memory (16) + padding |

#### Fixed Architecture Constants
| Parameter | Value | Description |
|-----------|-------|-------------|
| Dropout Rate | 0.1 | Dropout probability in all layers |
| Action Space Dimension | 331 | Output dimension: event type (35) + registers (3×8) + deviceId (16) + value (256) |
| Value Embedding Dimension | 256 | Dimension for 64-bit value encoding |
| Event Encoding Fields | 3 | (type, register, value) before embedding |

### Mixture of Experts (MoE) Network

When `moeNetwork = null` (default), creates standard MoE:

| Parameter | Default | Description |
|-----------|---------|-------------|
| Input Dim | `embeddingDim` | Same as controller embedding |
| Hidden Dim | `embeddingDim` | Expert hidden layer size |
| Output Dim | `embeddingDim` | Expert output size |
| Expert Count | 4 | Standard experts: Compute, Memory, Stack, Control |
| Expert Depth | 2 | Layers per expert module |
| Top-K | 2 | Number of experts activated per forward pass |

## Training Hyperparameters

### PolicyGradientTrainer (REINFORCE)

| Parameter | Default | Motor Skills | Description |
|-----------|---------|--------------|-------------|
| `gamma` (γ) | 0.99 | **0.99** | Discount factor for future rewards |
| `learningRate` | 1e-4 | **1e-4** | Adam optimizer learning rate |
| Optimizer | Adam | Adam | Gradient descent algorithm |

#### Adam Optimizer (TorchSharp defaults)
| Parameter | Value | Description |
|-----------|-------|-------------|
| beta1 | 0.9 | Exponential decay for 1st moment |
| beta2 | 0.999 | Exponential decay for 2nd moment |
| epsilon | 1e-8 | Numerical stability term |
| weight_decay | 0 | L2 regularization (disabled) |

### Episode Configuration

| Parameter | Motor Skills | Integration Test | Description |
|-----------|--------------|------------------|-------------|
| `episodesPerTask` | 200 | 3 | Number of training episodes per task |
| `maxCyclesPerEpisode` | 50 | 20 | Max CPU cycles per episode |

## State Vector Configuration

### StateVectorOptions

| Parameter | Value | Description |
|-----------|-------|-------------|
| `EventHistorySize` | 64 | Number of recent events to include |
| `StackSnapshotSize` | 8 | Top N stack items to include |
| `MemorySnapshotSize` | 16 | Number of memory locations to sample |

### TaskRunnerOptions

| Parameter | Value | Description |
|-----------|-------|-------------|
| `MaxCyclesPerEpisode` | 50 | Episode timeout |
| `InputBufferAddress` | 0x80000000 | Memory-mapped input buffer start |
| `InputLengthAddress` | 0x80000FF8 | Input length location |
| `OutputBufferAddress` | 0x80001000 | Memory-mapped output buffer start |
| `OutputLengthAddress` | 0x80001FF8 | Output length location |

## Action Space Breakdown

**Total Action Dimension: 331**

| Component | Dimension | Method | Description |
|-----------|-----------|--------|-------------|
| Event Type | 35 | Softmax | Which CPU event to emit |
| Register 1 | 8 | Softmax | First register operand (None, R0-R3, PC, SP, Flags) |
| Register 2 | 8 | Softmax | Second register operand |
| Register 3 | 8 | Softmax | Third register operand |
| Device ID | 16 | Softmax | Which I/O device (0-15) |
| Value | 256 | Sigmoid | 64-bit value encoding (4 bits per dimension) |

**Event Types Supported (35 total):**
- Registers: RegisterWrite (1)
- Stack: Push, Pop, Peek, Dup, PushImmediate (5)
- Memory: LoadDirect, LoadIndirect, StoreDirect, StoreIndirect (4)
- ALU Binary: Add, Sub, Mul, Div, And, Nand, Or, Nor, Xor, Xnor, Shl, Shr, Sar, Rol, Ror (15)
- ALU Unary: Not, Inc, Dec, Neg, Abs, Clz (6)
- I/O: InputRead, OutputWrite (2)
- Control: RegisterCopy, RegisterWriteWithFlags (2)

## Configuration Profiles

### Quick Test Profile (Integration Tests)
**Purpose**: Fast validation, check for crashes  
**Use Case**: CI/CD, development iteration

```csharp
embeddingDim: 64
numHeads: 2
numEncoderLayers: 1
episodesPerTask: 3
maxCyclesPerEpisode: 20
```

**Training Time**: ~2 seconds (3 episodes × 5 tasks)  
**Expected Results**: No crashes, some random successes

### Motor Skills Profile (Current)
**Purpose**: Component mastery training  
**Use Case**: Learning individual component control

```csharp
embeddingDim: 128
numHeads: 4
numEncoderLayers: 2
episodesPerTask: 200
maxCyclesPerEpisode: 50
learningRate: 1e-4
```

**Training Time**: ~30-60 minutes (200 episodes × 5 tasks)  
**Expected Results**: 60%+ success on simple tasks, 20-40% on hard tasks

### Large Network Profile (If Motor Skills Plateaus)
**Purpose**: More capacity for complex patterns  
**Use Case**: Retry training with larger network

```csharp
embeddingDim: 256
numHeads: 8
numEncoderLayers: 4
episodesPerTask: 500
maxCyclesPerEpisode: 50
learningRate: 1e-4
```

**Training Time**: ~2-4 hours (500 episodes × 5 tasks)  
**Expected Results**: Better performance on hard tasks

### Fast Learning Profile (Aggressive)
**Purpose**: Quick convergence testing  
**Use Case**: Check if network can learn at all

```csharp
embeddingDim: 128
numHeads: 4
numEncoderLayers: 2
episodesPerTask: 200
maxCyclesPerEpisode: 50
learningRate: 5e-3  // 50x higher!
```

**Training Time**: ~30-60 minutes  
**Expected Results**: Fast initial learning, possible instability

## Parameter Scaling Guidelines

### When to Increase Network Size
- Motor skills success rate < 30% on all tasks
- Loss decreases but plateaus early
- Network appears to be underfitting

**Increase**: `embeddingDim`, `numEncoderLayers`, `numHeads`

### When to Increase Learning Rate
- Loss decreases very slowly
- No learning after 100 episodes
- Gradient norms are very small

**Try**: 1e-3 (10x), 5e-3 (50x), 1e-2 (100x)

### When to Decrease Learning Rate
- Loss oscillates or increases
- Training becomes unstable
- Gradient explosion

**Try**: 5e-5 (0.5x), 1e-5 (0.1x)

### When to Increase Episodes
- Learning curve still improving at episode 200
- Success rate increasing but not plateaued
- Want better final performance

**Try**: 500, 1000, 2000 episodes

### When to Increase Max Cycles
- Tasks often timeout (hit maxCycles)
- Network finds solution but needs more time
- Success rate limited by episode length

**Try**: 100, 200 cycles (but slower training)

## Memory Footprint Estimates

### Small Network (64 dim, 1 layer)
- Parameters: ~150K
- Memory: ~5 MB
- Training: ~10 MB

### Motor Skills Network (128 dim, 2 layers)
- Parameters: ~600K
- Memory: ~20 MB
- Training: ~40 MB

### Large Network (256 dim, 4 layers)
- Parameters: ~2.5M
- Memory: ~80 MB
- Training: ~160 MB

*Note: Actual memory usage includes episode buffers, optimizer states (Adam doubles memory), and TorchSharp overhead.*

## Current Best Practices

Based on EndToEndTrainingTests and successful integration tests:

1. **Start Small**: Use Motor Skills profile (128 dim, 2 layers) first
2. **Conservative LR**: 1e-4 is safe, increase only if no learning
3. **Adequate Episodes**: 200 is reasonable, can extend if learning continues
4. **Short Episodes**: 50 cycles forces efficient solutions
5. **Baseline First**: Run without body discovery pre-training
6. **Monitor Early**: Check success rate at episodes 20, 50, 100, 150, 200

## References

- TransformerCpuController: `src/NeuralCpu.Network/Neural/TransformerCpuController.cs`
- PolicyGradientTrainer: `src/NeuralCpu.Network/Training/PolicyGradientTrainer.cs`
- MotorSkillsTrainingProgram: `src/NeuralCpu.Console/Programs/MotorSkillsTrainingProgram.cs`
- Integration Test: `tests/NeuralCpu.Network.Tests/Integration/MotorSkillsTrainingIntegrationTest.cs`

---

**Last Updated**: 2026-06-27  
**Current Configuration**: Motor Skills Profile (128 dim, 2 layers, 1e-4 LR)
