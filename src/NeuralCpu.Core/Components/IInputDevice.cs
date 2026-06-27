namespace NeuralCpu.Core.Components;

/// <summary>
/// Interface for input devices on the event bus.
/// Devices are addressed by ID (0-15), allowing multiple input sources.
/// Examples: TaskInput (device 0), Sensor (device 1), Keyboard (device 2).
/// </summary>
public interface IInputDevice
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
    /// Whether the device has data available to read.
    /// </summary>
    bool HasData { get; }

    /// <summary>
    /// Reads the next value from the device.
    /// Returns 0 if no data available.
    /// </summary>
    ulong Read();
}
