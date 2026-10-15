using System;
using Microsoft.Xna.Framework;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Physics;
using MonoGame_project.Engine.Systems;
using MonoGame_project.Engine.Time;

namespace MonoGame_project.Game;

/// <summary>Temporary scene behavior used to verify kinematic physics.</summary>
internal sealed class KinematicPlatformTestSystem : IFixedUpdateSystem
{
    public void FixedUpdate(World world, in FixedTime time)
    {
        var totalSeconds = (float)time.Total.TotalSeconds;
        var commands = world.GetResource<PhysicsCommands>();

        foreach (var entity in world.Query<KinematicPlatformTestMotion>())
        {
            var motion = world.Get<KinematicPlatformTestMotion>(entity);
            var offset = MathF.Sin(totalSeconds * motion.AngularSpeed) * motion.Distance;
            commands.SetKinematicTarget(
                entity,
                motion.Center + Vector3.Right * offset,
                Quaternion.Identity);
        }
    }
}

internal struct KinematicPlatformTestMotion : IComponent
{
    public KinematicPlatformTestMotion(Vector3 center, float distance, float angularSpeed)
    {
        Center = center;
        Distance = distance;
        AngularSpeed = angularSpeed;
    }

    public Vector3 Center;
    public float Distance;
    public float AngularSpeed;
}
