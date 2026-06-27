namespace NeuralCpu.Core.Components;

/// <summary>
/// Primary task input device (device ID 0).
/// Holds input buffer loaded at episode start and provides sequential FIFO reads.
/// </summary>
public sealed class TaskInputDevice : IInputDevice
{
    #region Fields

    private readonly Queue<ulong> _buffer = new();

    #endregion

    #region Properties

    public byte DeviceId => 0;
    public string DeviceName => "TaskInput";
    public bool HasData => _buffer.Count > 0;

    #endregion

    #region Methods

    /// <summary>
    /// Loads input data at the start of an episode.
    /// Clears any existing data.
    /// </summary>
    public void LoadInput(IEnumerable<ulong> input)
    {
        _buffer.Clear();
        foreach (var value in input)
            _buffer.Enqueue(value);
    }

    /// <summary>
    /// Reads the next value from the input buffer.
    /// Returns 0 if no data available.
    /// </summary>
    public ulong Read()
    {
        return _buffer.TryDequeue(out var value) ? value : 0;
    }

    /// <summary>
    /// Clears the input buffer.
    /// </summary>
    public void Clear()
    {
        _buffer.Clear();
    }

    #endregion
}
