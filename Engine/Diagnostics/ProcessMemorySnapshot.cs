using System;
using System.Diagnostics;

namespace MonoGame_project.Engine.Diagnostics;

/// <summary>Memory values useful for comparing managed and native physics engines.</summary>
public readonly record struct ProcessMemorySnapshot(long ManagedHeapBytes, long ResidentBytes)
{
    public static ProcessMemorySnapshot Capture()
    {
        using var process = Process.GetCurrentProcess();
        return new ProcessMemorySnapshot(
            GC.GetTotalMemory(forceFullCollection: false),
            process.WorkingSet64);
    }

    public override string ToString() =>
        $"managed heap: {ManagedHeapBytes / (1024d * 1024d):F1} MiB, " +
        $"process resident memory: {ResidentBytes / (1024d * 1024d):F1} MiB";
}
