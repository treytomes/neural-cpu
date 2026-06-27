namespace NeuralCpu.Core.Events;

/// <summary>
/// Event requesting to read from an input device.
/// </summary>
public sealed record InputReadRequest(
    byte DeviceId,
    DateTimeOffset Timestamp);
