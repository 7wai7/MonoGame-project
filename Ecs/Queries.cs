using System;

namespace MonoGame_project.Ecs;

public readonly struct EntityEnumerable
{
    private readonly World _world;

    internal EntityEnumerable(World world) => _world = world;

    public Enumerator GetEnumerator() => new(_world);

    public struct Enumerator
    {
        private readonly World _world;
        private readonly int _version;
        private int _id;

        internal Enumerator(World world)
        {
            _world = world;
            _version = world.StructureVersion;
            _id = -1;
            Current = Entity.None;
        }

        public Entity Current { get; private set; }

        public bool MoveNext()
        {
            QueryGuard.EnsureUnchanged(_world, _version);
            while (++_id < _world.EntityCapacity)
            {
                if (_world.IsAliveId(_id))
                {
                    Current = _world.GetEntityById(_id);
                    return true;
                }
            }

            return false;
        }
    }
}

public readonly struct EntityQuery<T1> where T1 : struct, IComponent
{
    private readonly World _world;
    private readonly ComponentPool<T1> _pool;

    internal EntityQuery(World world, ComponentPool<T1> pool) => (_world, _pool) = (world, pool);
    public Enumerator GetEnumerator() => new(_world, _pool);

    public struct Enumerator
    {
        private readonly World _world;
        private readonly ComponentPool<T1> _pool;
        private readonly int _version;
        private int _index;

        internal Enumerator(World world, ComponentPool<T1> pool)
        {
            _world = world;
            _pool = pool;
            _version = world.StructureVersion;
            _index = -1;
        }

        public Entity Current => _pool.GetEntity(_index);

        public bool MoveNext()
        {
            QueryGuard.EnsureUnchanged(_world, _version);
            return ++_index < _pool.Count;
        }
    }
}

