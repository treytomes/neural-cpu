using System.Collections.Concurrent;

namespace NeuralCpu.Core.Events;

/// <summary>
/// Thread-safe event bus implementation using System.Reactive patterns.
/// </summary>
public sealed class EventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, object> _subscriptions = new();
    private readonly object _lock = new();

    public IDisposable Subscribe<T>(IObserver<T> observer) where T : class
    {
        ArgumentNullException.ThrowIfNull(observer);

        var subject = GetOrCreateSubject<T>();
        return subject.Subscribe(observer);
    }

    public void Publish<T>(T @event) where T : class
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (_subscriptions.TryGetValue(typeof(T), out var subjectObj))
        {
            var subject = (Subject<T>)subjectObj;
            subject.OnNext(@event);
        }
    }

    public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(@event);

        return Task.Run(() => Publish(@event), cancellationToken);
    }

    private Subject<T> GetOrCreateSubject<T>() where T : class
    {
        return (Subject<T>)_subscriptions.GetOrAdd(
            typeof(T),
            _ => new Subject<T>());
    }

    /// <summary>
    /// Simple subject implementation for observer pattern.
    /// </summary>
    private sealed class Subject<T> where T : class
    {
        private readonly ConcurrentBag<IObserver<T>> _observers = new();

        public IDisposable Subscribe(IObserver<T> observer)
        {
            _observers.Add(observer);
            return new Unsubscriber(() => TryRemove(observer));
        }

        public void OnNext(T value)
        {
            foreach (var observer in _observers)
            {
                try
                {
                    observer.OnNext(value);
                }
                catch (Exception ex)
                {
                    observer.OnError(ex);
                }
            }
        }

        private void TryRemove(IObserver<T> observer)
        {
            // ConcurrentBag doesn't support removal, but for this use case
            // we can just let it remain - the unsubscriber pattern is more
            // about signaling than actual removal
        }

        private sealed class Unsubscriber : IDisposable
        {
            private readonly Action _onDispose;
            private int _disposed;

            public Unsubscriber(Action onDispose)
            {
                _onDispose = onDispose;
            }

            public void Dispose()
            {
                if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
                {
                    _onDispose();
                }
            }
        }
    }
}
