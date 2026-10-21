using System;
using System.Collections.Generic;

namespace MonoGame_project.Ecs;

internal interface IComponentPool
{
    int Count { get; }
    Type ComponentType { get; }
    Entity GetEntity(int denseIndex);
    bool Has(Entity entity);
    object GetBoxed(Entity entity);
    bool Remove(Entity entity);
    void Clear();
}

/// <summary>
/// Sparse-set storage: O(1) add/get/remove and cache-friendly dense iteration.
/// </summary>
internal sealed class ComponentPool<T> : IComponentPool where T : struct, IComponent
{
    private const int InitialCapacity = 16;

    private int[] _sparse = [];
    private Entity[] _entities = new Entity[InitialCapacity];
    private T[] _components = new T[InitialCapacity];

    public int Count { get; private set; }
    public Type ComponentType => typeof(T);

    public bool Has(Entity entity)
    {
        if ((uint)entity.Id >= (uint)_sparse.Length)
        {
            return false;
        }

        var denseIndex = _sparse[entity.Id] - 1;
        return (uint)denseIndex < (uint)Count && _entities[denseIndex] == entity;
    }

    public ref T Add(Entity entity, T component)
    {
        EnsureSparseCapacity(entity.Id);

        if (_sparse[entity.Id] != 0)
        {
            throw new InvalidOperationException($"{entity} already has component {typeof(T).Name}.");
        }

        EnsureDenseCapacity();
        var denseIndex = Count++;
        _sparse[entity.Id] = denseIndex + 1;
        _entities[denseIndex] = entity;
        _components[denseIndex] = component;
        return ref _components[denseIndex];
    }

    public ref T Set(Entity entity, T component, out bool wasAdded)
    {
        if (Has(entity))
        {
            wasAdded = false;
            var existingIndex = _sparse[entity.Id] - 1;
            _components[existingIndex] = component;
            return ref _components[existingIndex];
        }

        wasAdded = true;
        return ref Add(entity, component);
    }

    public ref T Get(Entity entity)
    {
        if (!Has(entity))
        {
            throw new KeyNotFoundException($"{entity} does not have component {typeof(T).Name}.");
        }

        return ref _components[_sparse[entity.Id] - 1];
    }

    public object GetBoxed(Entity entity) => Get(entity);

    public bool TryGet(Entity entity, out T component)
    {
        if (Has(entity))
        {
            component = _components[_sparse[entity.Id] - 1];
            return true;
        }

        component = default;
        return false;
    }

    public bool Remove(Entity entity)
    {
        if (!Has(entity))
        {
            return false;
        }

        var removedIndex = _sparse[entity.Id] - 1;
        var lastIndex = Count - 1;

        if (removedIndex != lastIndex)
        {
            var movedEntity = _entities[lastIndex];
            _entities[removedIndex] = movedEntity;
            _components[removedIndex] = _components[lastIndex];
            _sparse[movedEntity.Id] = removedIndex + 1;
        }

        _sparse[entity.Id] = 0;
        _entities[lastIndex] = default;
        _components[lastIndex] = default;
        Count--;
        return true;
    }

    public Entity GetEntity(int denseIndex)
    {
        if ((uint)denseIndex >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(denseIndex));
        }

        return _entities[denseIndex];
    }

    public void Clear()
    {
        Array.Clear(_sparse);
        Array.Clear(_entities, 0, Count);
        Array.Clear(_components, 0, Count);
        Count = 0;
    }

    private void EnsureSparseCapacity(int entityId)
    {
        if (entityId < _sparse.Length)
        {
            return;
        }

        var newLength = Math.Max(entityId + 1, Math.Max(InitialCapacity, _sparse.Length * 2));
        Array.Resize(ref _sparse, newLength);
    }

    private void EnsureDenseCapacity()
    {
        if (Count < _entities.Length)
        {
            return;
        }

        var newLength = _entities.Length * 2;
        Array.Resize(ref _entities, newLength);
        Array.Resize(ref _components, newLength);
    }
}
