using System.Diagnostics;
using Microsoft.Xna.Framework;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Diagnostics;
using MonoGame_project.Engine.Input;
using MonoGame_project.Engine.Rendering.ThreeD;
using MonoGame_project.Engine.Systems;
using MonoGame_project.Engine.Time;
using MonoGame_project.Game.UI;

namespace MonoGame_project.Game.Terrain;

/// <summary>Runs fixed-camera terrain and UI comparisons and exits automatically.</summary>
internal sealed class PerformanceBenchmarkSystem(
    TerrainRenderStreamingSystem streaming,
    TerrainColliderStreamingSystem collisionStreaming)
    : GameSystem, IUpdateSystem
{
    private static readonly BenchmarkView[] Views =
    [
        new("panorama", new Vector2(0f, 0f), 120f, 0f, -20f),
        new("ground-70m", new Vector2(0f, 0f), 70f, 0f, -88f)
    ];
    private static readonly BenchmarkRenderMode[] Modes =
        [BenchmarkRenderMode.ClearOnly, BenchmarkRenderMode.FlatTerrain,
         BenchmarkRenderMode.CurrentTerrain];

    private Entity _camera;
    private Entity _inspected;
    private Func<float, float, float> _sampleHeight = null!;
    private InspectHudScreen? _inspector;
    private PerformanceBenchmarkRecorder _recorder = null!;
    private Stage _stage = Stage.WaitingForFocus;
    private long _stageStartedAt;
    private long _focusWaitStartedAt;
    private long _focusStableAt;
    private int _focusWindowWidth;
    private int _focusWindowHeight;
    private int _focusBackBufferWidth;
    private int _focusBackBufferHeight;
    private int _settleWindowWidth;
    private int _settleWindowHeight;
    private int _settleBackBufferWidth;
    private int _settleBackBufferHeight;
    private int _viewIndex;
    private int _roundIndex;
    private int _modeIndex;

    public void Configure(Entity camera, Entity inspected,
        Func<float, float, float> sampleHeight, InspectHudScreen? inspector)
    {
        _camera = camera;
        _inspected = inspected;
        _sampleHeight = sampleHeight;
        _inspector = inspector;
    }

    protected override void OnInitialize()
    {
        _recorder = Context.Benchmark ??
            throw new InvalidOperationException("A benchmark recorder is required.");
        if (_camera.IsNone || _inspected.IsNone || _sampleHeight is null)
            throw new InvalidOperationException("Configure benchmark entities first.");
        Context.InputContexts.Reset(InputContexts.TerrainBenchmark);
        streaming.UseDeterministicLod = true;
        _focusWaitStartedAt = Stopwatch.GetTimestamp();
        _recorder.Event("focus", "Waiting for the game window to receive input focus.");
    }

    public void Update(World world, in FrameTime time)
    {
        if (_stage == Stage.Completed) return;
        var now = Stopwatch.GetTimestamp();
        if (!Context.Input.HasActiveInput)
        {
            if (_stage == Stage.Capturing)
            {
                Abort("Window lost focus during capture; current run is invalid.");
            }
            else
            {
                if (_stage != Stage.WaitingForFocus)
                {
                    _recorder.Event("focus", "Window focus changed during preparation; waiting for a stable window.");
                    streaming.IsPaused = false;
                    collisionStreaming.IsPaused = false;
                    _stage = Stage.WaitingForFocus;
                    _focusStableAt = 0;
                    _focusWaitStartedAt = now;
                }
                if (Stopwatch.GetElapsedTime(_focusWaitStartedAt, now).TotalSeconds > 120d)
                    Abort("Window did not regain focus within 120 seconds.");
            }
            return;
        }

        switch (_stage)
        {
            case Stage.WaitingForFocus:
                var window = Context.Window.ClientBounds;
                var buffer = Context.GraphicsDevice.PresentationParameters;
                if (_focusStableAt == 0 || window.Width != _focusWindowWidth ||
                    window.Height != _focusWindowHeight ||
                    buffer.BackBufferWidth != _focusBackBufferWidth ||
                    buffer.BackBufferHeight != _focusBackBufferHeight)
                {
                    _focusStableAt = now;
                    _focusWindowWidth = window.Width;
                    _focusWindowHeight = window.Height;
                    _focusBackBufferWidth = buffer.BackBufferWidth;
                    _focusBackBufferHeight = buffer.BackBufferHeight;
                }
                else if (Stopwatch.GetElapsedTime(_focusStableAt, now).TotalSeconds >= 2d)
                    ApplyView(now);
                break;
            case Stage.WaitingForMeshes:
                var (visible, ready) = streaming.GetVisibleReadiness();
                if (visible > 0 && ready == visible && !streaming.HasRunningBuild)
                {
                    streaming.IsPaused = true;
                    _recorder.Event("ready", $"{Views[_viewIndex].Name}: {ready}/{visible} visible LODs ready; streaming paused.");
                    PrepareMode(now);
                }
                else if (Elapsed(now) > 180d)
                    Abort($"Terrain readiness timed out: {ready}/{visible} visible chunks.");
                break;
            case Stage.Settling:
                var settleWindow = Context.Window.ClientBounds;
                var settleBuffer = Context.GraphicsDevice.PresentationParameters;
                if (settleWindow.Width != _settleWindowWidth ||
                    settleWindow.Height != _settleWindowHeight ||
                    settleBuffer.BackBufferWidth != _settleBackBufferWidth ||
                    settleBuffer.BackBufferHeight != _settleBackBufferHeight)
                {
                    _recorder.Event("display", "Window size changed during settling; rechecking visible LODs.");
                    streaming.IsPaused = false;
                    collisionStreaming.IsPaused = false;
                    _stage = Stage.WaitingForMeshes;
                    _stageStartedAt = now;
                }
                else if (Elapsed(now) >= 2d)
                {
                    collisionStreaming.IsPaused = true;
                    _recorder.BeginCapture(Label(), Context.GraphicsDevice, Context.Window);
                    _stageStartedAt = now;
                    _stage = Stage.Capturing;
                }
                break;
            case Stage.Capturing:
                if (Elapsed(now) >= _recorder.Options.Seconds)
                {
                    _recorder.EndCapture();
                    Advance(now);
                }
                break;
        }
    }

    private void ApplyView(long now)
    {
        var view = Views[_viewIndex];
        var position = new Vector3(view.XZ.X,
            _sampleHeight(view.XZ.X, view.XZ.Y) + view.HeightAboveTerrain, view.XZ.Y);
        var yaw = MathHelper.ToRadians(view.YawDegrees);
        var pitch = MathHelper.ToRadians(view.PitchDegrees);
        var direction = new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch),
            MathF.Sin(pitch), -MathF.Cos(yaw) * MathF.Cos(pitch));
        World.Get<Transform3D>(_camera).Position = position;
        ref var camera = ref World.Get<Camera3D>(_camera);
        camera.Target = position + direction;
        camera.Up = Vector3.Up;
        streaming.IsPaused = false;
        collisionStreaming.IsPaused = false;
        _stageStartedAt = now;
        _stage = Stage.WaitingForMeshes;
        _recorder.Event("view", $"View {view.Name}: position {position}, yaw {view.YawDegrees}°, pitch {view.PitchDegrees}°.");
    }

    private void PrepareMode(long now)
    {
        var inspectorStage = _modeIndex == Modes.Length;
        _recorder.RenderMode = inspectorStage ? BenchmarkRenderMode.CurrentTerrain : Modes[_modeIndex];
        _recorder.InspectorVisible = inspectorStage;
        if (inspectorStage)
        {
            World.GetResource<InspectSessionState>().Selected = _inspected;
            _inspector?.ShowBenchmarkDetails(_inspected);
        }
        else
        {
            World.GetResource<InspectSessionState>().Selected = Entity.None;
        }
        _stage = Stage.Settling;
        _stageStartedAt = now;
        _settleWindowWidth = Context.Window.ClientBounds.Width;
        _settleWindowHeight = Context.Window.ClientBounds.Height;
        _settleBackBufferWidth = Context.GraphicsDevice.PresentationParameters.BackBufferWidth;
        _settleBackBufferHeight = Context.GraphicsDevice.PresentationParameters.BackBufferHeight;
        _recorder.Event("settling", $"Preparing {Label()} for 2 seconds.");
    }

    private void Advance(long now)
    {
        _modeIndex++;
        var stagesForView = _viewIndex == Views.Length - 1 ? Modes.Length + 1 : Modes.Length;
        if (_modeIndex < stagesForView)
        {
            PrepareMode(now);
            return;
        }
        _modeIndex = 0;
        _roundIndex++;
        if (_roundIndex < _recorder.Options.Rounds)
        {
            PrepareMode(now);
            return;
        }
        _roundIndex = 0;
        _viewIndex++;
        if (_viewIndex < Views.Length)
        {
            ApplyView(now);
            return;
        }
        _recorder.Event("complete", "All measurements completed; exiting.");
        Finish();
    }

    private string Label() =>
        $"{Views[_viewIndex].Name}/round-{_roundIndex + 1}/" +
        (_modeIndex == Modes.Length ? "inspector-open" : Modes[_modeIndex].ToString());

    private double Elapsed(long now) =>
        Stopwatch.GetElapsedTime(_stageStartedAt, now).TotalSeconds;

    private void Abort(string reason)
    {
        _recorder.Event("invalid", "INVALID RUN: " + reason);
        Finish();
    }

    private void Finish()
    {
        _stage = Stage.Completed;
        Context.Application.RequestExit();
    }

    private readonly record struct BenchmarkView(
        string Name, Vector2 XZ, float HeightAboveTerrain, float YawDegrees, float PitchDegrees);

    private enum Stage { WaitingForFocus, WaitingForMeshes, Settling, Capturing, Completed }
}
