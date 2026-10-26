using Microsoft.Xna.Framework;
using MonoGame_project.Ecs;

namespace MonoGame_project.Engine.Rendering.ThreeD;

/// <summary>
/// Opt-in free-flight and orbit settings for inspecting a scene through an active Camera3D.
/// This component is not intended for character or vehicle cameras.
/// </summary>
public struct InspectCameraController3D : IComponent
{
    private float _moveSpeed;
    private float _mouseSensitivity;
    private float _pitchLimit;
    private float _orbitDistance;
    private bool _isEnabled;

    public InspectCameraController3D()
        : this(
            moveSpeed: 8f,
            mouseSensitivity: 0.0025f,
            pitchLimit: MathHelper.ToRadians(89f))
    {
    }

    public InspectCameraController3D(
        float moveSpeed,
        float mouseSensitivity,
        float pitchLimit,
        bool isEnabled = true,
        float orbitDistance = 10f)
    {
        _moveSpeed = ValidateMoveSpeed(moveSpeed);
        _mouseSensitivity = ValidateMouseSensitivity(mouseSensitivity);
        _pitchLimit = ValidatePitchLimit(pitchLimit);
        _orbitDistance = ValidateOrbitDistance(orbitDistance);
        _isEnabled = false;
        IsEnabled = isEnabled;
    }

    public float MoveSpeed
    {
        readonly get => _moveSpeed;
        set => _moveSpeed = ValidateMoveSpeed(value);
    }

    public float MouseSensitivity
    {
        readonly get => _mouseSensitivity;
        set => _mouseSensitivity = ValidateMouseSensitivity(value);
    }

    public float PitchLimit
    {
        readonly get => _pitchLimit;
        set => _pitchLimit = ValidatePitchLimit(value);
    }

    /// <summary>Initial distance from the selected entity when orbit mode starts.</summary>
    public float OrbitDistance
    {
        readonly get => _orbitDistance;
        set => _orbitDistance = ValidateOrbitDistance(value);
    }

    public bool IsEnabled
    {
        readonly get => _isEnabled;
        set
        {
            if (value)
            {
                ValidateMoveSpeed(_moveSpeed);
                ValidateMouseSensitivity(_mouseSensitivity);
                ValidatePitchLimit(_pitchLimit);
                ValidateOrbitDistance(_orbitDistance);
            }

            _isEnabled = value;
        }
    }

    private static float ValidateMoveSpeed(float value)
    {
        if (!float.IsFinite(value) || value < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MoveSpeed),
                value,
                "Inspect camera move speed must be finite and non-negative.");
        }

        return value;
    }

    private static float ValidateMouseSensitivity(float value)
    {
        if (!float.IsFinite(value) || value < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MouseSensitivity),
                value,
                "Inspect camera mouse sensitivity must be finite and non-negative.");
        }

        return value;
    }

    private static float ValidatePitchLimit(float value)
    {
        if (!float.IsFinite(value) || value <= 0f || value >= MathHelper.PiOver2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PitchLimit),
                value,
                "Inspect camera pitch limit must be between 0 and PI/2 radians.");
        }

        return value;
    }

    private static float ValidateOrbitDistance(float value)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(OrbitDistance),
                value,
                "Inspect camera orbit distance must be finite and positive.");
        }

        return value;
    }
}
