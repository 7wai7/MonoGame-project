using System;

namespace MonoGame_project.Engine.Time;

/// <summary>Timing data for one deterministic simulation tick.</summary>
public readonly record struct FixedTime(
    TimeSpan Step,
    TimeSpan Total,
    ulong TickNumber)
{
    public float DeltaSeconds => (float)Step.TotalSeconds;
}
