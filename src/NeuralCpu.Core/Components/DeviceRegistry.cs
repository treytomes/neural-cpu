using Microsoft.Extensions.Logging;
using NeuralCpu.Core.Events;

namespace NeuralCpu.Core.Components;

/// <summary>
/// Manages multiple input and output devices, routing events by device ID.
/// Supports up to 16 input devices and 16 output devices.
/// </summary>
public sealed class DeviceRegistry :
    IObserver<InputReadRequest>,
    IObserver<OutputWriteCommand>,
    IObserver<SystemReset>,
    IDisposable
{
    #region Fields

    private readonly IEventBus _eventBus;
    private readonly ILogger<DeviceRegistry> _logger;
    private readonly Dictionary<byte, IInputDevice> _inputDevices = new();
    private readonly Dictionary<byte, IOutputDevice> _outputDevices = new();
    private readonly HashSet<byte> _discoveredInputDevices = new();
    private readonly HashSet<byte> _discoveredOutputDevices = new();
    private readonly IDisposable _inputReadSubscription;
    private readonly IDisposable _outputWriteSubscription;
    private readonly IDisposable _resetSubscription;

    #endregion

    #region Constructors

    public DeviceRegistry(
        IEventBus eventBus,
        ILogger<DeviceRegistry> logger)
    {
        _eventBus = eventBus;
        _logger = logger;

        _inputReadSubscription = _eventBus.Subscribe<InputReadRequest>(this);
        _outputWriteSubscription = _eventBus.Subscribe<OutputWriteCommand>(this);
        _resetSubscription = _eventBus.Subscribe<SystemReset>(this);
    }

    #endregion

    #region Device Registration

    /// <summary>
    /// Registers an input device. Throws if device ID already registered.
    /// </summary>
    public void RegisterInputDevice(IInputDevice device)
    {
        if (device.DeviceId > 15)
            throw new ArgumentException($"DeviceId must be 0-15, got {device.DeviceId}", nameof(device));

        if (_inputDevices.ContainsKey(device.DeviceId))
            throw new InvalidOperationException($"Input device {device.DeviceId} already registered");

        _inputDevices[device.DeviceId] = device;
        _logger.LogInformation("Registered input device {DeviceId}: {DeviceName}", device.DeviceId, device.DeviceName);
    }

    /// <summary>
    /// Registers an output device. Throws if device ID already registered.
    /// </summary>
    public void RegisterOutputDevice(IOutputDevice device)
    {
        if (device.DeviceId > 15)
            throw new ArgumentException($"DeviceId must be 0-15, got {device.DeviceId}", nameof(device));

        if (_outputDevices.ContainsKey(device.DeviceId))
            throw new InvalidOperationException($"Output device {device.DeviceId} already registered");

        _outputDevices[device.DeviceId] = device;
        _logger.LogInformation("Registered output device {DeviceId}: {DeviceName}", device.DeviceId, device.DeviceName);
    }

    #endregion

    #region Event Handlers

    void IObserver<InputReadRequest>.OnNext(InputReadRequest request)
    {
        // Check if device exists
        if (!_inputDevices.TryGetValue(request.DeviceId, out var device))
        {
            _logger.LogWarning("InputReadRequest for unknown device {DeviceId}", request.DeviceId);
            _eventBus.Publish(new InputReadResponse(request.DeviceId, 0, false, DateTimeOffset.Now));
            return;
        }

        // Publish discovery event if first access
        if (_discoveredInputDevices.Add(request.DeviceId))
        {
            _eventBus.Publish(new DeviceDiscoveredEvent(
                request.DeviceId,
                "Input",
                device.DeviceName,
                DateTimeOffset.Now));
            _logger.LogInformation("Device discovered: Input[{DeviceId}] = {DeviceName}", request.DeviceId, device.DeviceName);
        }

        // Read from device
        var value = device.Read();
        var hasData = device.HasData;

        // Publish response
        _eventBus.Publish(new InputReadResponse(request.DeviceId, value, hasData, DateTimeOffset.Now));
    }

    void IObserver<OutputWriteCommand>.OnNext(OutputWriteCommand command)
    {
        // Check if device exists
        if (!_outputDevices.TryGetValue(command.DeviceId, out var device))
        {
            _logger.LogWarning("OutputWriteCommand for unknown device {DeviceId}", command.DeviceId);
            return;
        }

        // Publish discovery event if first access
        if (_discoveredOutputDevices.Add(command.DeviceId))
        {
            _eventBus.Publish(new DeviceDiscoveredEvent(
                command.DeviceId,
                "Output",
                device.DeviceName,
                DateTimeOffset.Now));
            _logger.LogInformation("Device discovered: Output[{DeviceId}] = {DeviceName}", command.DeviceId, device.DeviceName);
        }

        // Write to device
        device.Write(command.Value);

        // Publish acknowledgment
        _eventBus.Publish(new OutputWriteAcknowledged(command.DeviceId, DateTimeOffset.Now));
    }

    void IObserver<SystemReset>.OnNext(SystemReset reset)
    {
        _logger.LogInformation("System reset - clearing device discovery tracking");
        _discoveredInputDevices.Clear();
        _discoveredOutputDevices.Clear();
    }

    void IObserver<InputReadRequest>.OnCompleted() { }
    void IObserver<InputReadRequest>.OnError(Exception error) { }
    void IObserver<OutputWriteCommand>.OnCompleted() { }
    void IObserver<OutputWriteCommand>.OnError(Exception error) { }
    void IObserver<SystemReset>.OnCompleted() { }
    void IObserver<SystemReset>.OnError(Exception error) { }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        _inputReadSubscription.Dispose();
        _outputWriteSubscription.Dispose();
        _resetSubscription.Dispose();
    }

    #endregion
}
