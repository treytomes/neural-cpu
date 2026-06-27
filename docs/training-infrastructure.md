# Training Infrastructure

Complete guide to metrics logging, checkpointing, and visualization for Neural CPU training.

## Overview

The training infrastructure provides:
1. **Metrics Logging**: CSV/JSON output for all training metrics
2. **Checkpointing**: Save/resume training with full state
3. **Visualization**: Python scripts to plot learning curves
4. **Resumability**: Continue training from any checkpoint

## Components

### 1. TrainingMetricsLogger

**Purpose**: Logs episode-level metrics for analysis and plotting

**Features**:
- Real-time CSV logging (immediate flush after each episode)
- JSON export for post-processing
- Per-task summary statistics
- Timestamped entries for temporal analysis

**Output Files**:
```
training_metrics/
  ├── metrics_<runId>.csv       # Episode-level data (for plotting)
  ├── metrics_<runId>.json      # Same data in JSON format
  ├── summary_<runId>_RegisterActivation.json
  ├── summary_<runId>_StackGrowth.json
  └── ... (one summary per task)
```

**CSV Format**:
```csv
RunId,TaskName,Episode,Success,Reward,CycleCount,PolicyLoss,Timestamp
20260627_120000,RegisterActivation,0,False,0.0000,50,0.125678,2026-06-27T12:00:01Z
20260627_120000,RegisterActivation,1,True,1.0000,15,0.098234,2026-06-27T12:00:03Z
...
```

**Usage**:
```csharp
using var logger = new TrainingMetricsLogger("training_metrics");

// Log each episode
logger.LogEpisode("RegisterActivation", episode: 0, success: true, 
                  reward: 1.0, cycleCount: 15, policyLoss: 0.098);

// Log task summary
logger.LogTaskSummary("RegisterActivation", totalEpisodes: 200, 
                      successCount: 160, avgCyclesToSuccess: 12.3);

// JSON export happens automatically on Dispose()
```

### 2. TrainingCheckpoint

**Purpose**: Save/resume training with complete state

**Features**:
- Model weights (TorchSharp .pt format)
- Optimizer state (Adam momentum, variance)
- Training state (episode counters, task progress)
- Metadata (timestamps, configuration)
- "Latest" checkpoint linking

**Checkpoint Structure**:
```
checkpoints/
  ├── checkpoint_<runId>_<task>_ep<episode>/
  │   ├── model.pt              # Model weights
  │   ├── optimizer.pt.json     # Optimizer hyperparameters
  │   ├── state.json            # Training state
  │   └── metadata.json         # Checkpoint metadata
  ├── latest_<runId>/           # Copy of most recent checkpoint
  └── ...
```

**Training State** (state.json):
```json
{
  "RunId": "20260627_120000",
  "NetworkConfig": {
    "EmbeddingDim": 128,
    "NumHeads": 4,
    "NumEncoderLayers": 2,
    "Dropout": 0.1
  },
  "TrainingConfig": {
    "LearningRate": 0.0001,
    "Gamma": 0.99,
    "EpisodesPerTask": 200,
    "MaxCyclesPerEpisode": 50
  },
  "CurrentTask": "StackGrowth",
  "CurrentTaskEpisode": 100,
  "TotalEpisodesCompleted": 300,
  "TaskProgress": { ... },
  "StartTime": "2026-06-27T12:00:00Z",
  "LastCheckpointTime": "2026-06-27T12:15:00Z"
}
```

**Usage**:
```csharp
var checkpoint = new TrainingCheckpoint("checkpoints", runId: "20260627_120000");

// Save checkpoint
checkpoint.Save(controller, trainer, state, "RegisterActivation", episode: 50);

// Resume training
var latestPath = checkpoint.FindLatestCheckpoint();
var (controller, state) = checkpoint.Load(latestPath, trainer);

// Continue from state.CurrentTaskEpisode
```

### 3. MotorSkillsTrainerWithCheckpoints

**Purpose**: Enhanced trainer with automatic metrics + checkpointing

