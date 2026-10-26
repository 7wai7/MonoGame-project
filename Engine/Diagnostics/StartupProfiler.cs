using System;
using System.Diagnostics;

namespace MonoGame_project.Engine.Diagnostics;

/// <summary>
/// Lightweight wall-clock measurements from Program.Main to the first drawn frame.
/// Stops recording after startup, so normal gameplay does not produce timing logs.
/// </summary>
public static class StartupProfiler
{
    private static long _startedAt;
    private static bool _active;

    public static bool IsActive => _active;

    public static void Start()
    {
        _startedAt = Stopwatch.GetTimestamp();
        _active = true;
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        Console.WriteLine($"[startup] {configuration}: measuring startup to first drawn frame...");
    }

    public static Scope Measure(string name) =>
        new(name, _active ? Stopwatch.GetTimestamp() : 0);

    public static void Info(string message)
    {
        if (_active)
        {
            Console.WriteLine($"[startup] {message}");
        }
    }

    public static void Record(string name, TimeSpan duration, string? details = null)
    {
        if (!_active)
        {
            return;
        }

        var sinceStart = Stopwatch.GetElapsedTime(_startedAt);
        var suffix = details is null ? string.Empty : $" ({details})";
        Console.WriteLine(
            $"[startup +{sinceStart.TotalSeconds:F3}s] {name}: {duration.TotalSeconds:F3}s{suffix}");
    }

    public static void Complete()
    {
        if (!_active)
        {
            return;
        }

        var total = Stopwatch.GetElapsedTime(_startedAt);
        _active = false;
        Console.WriteLine($"[startup] First frame drawn after {total.TotalSeconds:F3}s. " +
            $"{ProcessMemorySnapshot.Capture()}.");
    }

    public readonly struct Scope : IDisposable
    {
        private readonly string _name;
        private readonly long _startedAt;

        internal Scope(string name, long startedAt)
        {
            _name = name;
            _startedAt = startedAt;
        }

        public void Dispose()
        {
            if (_startedAt != 0)
            {
                Record(_name, Stopwatch.GetElapsedTime(_startedAt));
            }
        }
    }
}
