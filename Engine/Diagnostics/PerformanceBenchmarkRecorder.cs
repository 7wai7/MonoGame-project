using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame_project.Game.Terrain;

namespace MonoGame_project.Engine.Diagnostics;

/// <summary>Collects frame samples in memory, then writes one set of files per process run.</summary>
public sealed class PerformanceBenchmarkRecorder : IDisposable
{
    private readonly List<FrameSample> _samples = [];
    private readonly StreamWriter _events;
    private readonly string _framesPath;
    private readonly List<object> _summaries = [];
    private readonly Process _process = Process.GetCurrentProcess();
    private FrameSample _frame;
    private long _previousDrawAt;
    private TimeSpan _cpuAtStart;
    private int[] _gcAtStart = [];
    private string? _label;
    private float _uiScale;
    private int _captureWindowWidth;
    private int _captureWindowHeight;
    private int _captureBackBufferWidth;
    private int _captureBackBufferHeight;
    private bool _resolutionChanged;
    private int _warmupFrames;
    private bool _disposed;

    public PerformanceBenchmarkRecorder(PerformanceBenchmarkOptions options)
    {
        Options = options;
        var id = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) +
                 "_" + Guid.NewGuid().ToString("N")[..8];
        DirectoryPath = Path.Combine(options.OutputRoot, id +
            (options.Fullscreen ? "_fullscreen" : "_window") +
            (options.UiEnabled ? "_ui-on" : "_ui-off") +
            (options.UsesFixedRenderTarget ? "_matched" : ""));
        Directory.CreateDirectory(DirectoryPath);
        _framesPath = Path.Combine(DirectoryPath, "frames.csv");
        File.WriteAllText(_framesPath,
            "stage,frameMs,updateMs,sceneUpdateMs,fixedUpdateMs,sceneDrawMs,terrainDrawMs,uiHandleMs,uiRefreshMs,gumUpdateMs,uiAfterDrawMs,gumDrawMs,drawCpuMs,chunks,triangles\n");
        _events = new StreamWriter(Path.Combine(DirectoryPath, "events.jsonl"), append: false)
            { AutoFlush = true };
        Event("run-start", $"UI {(options.UiEnabled ? "on" : "off")}, " +
            $"{(options.Fullscreen ? "fullscreen" : "window")}; output: {DirectoryPath}");
    }

    public PerformanceBenchmarkOptions Options { get; }
    public string DirectoryPath { get; }
    internal TerrainRenderStatistics? TerrainStatistics { get; set; }
    public BenchmarkRenderMode RenderMode { get; set; } = BenchmarkRenderMode.CurrentTerrain;
    public bool InspectorVisible { get; set; }
    public bool IsCapturing => _label is not null;

    public void WriteEnvironment(GraphicsDevice device, GameWindow window, float uiScale)
    {
        _uiScale = uiScale;
        var backBuffer = device.PresentationParameters;
        var data = new
        {
            utc = DateTimeOffset.UtcNow,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            cpuCount = Environment.ProcessorCount,
            configuration =
#if DEBUG
                "Debug",
#else
                "Release",
#endif
            uiEnabled = Options.UiEnabled,
            fullscreenRequested = Options.Fullscreen,
            fullscreenApplied = backBuffer.IsFullScreen,
            vsyncRequested = false,
            presentInterval = backBuffer.PresentationInterval.ToString(),
            fixedTimeStep = false,
            uiScale,
            windowWidth = window.ClientBounds.Width,
            windowHeight = window.ClientBounds.Height,
            backBufferWidth = backBuffer.BackBufferWidth,
            backBufferHeight = backBuffer.BackBufferHeight,
            viewportWidth = device.Viewport.Width,
            viewportHeight = device.Viewport.Height,
            secondsPerCapture = Options.Seconds,
            rounds = Options.Rounds,
            fixedInternalWidth = Options.InternalWidth,
            fixedInternalHeight = Options.InternalHeight,
            processId = Environment.ProcessId,
            commit = Environment.GetEnvironmentVariable("BENCHMARK_GIT_COMMIT") ?? "unknown"
        };
        File.WriteAllText(Path.Combine(DirectoryPath, "environment.json"),
            JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Event(string kind, string message)
    {
        _events.WriteLine(JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, kind, message }));
        Console.WriteLine($"[benchmark] {message}");
    }

    public void BeginCapture(string label, GraphicsDevice device, GameWindow window)
    {
        if (_label is not null) throw new InvalidOperationException("Capture already active.");
        _label = label;
        WriteEnvironment(device, window, _uiScale);
        _captureWindowWidth = window.ClientBounds.Width;
        _captureWindowHeight = window.ClientBounds.Height;
        _captureBackBufferWidth = device.PresentationParameters.BackBufferWidth;
        _captureBackBufferHeight = device.PresentationParameters.BackBufferHeight;
        _resolutionChanged = false;
        _warmupFrames = 5;
        _samples.Clear();
        _previousDrawAt = 0;
        _cpuAtStart = _process.TotalProcessorTime;
        _gcAtStart = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
        Event("capture-start", $"START {label}: {RenderMode}, " +
            $"window {window.ClientBounds.Width}x{window.ClientBounds.Height}, " +
            $"backbuffer {device.PresentationParameters.BackBufferWidth}x{device.PresentationParameters.BackBufferHeight}, " +
            $"viewport {device.Viewport.Width}x{device.Viewport.Height}, " +
            $"internal {(Options.UsesFixedRenderTarget ? $"{Options.InternalWidth}x{Options.InternalHeight}" : "native")}");
    }

    public void BeginFrame() => _frame = default;

    public void EndFrame(long drawStartedAt, double drawCpuMs,
        GraphicsDevice device, GameWindow window)
    {
        if (_label is null) return;
        if (window.ClientBounds.Width != _captureWindowWidth ||
            window.ClientBounds.Height != _captureWindowHeight ||
            device.PresentationParameters.BackBufferWidth != _captureBackBufferWidth ||
            device.PresentationParameters.BackBufferHeight != _captureBackBufferHeight)
            _resolutionChanged = true;
        _frame.DrawCpuMs = drawCpuMs;
        _frame.FrameMs = _previousDrawAt == 0 ? 0d :
            Stopwatch.GetElapsedTime(_previousDrawAt, drawStartedAt).TotalMilliseconds;
        _previousDrawAt = drawStartedAt;
        if (_frame.FrameMs <= 0d) return;
        if (_warmupFrames-- > 0) return;
        if (RenderMode != BenchmarkRenderMode.ClearOnly && TerrainStatistics is { } terrain)
        {
            _frame.Chunks = terrain.TotalChunks;
            _frame.Triangles = terrain.TotalTriangles;
        }
        _samples.Add(_frame);
    }

    public void RecordUpdate(double ms) => _frame.UpdateMs += ms;
    public void RecordSceneUpdate(double ms) => _frame.SceneUpdateMs += ms;
    public void RecordFixedUpdate(double ms) => _frame.FixedUpdateMs += ms;
    public void RecordSceneDraw(double ms) => _frame.SceneDrawMs += ms;
    public void RecordTerrainDraw(double ms) => _frame.TerrainDrawMs += ms;
    public void RecordUiHandle(double ms) => _frame.UiHandleMs += ms;
    public void RecordUiRefresh(double ms) => _frame.UiRefreshMs += ms;
    public void RecordGumUpdate(double ms) => _frame.GumUpdateMs += ms;
    public void RecordUiAfterDraw(double ms) => _frame.UiAfterDrawMs += ms;
    public void RecordGumDraw(double ms) => _frame.GumDrawMs += ms;

    public void EndCapture()
    {
        if (_label is null) throw new InvalidOperationException("No capture active.");
        var label = _label;
        _label = null;
        if (_resolutionChanged)
        {
            Event("capture-invalid", $"INVALID {label}: window or backbuffer changed during capture.");
            _samples.Clear();
            return;
        }
        var cpuMs = (_process.TotalProcessorTime - _cpuAtStart).TotalMilliseconds;
        if (_samples.Count == 0)
        {
            Event("capture-invalid", $"INVALID {label}: no complete frames");
            return;
        }

        var csv = new StringBuilder(_samples.Count * 120);
        foreach (var sample in _samples)
        {
            csv.Append(label);
            foreach (var value in new[] { sample.FrameMs, sample.UpdateMs, sample.SceneUpdateMs,
                         sample.FixedUpdateMs, sample.SceneDrawMs, sample.TerrainDrawMs,
                         sample.UiHandleMs, sample.UiRefreshMs, sample.GumUpdateMs,
                         sample.UiAfterDrawMs, sample.GumDrawMs, sample.DrawCpuMs })
                csv.Append(',').Append(value.ToString("F4", CultureInfo.InvariantCulture));
            csv.Append(',').Append(sample.Chunks).Append(',').Append(sample.Triangles).Append('\n');
        }
        File.AppendAllText(_framesPath, csv.ToString());

        var ordered = _samples.Select(sample => sample.FrameMs).Order().ToArray();
        var mean = ordered.Average();
        double Percentile(double fraction) => ordered[(int)Math.Ceiling((ordered.Length - 1) * fraction)];
        _process.Refresh();
        var summary = new
        {
            label,
            renderMode = RenderMode.ToString(),
            uiEnabled = Options.UiEnabled,
            fullscreen = Options.Fullscreen,
            fixedInternalWidth = Options.InternalWidth,
            fixedInternalHeight = Options.InternalHeight,
            windowWidth = _captureWindowWidth,
            windowHeight = _captureWindowHeight,
            backBufferWidth = _captureBackBufferWidth,
            backBufferHeight = _captureBackBufferHeight,
            frames = ordered.Length,
            fps = 1000d / mean,
            meanMs = mean,
            p50Ms = Percentile(.50),
            p95Ms = Percentile(.95),
            p99Ms = Percentile(.99),
            maxMs = ordered[^1],
            averageUpdateMs = _samples.Average(sample => sample.UpdateMs),
            averageSceneUpdateMs = _samples.Average(sample => sample.SceneUpdateMs),
            averageFixedUpdateMs = _samples.Average(sample => sample.FixedUpdateMs),
            averageSceneDrawMs = _samples.Average(sample => sample.SceneDrawMs),
            averageTerrainDrawMs = _samples.Average(sample => sample.TerrainDrawMs),
            averageUiHandleMs = _samples.Average(sample => sample.UiHandleMs),
            averageUiRefreshMs = _samples.Average(sample => sample.UiRefreshMs),
            averageGumUpdateMs = _samples.Average(sample => sample.GumUpdateMs),
            averageUiAfterDrawMs = _samples.Average(sample => sample.UiAfterDrawMs),
            averageGumDrawMs = _samples.Average(sample => sample.GumDrawMs),
            averageDrawCpuMs = _samples.Average(sample => sample.DrawCpuMs),
            averageChunks = _samples.Average(sample => sample.Chunks),
            averageTriangles = _samples.Average(sample => sample.Triangles),
            processCpuMs = cpuMs,
            managedHeapBytes = GC.GetTotalMemory(false),
            workingSetBytes = _process.WorkingSet64,
            gc0 = GC.CollectionCount(0) - _gcAtStart[0],
            gc1 = GC.CollectionCount(1) - _gcAtStart[1],
            gc2 = GC.CollectionCount(2) - _gcAtStart[2]
        };
        _summaries.Add(summary);
        File.WriteAllText(Path.Combine(DirectoryPath, "summary.json"),
            JsonSerializer.Serialize(_summaries, new JsonSerializerOptions { WriteIndented = true }));
        Event("capture-end", $"END {label}: {ordered.Length} frames, {1000d / mean:F1} FPS, " +
            $"mean {mean:F3} ms, p95 {Percentile(.95):F3} ms, p99 {Percentile(.99):F3} ms; " +
            $"CPU update {_samples.Average(sample => sample.UpdateMs):F3} ms, " +
            $"scene draw {_samples.Average(sample => sample.SceneDrawMs):F3} ms, " +
            $"Gum draw {_samples.Average(sample => sample.GumDrawMs):F3} ms");
        _samples.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_label is not null) Event("capture-invalid", $"INCOMPLETE {_label}: process stopped");
        Event("run-end", "Run finished; results written to " + DirectoryPath);
        _events.Dispose();
        _process.Dispose();
        _disposed = true;
    }

    private struct FrameSample
    {
        public double FrameMs;
        public double UpdateMs;
        public double SceneUpdateMs;
        public double FixedUpdateMs;
        public double SceneDrawMs;
        public double TerrainDrawMs;
        public double UiHandleMs;
        public double UiRefreshMs;
        public double GumUpdateMs;
        public double UiAfterDrawMs;
        public double GumDrawMs;
        public double DrawCpuMs;
        public int Chunks;
        public long Triangles;
    }
}
