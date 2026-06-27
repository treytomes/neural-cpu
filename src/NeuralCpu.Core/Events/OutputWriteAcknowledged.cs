namespace NeuralCpu.Core.Events;

/// <summary>
/// Event published after successfully writing to an output device.
/// </summary>
public sealed record OutputWriteAcknowledged(
    byte DeviceId,
    DateTimeOffset Timestamp);
