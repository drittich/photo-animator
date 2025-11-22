using System;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoAnimator.App.Infrastructure;

public sealed class AsyncLazy<T>
{
    private readonly Func<CancellationToken, Task<T>> _factory;
    private readonly object _syncRoot = new();
    private Task<T>? _valueTask;

    public AsyncLazy(Func<CancellationToken, Task<T>> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public bool IsValueCreated => _valueTask is not null;

    public Task<T> GetValueAsync(CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            _valueTask ??= _factory(cancellationToken);
            return _valueTask;
        }
    }

    public Task<T>? GetIfCreated() => _valueTask;
}