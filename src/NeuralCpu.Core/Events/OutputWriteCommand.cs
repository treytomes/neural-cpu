namespace NeuralCpu.Core.Events;

/// <summary>
/// Event commanding to write a value to an output device.
/// </summary>
public sealed record OutputWriteCommand(
    byte DeviceId,
    ulong Value,
    DateTimeOffset Timestamp);
