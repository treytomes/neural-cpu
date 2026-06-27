namespace NeuralCpu.Core.Events;

/// <summary>
/// Event published when a device is first accessed by the network.
/// Useful for tracking exploration progress during embodied learning.
/// </summary>
public sealed record DeviceDiscoveredEvent(
    byte DeviceId,
    string DeviceType,  // "Input" or "Output"
    string DeviceName,
    DateTimeOffset Timestamp);
