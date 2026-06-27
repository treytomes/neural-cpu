namespace NeuralCpu.Core.Components;

/// <summary>
/// Primary task output device (device ID 0).
/// Buffers output values in order for reward computation.
/// </summary>
public sealed class TaskOutputDevice : IOutputDevice
{
    #region Fields

    private readonly List<ulong> _buffer = new();

    #endregion

    #region Properties

    public byte DeviceId => 0;
    public string DeviceName => "TaskOutput";

    #endregion

    #region Methods

    /// <summary>
    /// Writes a value to the output buffer.
    /// </summary>
    public void Write(ulong value)
    {
        _buffer.Add(value);
    }

    /// <summary>
    /// Gets the current output buffer contents.
    /// </summary>
    public IReadOnlyList<ulong> GetBuffer()
    {
        return _buffer.AsReadOnly();
    }

    /// <summary>
    /// Clears the output buffer.
    /// </summary>
    public void Clear()
    {
        _buffer.Clear();
    }

    #endregion
}
