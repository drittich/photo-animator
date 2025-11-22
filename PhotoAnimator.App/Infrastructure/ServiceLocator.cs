using System;
using System.Collections.Generic;
using System.Threading;

namespace PhotoAnimator.App.Infrastructure;

/// <summary>
/// Minimal service locator used as a lightweight DI substitute for this sample-sized app.
/// Supports singleton registrations with lazy construction and thread-safe retrieval.
/// </summary>
public sealed class ServiceLocator
{
    private readonly Dictionary<Type, Lazy<object>> _singletons = new();
    private readonly object _sync = new();

    /// <summary>
    /// Registers a singleton factory for <typeparamref name="TService"/>.
    /// Subsequent calls for the same service type are rejected to avoid accidental overrides.
    /// </summary>
    public void RegisterSingleton<TService>(Func<ServiceLocator, TService> factory)
    {
        if (factory is null) throw new ArgumentNullException(nameof(factory));

        var lazy = new Lazy<object>(() => factory(this)!, LazyThreadSafetyMode.ExecutionAndPublication);

        lock (_sync)
        {
            if (_singletons.ContainsKey(typeof(TService)))
                throw new InvalidOperationException($"Service {typeof(TService).Name} is already registered.");

            _singletons[typeof(TService)] = lazy;
        }
    }

    /// <summary>
    /// Attempts to resolve a service, returning false when the service was not registered.
    /// </summary>
    public bool TryGet<TService>(out TService service)
    {
        lock (_sync)
        {
            if (_singletons.TryGetValue(typeof(TService), out var lazy) && lazy.Value is TService resolved)
            {
                service = resolved;
                return true;
            }
        }

        service = default!;
        return false;
    }

    /// <summary>
    /// Resolves a registered service or throws when missing.
    /// </summary>
    public TService GetRequired<TService>()
    {
        if (TryGet<TService>(out var service))
        {
            return service;
        }

        throw new InvalidOperationException($"Required service {typeof(TService).Name} is not registered.");
    }
}