**Features**:
- Automatic metrics logging (every episode)
- Periodic checkpointing (configurable interval)
- Resume from checkpoint support
- Progress reporting

**Configuration**:
```csharp
var trainer = new MotorSkillsTrainerWithCheckpoints(
    eventBus, clock, registerFile, alu, stack, memory,
    inputDevice, outputDevice, deviceRegistry,
    stateVectorBuilder, controller, policyGradientTrainer,
    loggerFactory,
    metricsDirectory: "training_metrics",
    checkpointDirectory: "checkpoints",
    checkpointInterval: 50,  // Checkpoint every 50 episodes
    runId: null);            // Auto-generate timestamp-based ID

// Train with automatic logging/checkpointing
var results = trainer.TrainAllTasks(
    episodesPerTask: 200,
    maxCyclesPerEpisode: 50,
    resumeState: null);      // Or pass state to resume
```

**Output During Training**:
```
Starting Basic Motor Skills Training with Checkpointing
Configuration: 200 episodes per task, 50 max cycles, checkpoint every 50 episodes
=== Task 1/5: RegisterActivation ===
RegisterActivation Episode 0/200: Success=False, Reward=0.00, Cycles=50, Loss=0.125678
RegisterActivation Episode 20/200: Success=True, Reward=1.00, Cycles=15, Loss=0.098234
...
Saving checkpoint: RegisterActivation episode 50
...
RegisterActivation Summary: 80.0% success rate (160/200), 12.3 avg cycles to success
```

## Visualization

### plot_training_curves.py

**Purpose**: Generate learning curve plots from CSV metrics

**Requirements**:
```bash
pip install pandas matplotlib seaborn
```

**Usage**:
```bash
python scripts/plot_training_curves.py training_metrics/metrics_20260627_120000.csv
```

**Generated Plots**:

1. **success_rate.png**: Rolling average success rate per task
   - Shows learning progression over episodes
   - 20-episode rolling window for smoothing

2. **policy_loss.png**: Policy loss over time (log scale)
   - Lower is better (network improving)
   - Should trend downward

3. **cycles_to_success.png**: Efficiency improvement
   - Only successful episodes
   - Should decrease as network learns optimal strategies

4. **dashboard.png**: 2×2 grid with all key metrics
   - Success rate, policy loss, reward, cycles
   - Single-page overview

5. **task_comparison.png**: Final performance comparison
   - Bar charts comparing all tasks
   - Based on last 20 episodes

**Example Output**:
```
Loading metrics from: training_metrics/metrics_20260627_120000.csv
Run ID: 20260627_120000
Total episodes: 1000
Tasks: ['RegisterActivation', 'StackGrowth', 'ALUActivation', 'InputRead', 'OutputWrite']
✓ success_rate.png
✓ policy_loss.png
✓ cycles_to_success.png
✓ dashboard.png
✓ task_comparison.png

============================================================
TRAINING SUMMARY STATISTICS
============================================================

RegisterActivation:
  Total Episodes: 200
  Overall Success Rate: 78.5%
  Final Success Rate (last 20): 95.0%
  First Success: Episode 8
  Avg Cycles to Success: 11.2
  Min Cycles: 5
  Final Avg Policy Loss: 0.045678

...
```

## Resuming Training

### From Latest Checkpoint

```csharp
var checkpoint = new TrainingCheckpoint("checkpoints", runId: "20260627_120000");
var latestPath = checkpoint.FindLatestCheckpoint();

if (latestPath != null)
{
    var (controller, state) = checkpoint.Load(latestPath, trainer);

    // Continue training from saved state
    var results = trainer.TrainAllTasks(
        episodesPerTask: 200,
        maxCyclesPerEpisode: 50,
        resumeState: state);  // Resume from checkpoint
}
```

### From Specific Checkpoint

```csharp
var checkpoints = checkpoint.ListCheckpoints();

// Find checkpoint at episode 100
var target = checkpoints.FirstOrDefault(c => 
    c.TaskName == "StackGrowth" && c.Episode == 100);

if (target != null)
{
    var (controller, state) = checkpoint.Load(target.Path, trainer);
    // Resume training...
}
```

