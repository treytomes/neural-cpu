namespace NeuralCpu.Core.Components;

/// <summary>
/// Interface for output devices on the event bus.
/// Devices are addressed by ID (0-15), allowing multiple output destinations.
/// Examples: TaskOutput (device 0), Display (device 1), Actuator (device 2).
/// </summary>
public interface IOutputDevice
{
    /// <summary>
    /// Unique device ID (0-15).
    /// </summary>
    byte DeviceId { get; }

    /// <summary>
    /// Human-readable device name for debugging and visualization.
    /// </summary>
    string DeviceName { get; }

    /// <summary>
    /// Writes a value to the device.
    /// </summary>
    void Write(ulong value);

    /// <summary>
    /// Gets the current output buffer contents (for validation and debugging).
    /// </summary>
    IReadOnlyList<ulong> GetBuffer();
}
