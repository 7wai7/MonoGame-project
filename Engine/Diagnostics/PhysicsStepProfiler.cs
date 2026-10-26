using System;
using System.Diagnostics;

namespace MonoGame_project.Engine.Diagnostics;

/// <summary>Reports fixed-update and physics-step timings in five-second windows.</summary>
internal sealed class PhysicsStepProfiler
{
    private const double WindowSeconds = 5d;

    private long _windowStartedAt;
    private int _sampleCount;
    private double _stepTotalMilliseconds;
    private double _stepMaxMilliseconds;
    private double _fixedTotalMilliseconds;
    private double _fixedMaxMilliseconds;

    public void Record(
        TimeSpan stepDuration,
        TimeSpan fixedDuration,
        int bodyCount,
        int terrainBodyCount)
    {
        // The startup profiler already covers initialization. Start the
        // rolling window only after the first frame has been drawn.
        if (StartupProfiler.IsActive)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (_windowStartedAt == 0)
        {
            _windowStartedAt = now;
        }

        _sampleCount++;
        _stepTotalMilliseconds += stepDuration.TotalMilliseconds;
        _stepMaxMilliseconds = Math.Max(_stepMaxMilliseconds, stepDuration.TotalMilliseconds);
        _fixedTotalMilliseconds += fixedDuration.TotalMilliseconds;
        _fixedMaxMilliseconds = Math.Max(_fixedMaxMilliseconds, fixedDuration.TotalMilliseconds);

        var elapsed = Stopwatch.GetElapsedTime(_windowStartedAt, now);
        if (elapsed.TotalSeconds < WindowSeconds)
        {
            return;
        }

        Console.WriteLine(
            $"[physics] {elapsed.TotalSeconds:F1}s / {_sampleCount} fixed steps: " +
            $"Jolt step avg {_stepTotalMilliseconds / _sampleCount:F2}ms, " +
            $"max {_stepMaxMilliseconds:F2}ms; " +
            $"whole physics fixed update avg {_fixedTotalMilliseconds / _sampleCount:F2}ms, " +
            $"max {_fixedMaxMilliseconds:F2}ms; " +
            $"{bodyCount} active bodies ({terrainBodyCount} terrain); " +
            $"{ProcessMemorySnapshot.Capture()}.");

        _windowStartedAt = now;
        _sampleCount = 0;
        _stepTotalMilliseconds = 0d;
        _stepMaxMilliseconds = 0d;
        _fixedTotalMilliseconds = 0d;
        _fixedMaxMilliseconds = 0d;
    }
}
