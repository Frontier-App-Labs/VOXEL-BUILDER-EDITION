using System;
using System.Collections.Generic;

namespace VoxelSiege.Utility;

public sealed class ObjectPool<T>
    where T : class
{
    private readonly Stack<T> _available;
    private readonly Func<T> _factory;
    private readonly Action<T>? _onGet;
    private readonly Action<T>? _onReturn;

    public ObjectPool(Func<T> factory, int initialCapacity = 0, Action<T>? onGet = null, Action<T>? onReturn = null)
    {
        _factory = factory;
        _available = new Stack<T>(initialCapacity);
        _onGet = onGet;
        _onReturn = onReturn;

        for (int index = 0; index < initialCapacity; index++)
        {
            _available.Push(_factory());
        }
    }

    public T Get()
    {
        T item = _available.Count > 0 ? _available.Pop() : _factory();
        _onGet?.Invoke(item);
        return item;
    }

    public void Return(T item)
    {
        _onReturn?.Invoke(item);
        _available.Push(item);
    }

    public void Clear()
    {
        _available.Clear();
    }
}
