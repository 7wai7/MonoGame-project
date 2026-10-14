using System;
using System.Collections.Generic;

namespace MonoGame_project.Ecs;

/// <summary>
/// Defers structural ECS changes until a query has finished iterating.
/// </summary>
public sealed class EntityCommandBuffer
{
    private readonly List<Action<World>> _commands = [];
    private bool _isPlayingBack;

    public int Count => _commands.Count;

    public void Create() => _commands.Add(world => world.CreateEntity());

    public void Create(Action<EntityBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _commands.Add(world =>
        {
            var entity = world.CreateEntity();
            configure(new EntityBuilder(world, entity));
        });
    }

    public void Destroy(Entity entity) => _commands.Add(world => world.DestroyEntity(entity));

    public void Add<T>(Entity entity, in T component) where T : struct, IComponent
    {
        var captured = component;
        _commands.Add(world => world.Add(entity, captured));
    }

    public void Set<T>(Entity entity, in T component) where T : struct, IComponent
    {
        var captured = component;
        _commands.Add(world => world.Set(entity, captured));
    }

    public void Remove<T>(Entity entity) where T : struct, IComponent =>
        _commands.Add(world => world.Remove<T>(entity));

    public void Playback(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (_isPlayingBack)
        {
            throw new InvalidOperationException("An EntityCommandBuffer cannot play back recursively.");
        }

        _isPlayingBack = true;
        try
        {
            foreach (var command in _commands)
            {
                command(world);
            }
        }
        finally
        {
            _commands.Clear();
            _isPlayingBack = false;
        }
    }

    public void Clear() => _commands.Clear();
}

/// <summary>Configures an entity created during command-buffer playback.</summary>
public readonly struct EntityBuilder
{
    private readonly World _world;

    internal EntityBuilder(World world, Entity entity) => (_world, Entity) = (world, entity);

    public Entity Entity { get; }

    public EntityBuilder Add<T>(in T component) where T : struct, IComponent
    {
        _world.Add(Entity, component);
        return this;
    }

    public EntityBuilder SetResource<T>(T resource) where T : notnull
    {
        _world.SetResource(resource);
        return this;
    }
}
