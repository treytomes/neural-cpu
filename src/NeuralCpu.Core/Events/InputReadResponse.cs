namespace NeuralCpu.Core.Events;

/// <summary>
/// Event published after reading from an input device.
/// </summary>
public sealed record InputReadResponse(
    byte DeviceId,
    ulong Value,
    bool HasData,
    DateTimeOffset Timestamp);
