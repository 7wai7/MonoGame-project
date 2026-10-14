using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MonoGame_project.Ecs;

/// <summary>
/// Owns entities, components and world-scoped resources for one scene.
/// </summary>
public sealed class World : IDisposable
{
    private readonly List<int> _generations = [];
    private readonly List<bool> _alive = [];
    private readonly Stack<int> _freeIds = [];
    private readonly Dictionary<Type, IComponentPool> _componentPools = [];
    private readonly Dictionary<Type, object> _resources = [];
    private bool _disposed;

    internal int StructureVersion { get; private set; }

    public int EntityCount { get; private set; }
    public EntityEnumerable Entities => new(this);

    public Entity CreateEntity()
    {
        ThrowIfDisposed();

        int id;
        if (_freeIds.TryPop(out var recycledId))
        {
            id = recycledId;
            _alive[id] = true;
        }
        else
        {
            id = _generations.Count;
            _generations.Add(1);
            _alive.Add(true);
        }

        EntityCount++;
        StructureVersion++;
        return new Entity(id, _generations[id]);
    }

    public bool IsAlive(Entity entity) =>
        entity.Id >= 0 &&
        entity.Id < _alive.Count &&
        _alive[entity.Id] &&
        _generations[entity.Id] == entity.Generation;

    public void DestroyEntity(Entity entity)
    {
        EnsureAlive(entity);

        foreach (var pool in _componentPools.Values)
        {
            pool.Remove(entity);
        }

        _alive[entity.Id] = false;
        _generations[entity.Id] = NextGeneration(_generations[entity.Id]);
        _freeIds.Push(entity.Id);
        EntityCount--;
        StructureVersion++;
    }

    public ref T Add<T>(Entity entity) where T : struct, IComponent
    {
        return ref Add(entity, default(T));
    }

    public ref T Add<T>(Entity entity, T component) where T : struct, IComponent
    {
        EnsureAlive(entity);
        ref var added = ref GetOrCreatePool<T>().Add(entity, component);
        StructureVersion++;
        return ref added;
    }

    /// <summary>Adds a component or replaces its value when it already exists.</summary>
    public ref T Set<T>(Entity entity, T component) where T : struct, IComponent
    {
        EnsureAlive(entity);
        ref var result = ref GetOrCreatePool<T>().Set(entity, component, out var wasAdded);
        if (wasAdded)
        {
            StructureVersion++;
        }

        return ref result;
    }

    public bool Has<T>(Entity entity) where T : struct, IComponent =>
        IsAlive(entity) &&
        TryGetPool<T>(out var pool) &&
        pool.Has(entity);

    public ref T Get<T>(Entity entity) where T : struct, IComponent
    {
        EnsureAlive(entity);
        if (!TryGetPool<T>(out var pool))
        {
            throw new KeyNotFoundException($"No {typeof(T).Name} components exist in this world.");
        }

        return ref pool.Get(entity);
    }

    public bool TryGet<T>(Entity entity, out T component) where T : struct, IComponent
    {
        if (IsAlive(entity) && TryGetPool<T>(out var pool))
        {
            return pool.TryGet(entity, out component);
        }

        component = default;
        return false;
    }

    public bool Remove<T>(Entity entity) where T : struct, IComponent
    {
        EnsureAlive(entity);
        if (!TryGetPool<T>(out var pool) || !pool.Remove(entity))
        {
            return false;
        }

        StructureVersion++;
        return true;
    }

    public EntityQuery<T1> Query<T1>()
        where T1 : struct, IComponent =>
        new(this, GetOrCreatePool<T1>());

    public EntityQuery<T1, T2> Query<T1, T2>()
        where T1 : struct, IComponent
        where T2 : struct, IComponent =>
        new(this, GetOrCreatePool<T1>(), GetOrCreatePool<T2>());

    public EntityQuery<T1, T2, T3> Query<T1, T2, T3>()
        where T1 : struct, IComponent
        where T2 : struct, IComponent
        where T3 : struct, IComponent =>
        new(this, GetOrCreatePool<T1>(), GetOrCreatePool<T2>(), GetOrCreatePool<T3>());

    public EntityQuery<T1, T2, T3, T4> Query<T1, T2, T3, T4>()
        where T1 : struct, IComponent
        where T2 : struct, IComponent
        where T3 : struct, IComponent
        where T4 : struct, IComponent =>
        new(
            this,
            GetOrCreatePool<T1>(),
            GetOrCreatePool<T2>(),
            GetOrCreatePool<T3>(),
            GetOrCreatePool<T4>());

    public void SetResource<T>(T resource) where T : notnull
    {
        ThrowIfDisposed();
        _resources[typeof(T)] = resource;
    }

    public bool HasResource<T>() where T : notnull => _resources.ContainsKey(typeof(T));

    public T GetResource<T>() where T : notnull
    {
        ThrowIfDisposed();
        if (_resources.TryGetValue(typeof(T), out var resource))
        {
            return (T)resource;
        }

        throw new KeyNotFoundException($"Resource {typeof(T).Name} is not registered.");
    }

    public bool TryGetResource<T>([MaybeNullWhen(false)] out T resource) where T : notnull
    {
        ThrowIfDisposed();
        if (_resources.TryGetValue(typeof(T), out var value))
        {
            resource = (T)value;
            return true;
        }

        resource = default;
        return false;
    }

    public bool RemoveResource<T>() where T : notnull
    {
        ThrowIfDisposed();
        return _resources.Remove(typeof(T));
    }

    public EntityCommandBuffer CreateCommandBuffer() => new();

    /// <summary>Destroys every entity while keeping registered resources.</summary>
    public void ClearEntities()
    {
        ThrowIfDisposed();

        foreach (var pool in _componentPools.Values)
        {
            pool.Clear();
        }

        _freeIds.Clear();
        for (var id = 0; id < _alive.Count; id++)
        {
            if (_alive[id])
            {
                _generations[id] = NextGeneration(_generations[id]);
            }

            _alive[id] = false;
            _freeIds.Push(id);
        }

        EntityCount = 0;
        StructureVersion++;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var resource in _resources.Values)
        {
            if (resource is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        _resources.Clear();
        _componentPools.Clear();
        _alive.Clear();
        _generations.Clear();
        _freeIds.Clear();
        EntityCount = 0;
        StructureVersion++;
        _disposed = true;
    }

    internal Entity GetEntityById(int id) => new(id, _generations[id]);
    internal bool IsAliveId(int id) => (uint)id < (uint)_alive.Count && _alive[id];
    internal int EntityCapacity => _alive.Count;

    private ComponentPool<T> GetOrCreatePool<T>() where T : struct, IComponent
    {
        ThrowIfDisposed();
        if (_componentPools.TryGetValue(typeof(T), out var existing))
        {
            return (ComponentPool<T>)existing;
        }

        var pool = new ComponentPool<T>();
        _componentPools.Add(typeof(T), pool);
        return pool;
    }

    private bool TryGetPool<T>(out ComponentPool<T> pool) where T : struct, IComponent
    {
        if (_componentPools.TryGetValue(typeof(T), out var existing))
        {
            pool = (ComponentPool<T>)existing;
            return true;
        }

        pool = null!;
        return false;
    }

    private void EnsureAlive(Entity entity)
    {
        ThrowIfDisposed();
        if (!IsAlive(entity))
        {
            throw new InvalidOperationException($"{entity} is not alive in this world.");
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static int NextGeneration(int generation) =>
        generation == int.MaxValue ? 1 : generation + 1;
}