public readonly struct EntityQuery<T1, T2>
    where T1 : struct, IComponent
    where T2 : struct, IComponent
{
    private readonly World _world;
    private readonly ComponentPool<T1> _pool1;
    private readonly ComponentPool<T2> _pool2;

    internal EntityQuery(World world, ComponentPool<T1> pool1, ComponentPool<T2> pool2) =>
        (_world, _pool1, _pool2) = (world, pool1, pool2);

    public Enumerator GetEnumerator() => new(_world, _pool1, _pool2);

    public struct Enumerator
    {
        private readonly World _world;
        private readonly ComponentPool<T1> _pool1;
        private readonly ComponentPool<T2> _pool2;
        private readonly IComponentPool _smallest;
        private readonly int _version;
        private int _index;

        internal Enumerator(World world, ComponentPool<T1> pool1, ComponentPool<T2> pool2)
        {
            _world = world;
            _pool1 = pool1;
            _pool2 = pool2;
            _smallest = pool1.Count <= pool2.Count ? pool1 : pool2;
            _version = world.StructureVersion;
            _index = -1;
            Current = Entity.None;
        }

        public Entity Current { get; private set; }

        public bool MoveNext()
        {
            QueryGuard.EnsureUnchanged(_world, _version);
            while (++_index < _smallest.Count)
            {
                var candidate = _smallest.GetEntity(_index);
                if (_pool1.Has(candidate) && _pool2.Has(candidate))
                {
                    Current = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}

public readonly struct EntityQuery<T1, T2, T3>
    where T1 : struct, IComponent
    where T2 : struct, IComponent
    where T3 : struct, IComponent
{
    private readonly World _world;
    private readonly ComponentPool<T1> _pool1;
    private readonly ComponentPool<T2> _pool2;
    private readonly ComponentPool<T3> _pool3;

    internal EntityQuery(
        World world,
        ComponentPool<T1> pool1,
        ComponentPool<T2> pool2,
        ComponentPool<T3> pool3) =>
        (_world, _pool1, _pool2, _pool3) = (world, pool1, pool2, pool3);

    public Enumerator GetEnumerator() => new(_world, _pool1, _pool2, _pool3);

    public struct Enumerator
    {
        private readonly World _world;
        private readonly ComponentPool<T1> _pool1;
        private readonly ComponentPool<T2> _pool2;
        private readonly ComponentPool<T3> _pool3;
        private readonly IComponentPool _smallest;
        private readonly int _version;
        private int _index;

        internal Enumerator(
            World world,
            ComponentPool<T1> pool1,
            ComponentPool<T2> pool2,
            ComponentPool<T3> pool3)
        {
            _world = world;
            _pool1 = pool1;
            _pool2 = pool2;
            _pool3 = pool3;
            _smallest = QueryGuard.Smallest(pool1, pool2, pool3);
            _version = world.StructureVersion;
            _index = -1;
            Current = Entity.None;
        }

        public Entity Current { get; private set; }

        public bool MoveNext()
        {
            QueryGuard.EnsureUnchanged(_world, _version);
            while (++_index < _smallest.Count)
            {
                var candidate = _smallest.GetEntity(_index);
                if (_pool1.Has(candidate) && _pool2.Has(candidate) && _pool3.Has(candidate))
                {
                    Current = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}

public readonly struct EntityQuery<T1, T2, T3, T4>
    where T1 : struct, IComponent
    where T2 : struct, IComponent
    where T3 : struct, IComponent
    where T4 : struct, IComponent
{
    private readonly World _world;
    private readonly ComponentPool<T1> _pool1;
    private readonly ComponentPool<T2> _pool2;
    private readonly ComponentPool<T3> _pool3;
    private readonly ComponentPool<T4> _pool4;

    internal EntityQuery(
        World world,
        ComponentPool<T1> pool1,
        ComponentPool<T2> pool2,
        ComponentPool<T3> pool3,
        ComponentPool<T4> pool4) =>
        (_world, _pool1, _pool2, _pool3, _pool4) = (world, pool1, pool2, pool3, pool4);

    public Enumerator GetEnumerator() => new(_world, _pool1, _pool2, _pool3, _pool4);

    public struct Enumerator
    {
        private readonly World _world;
        private readonly ComponentPool<T1> _pool1;
        private readonly ComponentPool<T2> _pool2;
        private readonly ComponentPool<T3> _pool3;
        private readonly ComponentPool<T4> _pool4;
        private readonly IComponentPool _smallest;
        private readonly int _version;
        private int _index;

        internal Enumerator(
            World world,
            ComponentPool<T1> pool1,
            ComponentPool<T2> pool2,
            ComponentPool<T3> pool3,
            ComponentPool<T4> pool4)
        {
            _world = world;
            _pool1 = pool1;
            _pool2 = pool2;
            _pool3 = pool3;
            _pool4 = pool4;
            _smallest = QueryGuard.Smallest(pool1, pool2, pool3, pool4);
            _version = world.StructureVersion;
            _index = -1;
            Current = Entity.None;
        }

        public Entity Current { get; private set; }

        public bool MoveNext()
        {
            QueryGuard.EnsureUnchanged(_world, _version);
            while (++_index < _smallest.Count)
            {
                var candidate = _smallest.GetEntity(_index);
                if (_pool1.Has(candidate) &&
                    _pool2.Has(candidate) &&
                    _pool3.Has(candidate) &&
                    _pool4.Has(candidate))
                {
                    Current = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}

internal static class QueryGuard
{
    public static void EnsureUnchanged(World world, int expectedVersion)
    {
        if (world.StructureVersion != expectedVersion)
        {
            throw new InvalidOperationException(
                "The ECS structure changed during iteration. Use EntityCommandBuffer and play it back after the query.");
        }
    }

    public static IComponentPool Smallest(params IComponentPool[] pools)
    {
        var smallest = pools[0];
        for (var index = 1; index < pools.Length; index++)
        {
            if (pools[index].Count < smallest.Count)
            {
                smallest = pools[index];
            }
        }

        return smallest;
    }
}
