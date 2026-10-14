using System;

namespace MonoGame_project.Engine.Time;

/// <summary>Timing data for one variable-rate update.</summary>
public readonly record struct FrameTime(
    TimeSpan Elapsed,
    TimeSpan Total,
    ulong FrameNumber)
{
    public float DeltaSeconds => (float)Elapsed.TotalSeconds;
}
