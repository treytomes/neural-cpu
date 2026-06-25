namespace NeuralCpu.Core.Events;

/// <summary>
/// Global event bus for component communication using the observer pattern.
/// Thread-safe message broker for CPU component coordination.
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Subscribe to events of type T.
    /// </summary>
    /// <typeparam name="T">Event type to observe</typeparam>
    /// <param name="observer">Observer to receive notifications</param>
    /// <returns>Disposable subscription handle</returns>
    IDisposable Subscribe<T>(IObserver<T> observer) where T : class;

    /// <summary>
    /// Publish an event to all subscribers.
    /// </summary>
    /// <typeparam name="T">Event type</typeparam>
    /// <param name="event">Event instance to publish</param>
    void Publish<T>(T @event) where T : class;

    /// <summary>
    /// Publish an event asynchronously to all subscribers.
    /// </summary>
    /// <typeparam name="T">Event type</typeparam>
    /// <param name="event">Event instance to publish</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default) where T : class;
}