## Checkpoint Interval Strategy

### Conservative (Every 20 episodes)
**Pro**: Minimal data loss if training crashes  
**Con**: More disk I/O, larger storage

```csharp
checkpointInterval: 20
```

### Balanced (Every 50 episodes) - **Recommended**
**Pro**: Good trade-off between safety and performance  
**Con**: Could lose up to 50 episodes on crash

```csharp
checkpointInterval: 50
```

### Aggressive (Every 100 episodes)
**Pro**: Minimal overhead  
**Con**: More data loss risk

```csharp
checkpointInterval: 100
```

### Per-Task Only (0 = disabled during task)
**Pro**: Fastest training  
**Con**: Only checkpoints at task boundaries

```csharp
checkpointInterval: 0  // Only checkpoint after each task completes
```

## Storage Requirements

### Metrics
- **CSV**: ~100 bytes/episode × 1000 episodes = ~100 KB
- **JSON**: ~150 bytes/episode × 1000 episodes = ~150 KB
- **Total**: <1 MB per training run

### Checkpoints
- **Model**: ~2.5 MB (128 dim, 2 layers)
- **Optimizer state**: ~100 KB (metadata only, full state not yet supported)
- **Training state**: ~10 KB
- **Per checkpoint**: ~3 MB

**Example**: 200 episodes/task × 5 tasks, checkpoint every 50 episodes:
- Checkpoints: 20 total × 3 MB = **60 MB**
- Metrics: **<1 MB**
- **Total**: ~61 MB per training run

## Integration with Motor Skills Training

### Standard Training (No checkpoints)
```csharp
var trainer = new MotorSkillsTrainer(...);
var results = trainer.TrainAllTasks(episodesPerTask: 200);
```

### With Metrics + Checkpoints
```csharp
var trainer = new MotorSkillsTrainerWithCheckpoints(
    ...,
    metricsDirectory: "training_metrics",
    checkpointDirectory: "checkpoints",
    checkpointInterval: 50);

var results = trainer.TrainAllTasks(episodesPerTask: 200);

// Metrics automatically saved to:
//   training_metrics/metrics_<runId>.csv

// Checkpoints automatically saved every 50 episodes
```

### Resume from Crash
```csharp
var checkpoint = new TrainingCheckpoint("checkpoints");
var latest = checkpoint.FindLatestCheckpoint();

if (latest != null)
{
    var (controller, state) = checkpoint.Load(latest, trainer);
    var results = trainer.TrainAllTasks(resumeState: state);
}
else
{
    // Start fresh training
    var results = trainer.TrainAllTasks();
}
```

## Best Practices

1. **Always use metrics logging**: Even without checkpoints, metrics are essential for analysis
2. **Checkpoint every 50 episodes**: Good default for 200-episode training
3. **Check for existing checkpoints**: Always check `FindLatestCheckpoint()` before starting
4. **Plot curves frequently**: Run plotting script after each training run
5. **Archive successful runs**: Keep checkpoints + metrics from successful training
6. **Clean up failed runs**: Delete checkpoints from crashed/abandoned runs

## Troubleshooting

### "No metrics file found"
- Check `metricsDirectory` path is correct
- Ensure training completed at least 1 episode
- CSV is written real-time, should exist immediately

### "Checkpoint not found"
- Check `checkpointDirectory` path
- Ensure training ran long enough to hit checkpoint interval
- Use `ListCheckpoints()` to see what exists

### "Cannot resume training"
- Network architecture must match checkpoint
- Check `NetworkConfig` in state.json
- May need to recreate controller with exact same parameters

### "Plots look wrong"
- Check CSV has valid data (open in Excel/text editor)
- Ensure pandas/matplotlib installed (`pip install -r requirements.txt`)
- Check for NaN or inf values in metrics

---

**Last Updated**: 2026-06-27  
**Status**: Implemented, ready for use  
**Next**: Run training and analyze results!
