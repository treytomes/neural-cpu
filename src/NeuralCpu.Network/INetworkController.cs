using NeuralCpu.Core.Events;

namespace NeuralCpu.Network;

/// <summary>
/// Interface for neural network controllers (both mock and real).
/// The controller observes CPU state and decides what event to emit next.
/// </summary>
public interface INetworkController
{
    /// <summary>
    /// Given the current CPU state, decide what control signal (event) to emit.
    /// </summary>
    /// <param name="state">Complete snapshot of CPU state</param>
    /// <returns>Event to publish to the CPU event bus (any CPU event record type)</returns>
    object DecideNextAction(CpuStateVector state);

    /// <summary>
    /// Notify the controller that the episode/task has ended.
    /// Used for training to compute rewards and update weights.
    /// </summary>
    /// <param name="success">Whether the task completed successfully</param>
    /// <param name="reward">Reward signal (task-specific)</param>
    void OnEpisodeEnd(bool success, double reward);

    /// <summary>
    /// Reset controller state for a new episode.
    /// </summary>
    void Reset();
}
