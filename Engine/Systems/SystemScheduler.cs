using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Content;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Rendering;
using MonoGame_project.Engine.Time;

namespace MonoGame_project.Engine.Systems;

/// <summary>Runs systems in a stable, explicit order.</summary>
public sealed class SystemScheduler
{
    private readonly List<SystemEntry> _entries = [];
    private World _world = null!;
    private EngineContext _context = null!;
    private ContentManager _content = null!;
    private long _nextSequence;
    private bool _initialized;
    private bool _contentLoaded;
    private bool _executing;

    public int Count => _entries.Count;

    public SystemScheduler Add(IGameSystem system, int order = 0)
    {
        ArgumentNullException.ThrowIfNull(system);
        EnsureNotExecuting();

        if (_entries.Any(entry => ReferenceEquals(entry.System, system)))
        {
            throw new InvalidOperationException("The same system instance cannot be added twice.");
        }

        var entry = new SystemEntry(system, order, _nextSequence++);
        _entries.Add(entry);
        _entries.Sort(SystemEntryComparer.Instance);

        if (_initialized)
        {
            system.Initialize(_world!, _context!);
            if (_contentLoaded && system is IContentSystem contentSystem)
            {
                contentSystem.LoadContent(_world!, _content!);
            }
        }

        return this;
    }

    public bool Remove(IGameSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        EnsureNotExecuting();

        var index = _entries.FindIndex(entry => ReferenceEquals(entry.System, system));
        if (index < 0)
        {
            return false;
        }

        if (_contentLoaded && system is IContentSystem contentSystem)
        {
            contentSystem.UnloadContent(_world!);
        }

        if (_initialized)
        {
            system.Shutdown(_world!);
        }

        _entries.RemoveAt(index);
        return true;
    }

    internal void Initialize(World world, EngineContext context)
    {
        if (_initialized)
        {
            throw new InvalidOperationException("SystemScheduler is already initialized.");
        }

        _world = world;
        _context = context;
        Execute(system => system.Initialize(world, context));
        _initialized = true;
    }

    internal void LoadContent(ContentManager content)
    {
        EnsureInitialized();
        if (_contentLoaded)
        {
            return;
        }

        _content = content;
        Execute(system =>
        {
            if (system is IContentSystem contentSystem)
            {
                contentSystem.LoadContent(_world!, content);
            }
        });
        _contentLoaded = true;
    }

    internal void Update(in FrameTime time)
    {
        EnsureInitialized();
        var capturedTime = time;
        Execute(system =>
        {
            if (system is IUpdateSystem updateSystem)
            {
                updateSystem.Update(_world!, capturedTime);
            }
        });
    }

    internal void FixedUpdate(in FixedTime time)
    {
        EnsureInitialized();
        var capturedTime = time;
        Execute(system =>
        {
            if (system is IFixedUpdateSystem fixedUpdateSystem)
            {
                fixedUpdateSystem.FixedUpdate(_world!, capturedTime);
            }
        });
    }

    internal void Draw(in RenderContext context)
    {
        EnsureInitialized();
        var capturedContext = context;
        Execute(system =>
        {
            if (system is IRenderSystem renderSystem)
            {
                renderSystem.Draw(_world!, capturedContext);
            }
        });
    }

    internal void UnloadContent()
    {
        if (!_contentLoaded)
        {
            return;
        }

        ExecuteReverse(system =>
        {
            if (system is IContentSystem contentSystem)
            {
                contentSystem.UnloadContent(_world!);
            }
        });
        _contentLoaded = false;
        _content = null!;
    }

    internal void Shutdown()
    {
        if (!_initialized)
        {
            return;
        }

        UnloadContent();
        ExecuteReverse(system => system.Shutdown(_world!));
        _initialized = false;
        _world = null!;
        _context = null!;
    }

    private void Execute(Action<IGameSystem> action)
    {
        _executing = true;
        try
        {
            foreach (var entry in _entries)
            {
                action(entry.System);
            }
        }
        finally
        {
            _executing = false;
        }
    }

    private void ExecuteReverse(Action<IGameSystem> action)
    {
        _executing = true;
        try
        {
            for (var index = _entries.Count - 1; index >= 0; index--)
            {
                action(_entries[index].System);
            }
        }
        finally
        {
            _executing = false;
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("SystemScheduler has not been initialized.");
        }
    }

    private void EnsureNotExecuting()
    {
        if (_executing)
        {
            throw new InvalidOperationException("Systems cannot be added or removed while the scheduler is executing.");
        }
    }

    private readonly record struct SystemEntry(IGameSystem System, int Order, long Sequence);

    private sealed class SystemEntryComparer : IComparer<SystemEntry>
    {
        public static SystemEntryComparer Instance { get; } = new();

        public int Compare(SystemEntry left, SystemEntry right)
        {
            var orderComparison = left.Order.CompareTo(right.Order);
            return orderComparison != 0 ? orderComparison : left.Sequence.CompareTo(right.Sequence);
        }
    }
}
