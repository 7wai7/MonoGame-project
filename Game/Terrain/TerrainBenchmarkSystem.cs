using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Input;
using MonoGame_project.Engine.Rendering.ThreeD;
using MonoGame_project.Engine.Systems;
using MonoGame_project.Engine.Time;
using MonoGame_project.Game.UI;

namespace MonoGame_project.Game.Terrain;

/// <summary>Temporary, repeatable camera views for comparing terrain shader cost.</summary>
internal sealed class TerrainBenchmarkSystem(
    TerrainRenderStreamingSystem streaming,
    TerrainMaterialRenderSystem material) : GameSystem, IUpdateSystem
{
    private const double StartupDelaySeconds = 5d;
    private const double SettleSeconds = 3d;
    private const double CaptureSeconds = 15d;
    private const double PreparationTimeoutSeconds = 180d;

    // Edit these views between runs. X/Z are world coordinates; height is
    // measured above the heightmap at that X/Z. Yaw 0 faces -Z; negative pitch
    // looks downward. The camera stays fixed throughout each mode comparison.
    private static readonly BenchmarkView[] Views =
    [
        new("panorama", new Vector2(0f, 0f),
            HeightAboveTerrain: 120f, YawDegrees: 0f, PitchDegrees: -20f),
        new("ground-70m", new Vector2(0f, 0f),
            HeightAboveTerrain: 70f, YawDegrees: 0f, PitchDegrees: -88f),
        new("close-25m", new Vector2(0f, 0f),
            HeightAboveTerrain: 25f, YawDegrees: 0f, PitchDegrees: -88f)
    ];
    private static readonly TerrainHexMode[] StandardModes =
        [TerrainHexMode.AllMaterials, TerrainHexMode.GrassOnly, TerrainHexMode.Off];

    private Entity _camera = Entity.None;
    private Func<float, float, float> _sampleHeight = null!;
    private PerformanceScreen _performance = null!;
    private Stage _stage = Stage.StartupDelay;
    private int _viewIndex;
    private int _modeIndex;
    private long _stageStartedAt;
    private long _lastProgressAt;
    private bool _originalControllerEnabled;

    public void Configure(Entity camera, Func<float, float, float> sampleHeight,
        PerformanceScreen performance)
    {
        if (camera.IsNone)
        {
            throw new ArgumentException("A camera entity is required.", nameof(camera));
        }

        _camera = camera;
        _sampleHeight = sampleHeight ?? throw new ArgumentNullException(nameof(sampleHeight));
        _performance = performance ?? throw new ArgumentNullException(nameof(performance));
    }

    protected override void OnInitialize()
    {
        if (_camera.IsNone || _sampleHeight is null || _performance is null)
        {
            throw new InvalidOperationException("Configure the terrain benchmark camera first.");
        }

        ref var controller = ref World.Get<InspectCameraController3D>(_camera);
        _originalControllerEnabled = controller.IsEnabled;
        controller.IsEnabled = false;
        Context.InputContexts.Reset(InputContexts.TerrainBenchmark);
        Console.WriteLine("[benchmark] Automated terrain test enabled. " +
            "Focus the game window; camera moves 5s later. F12 cancels the test.");
    }

    public void Update(World world, in FrameTime time)
    {
        if (_stage == Stage.Completed)
        {
            return;
        }

        if (!Context.Input.HasActiveInput)
        {
            if (_stage == Stage.StartupDelay)
            {
                // Count the initial five seconds only while the game is focused.
                _stageStartedAt = 0;
            }
            else
            {
                Console.WriteLine("[benchmark] Window focus lost; " +
                    "discarding the current capture. Restart for a valid comparison.");
                Finish(cancelled: true);
            }

            return;
        }

        if (Context.Input.WasKeyPressed(Keys.F12))
        {
            Console.WriteLine("[benchmark] Cancelled with F12.");
            Finish(cancelled: true);
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (_stageStartedAt == 0)
        {
            _stageStartedAt = now;
        }

        switch (_stage)
        {
            case Stage.StartupDelay:
                if (Elapsed(now) >= StartupDelaySeconds)
                {
                    ApplyView(now);
                }
                break;

            case Stage.WaitingForMeshes:
                UpdatePreparation(now);
                break;

            case Stage.Settling:
                if (streaming.HasRunningBuild)
                {
                    _stageStartedAt = now;
                }
                else if (Elapsed(now) >= SettleSeconds)
                {
                    BeginCapture(now, StandardModes[0]);
                }
                break;

            case Stage.Capturing:
                if (Elapsed(now) >= CaptureSeconds)
                {
                    _performance.EndCapture();
                    if (++_modeIndex < StandardModes.Length)
                    {
                        BeginCapture(now, StandardModes[_modeIndex]);
                    }
                    else if (++_viewIndex < Views.Length)
                    {
                        ApplyView(now);
                    }
                    else
                    {
                        // The final capture was already ended above.
                        _stage = Stage.Completed;
                        Finish();
                    }
                }
                break;
        }
    }

    private void ApplyView(long now)
    {
        var view = Views[_viewIndex];
        var position = new Vector3(
            view.XZ.X,
            _sampleHeight(view.XZ.X, view.XZ.Y) + view.HeightAboveTerrain,
            view.XZ.Y);
        var yaw = MathHelper.ToRadians(view.YawDegrees);
        var pitch = MathHelper.ToRadians(view.PitchDegrees);
        var direction = new Vector3(
            MathF.Sin(yaw) * MathF.Cos(pitch),
            MathF.Sin(pitch),
            -MathF.Cos(yaw) * MathF.Cos(pitch));

        World.Get<Transform3D>(_camera).Position = position;
        ref var camera = ref World.Get<Camera3D>(_camera);
        camera.Target = position + direction;
        camera.Up = Vector3.Up;
        streaming.IsPaused = false;
        _stage = Stage.WaitingForMeshes;
        _modeIndex = 0;
        _stageStartedAt = now;
        _lastProgressAt = 0;
        Console.WriteLine($"[benchmark] View {view.Name}: position {position}, " +
            $"yaw {view.YawDegrees}°, pitch {view.PitchDegrees}°. " +
            "Waiting for visible terrain LODs before measuring.");
    }

    private void UpdatePreparation(long now)
    {
        var (visible, ready) = streaming.GetVisibleReadiness();
        if (visible > 0 && ready == visible)
        {
            streaming.IsPaused = true;
            _stage = Stage.Settling;
            _stageStartedAt = now;
            Console.WriteLine($"[benchmark] {Views[_viewIndex].Name}: " +
                $"{ready}/{visible} visible chunks ready; meshes frozen, " +
                $"settling for {SettleSeconds:F0}s.");
            return;
        }

        if (Elapsed(now) >= PreparationTimeoutSeconds)
        {
            Console.WriteLine($"[benchmark] Aborted: only {ready}/{visible} " +
                $"visible chunks ready after {PreparationTimeoutSeconds:F0}s. " +
                "No incomplete view will be measured.");
            Finish(cancelled: true);
            return;
        }

        if (_lastProgressAt == 0 ||
            Stopwatch.GetElapsedTime(_lastProgressAt, now).TotalSeconds >= 5d)
        {
            Console.WriteLine($"[benchmark] {Views[_viewIndex].Name}: " +
                $"waiting for visible chunks {ready}/{visible}.");
            _lastProgressAt = now;
        }
    }

    private void BeginCapture(long now, TerrainHexMode mode)
    {
        material.SetHexMode(mode, "benchmark");
        var hexLabel = mode switch
        {
            TerrainHexMode.AllMaterials => "hex-all",
            TerrainHexMode.GrassOnly => "hex-grass",
            _ => "hex-off"
        };
        _performance.BeginCapture($"{Views[_viewIndex].Name}/{hexLabel}");
        _stage = Stage.Capturing;
        _stageStartedAt = now;
    }

    private double Elapsed(long now) =>
        Stopwatch.GetElapsedTime(_stageStartedAt, now).TotalSeconds;

    private void Finish(bool cancelled = false)
    {
        if (_stage == Stage.Capturing)
        {
            if (cancelled)
            {
                _performance.CancelCapture();
            }
            else
            {
                _performance.EndCapture();
            }
        }

        _stage = Stage.Completed;
        streaming.IsPaused = false;
        material.SetHexMode(TerrainHexMode.AllMaterials, "benchmark complete");
        World.Get<InspectCameraController3D>(_camera).IsEnabled =
            _originalControllerEnabled;
        Context.InputContexts.Reset(InputContexts.InspectViewport);
        Console.WriteLine("[benchmark] Complete. Inspect camera control restored; " +
            "F11 can now be toggled normally.");
    }

    private readonly record struct BenchmarkView(
        string Name,
        Vector2 XZ,
        float HeightAboveTerrain,
        float YawDegrees,
        float PitchDegrees);

    private enum Stage
    {
        StartupDelay,
        WaitingForMeshes,
        Settling,
        Capturing,
        Completed
    }
}
