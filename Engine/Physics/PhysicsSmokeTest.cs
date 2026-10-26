using System;
using Microsoft.Xna.Framework;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Physics.Components;
using MonoGame_project.Engine.Rendering.ThreeD;
using MonoGame_project.Engine.Terrain;

namespace MonoGame_project.Engine.Physics;

/// <summary>Small headless regression test for Jolt physics.</summary>
internal static class PhysicsSmokeTest
{
    public static void Run()
    {
        using var world = new World();
        var left = world.CreateEntity();
        var right = world.CreateEntity();
        var sphere = world.CreateEntity();
        var platform = world.CreateEntity();
        static float Height(float x, float z) => x * 0.05f + z * 0.02f;
        var leftHeights = new TerrainHeightField(0, 0, 128, 1f, Height);
        var rightHeights = new TerrainHeightField(1, 0, 128, 1f, Height);
        var leftTransform = new Transform3D(new Vector3(leftHeights.OriginX, 0f, 0f));
        var rightTransform = new Transform3D(new Vector3(rightHeights.OriginX, 0f, 0f));
        var groundAtSeam = Height(128f, 64f);
        var sphereTransform = new Transform3D(new Vector3(128f, groundAtSeam + 5f, 64f));
        var platformTransform = new Transform3D(new Vector3(10f, 10f, 10f));
        var sphereSettings = new PhysicsBody3D(PhysicsMotionType.Dynamic);
        var platformSettings = new PhysicsBody3D(PhysicsMotionType.Kinematic);
        var sphereCollider = PhysicsColliderDefinition.From(new SphereCollider3D(1f));
        var platformCollider = PhysicsColliderDefinition.From(new BoxCollider3D(new Vector3(2f)));

        foreach (var useMultithreading in new[] { false, true })
        {
            var variant = $"Jolt (multithreading={useMultithreading})";
            var events = new PhysicsCollisionEvents();
            var settings = PhysicsSettings.Default with
            {
                UseMultithreading = useMultithreading
            };
            using var simulation = new JoltPhysicsSimulation(settings, events);
            simulation.EnsureTerrainBody(left, leftTransform, new TerrainCollider3D(leftHeights));
            simulation.EnsureTerrainBody(right, rightTransform, new TerrainCollider3D(rightHeights));
            simulation.EnsureBody(sphere, sphereTransform, sphereSettings, sphereCollider);
            simulation.EnsureBody(platform, platformTransform, platformSettings, platformCollider);

            var contactStarted = false;
            for (var i = 0; i < 120; i++)
            {
                simulation.Step(1f / 60f);
                events.Publish();
                contactStarted |= events.Started.Count > 0;
            }

            if (!contactStarted)
                throw new InvalidOperationException($"{variant}: terrain contact event was not emitted.");

            if (!simulation.TryGetPose(sphere, out var position, out _) ||
                position.Y < groundAtSeam + 0.7f ||
                position.Y > groundAtSeam + 1.4f)
            {
                throw new InvalidOperationException(
                    $"{variant}: sphere did not land at the seam (Y={position.Y}).");
            }

            var platformTarget = new Vector3(11f, 10f, 10f);
            simulation.SetKinematicTarget(platform, platformTarget, Quaternion.Identity, 1f / 60f);
            simulation.Step(1f / 60f);
            if (!simulation.TryGetPose(platform, out var platformPosition, out _) ||
                Vector3.Distance(platformPosition, platformTarget) > 0.1f)
            {
                throw new InvalidOperationException($"{variant}: kinematic target failed.");
            }

            simulation.StopKinematic(platform);
            var oldCollider = sphereCollider;
            sphereCollider = PhysicsColliderDefinition.From(new SphereCollider3D(0.5f));
            simulation.EnsureBody(sphere, new Transform3D(position), sphereSettings, sphereCollider);
            simulation.EnsureBody(sphere, new Transform3D(position), sphereSettings, oldCollider);
            sphereCollider = oldCollider;

            var primitiveColliders = new[]
            {
                PhysicsColliderDefinition.From(new BoxCollider3D(new Vector3(1f))),
                PhysicsColliderDefinition.From(new SphereCollider3D(0.5f)),
                PhysicsColliderDefinition.From(new CylinderCollider3D(0.5f, 2f)),
                PhysicsColliderDefinition.From(new CapsuleCollider3D(0.5f, 1f)),
                PhysicsColliderDefinition.From(new ConeCollider3D(0.5f, 2f))
            };
            foreach (var primitiveCollider in primitiveColliders)
            {
                var primitive = world.CreateEntity();
                simulation.EnsureBody(primitive,
                    new Transform3D(new Vector3(30f, 10f, 30f)),
                    sphereSettings, primitiveCollider);
                simulation.SetMotionType(primitive, PhysicsMotionType.Static);
                simulation.SetMotionType(primitive, PhysicsMotionType.Dynamic);
                simulation.ApplyForce(primitive, new Vector3(0f, 1f, 0f));
                simulation.Step(1f / 60f);
                if (!simulation.RemoveBody(primitive))
                    throw new InvalidOperationException($"{variant}: primitive cleanup failed.");
            }

            if (!simulation.RemoveBody(platform) || !simulation.RemoveBody(sphere) ||
                !simulation.RemoveBody(left) || !simulation.RemoveBody(right) ||
                simulation.BodyCount != 0)
            {
                throw new InvalidOperationException($"{variant}: body cleanup failed.");
            }

            Console.WriteLine($"[physics-smoke] {variant}: seam sphere Y={position.Y:F3}, " +
                $"contact started={contactStarted}, kinematic target, five primitive " +
                "colliders, motion changes and cleanup passed.");
        }
    }
}
