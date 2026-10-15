# Architecture

`MainGame` is only the MonoGame/DesktopGL host. It configures the window and
forwards lifecycle calls to `GameEngine`.

`GameEngine` owns the main loop:

1. one variable-rate update (`IUpdateSystem`);
2. zero or more fixed 60 Hz simulation ticks (`IFixedUpdateSystem`);
3. one render pass (`IRenderSystem`) with an interpolation alpha.

Each `GameScene` owns an isolated ECS `World` and a `SystemScheduler`. Put future
game composition in `Game/BootstrapScene.cs`, and split components and systems
by gameplay feature when those features are introduced.

## ECS usage

```csharp
public struct Position : IComponent
{
    public Vector2 Value;
}

public struct Velocity : IComponent
{
    public Vector2 Value;
}

var entity = World.CreateEntity();
World.Add(entity, new Position());
World.Add(entity, new Velocity { Value = Vector2.UnitX });

foreach (var current in World.Query<Position, Velocity>())
{
    ref var position = ref World.Get<Position>(current);
    ref var velocity = ref World.Get<Velocity>(current);
    position.Value += velocity.Value;
}
```

Queries are allocation-free and iterate the smallest matching sparse-set pool.
Do not add/remove entities or components inside a query. Queue structural changes
in `EntityCommandBuffer` and call `Playback(World)` after iteration instead.

Resources are world-scoped non-component services or state. Register them with
`World.SetResource(...)` and retrieve them with `World.GetResource<T>()`.

`World.EntityDestroying` and `World.ComponentRemoving` are synchronous lifecycle
notifications. They run before ECS data is deleted, so a subscriber can still
read the entity and component being removed. A lifecycle handler may clean up
external resources, but it must not make another structural ECS change. This is
the hook `PhysicsSystem` uses to remove a Jitter body immediately when its
`PhysicsBody3D`, collider, transform, or owning entity is removed.

## 3D rendering

An active camera entity has `Transform3D` and `Camera3D`. A renderable entity
has `Transform3D` and `MeshRenderer`. `MeshRenderSystem` finds the camera,
creates its view/projection matrices, then draws every visible mesh using depth
testing and `BasicEffect` lighting.

`MeshData` contains CPU-side vertices and indices and does not need a
`GraphicsDevice`. `ProceduralMeshFactory` creates this data. `Mesh` uploads it
to GPU vertex/index buffers and owns only those GPU resources. `MeshData` is a
general rendering type; physics must not depend on it. A collider may be built
from the same source geometry through a separate converter when that is useful.

## Physics

The engine uses Jitter 2 behind an engine-owned boundary. Gameplay and rendering
code use MonoGame types and ECS entities; native `RigidBody`, Jitter math types,
and the Jitter `World` remain private to `PhysicsSimulation`.

A physical box requires three components:

```csharp
var cube = World.CreateEntity();
World.Add(cube, new Transform3D(new Vector3(0f, 4f, 0f)));
World.Add(cube, new PhysicsBody3D(PhysicsMotionType.Dynamic, mass: 1f));
World.Add(cube, new BoxCollider3D(Vector3.One));
```

The physics files have these responsibilities:

- `PhysicsBody3D` stores engine-facing body settings: motion type, mass,
  friction, restitution, and gravity participation.
- `BoxCollider3D` stores the collision box size. More collider component types
  can be added later without changing `MeshData`.
- `PhysicsPose3D` is internal runtime interpolation state maintained
  automatically by the physics system. Gameplay code does not add or edit it.
  The `PhysicsSystem` automatically adds it to every entity with a physical body
  and removes it itself.
- `PhysicsSimulation` owns the Jitter world, native bodies, entity-to-body
  mapping, collision callbacks, and MonoGame/Jitter type conversion.
- `PhysicsSystem` creates and removes native bodies, executes commands, steps
  Jitter once per fixed tick, publishes collision changes, and copies resulting
  poses back to ECS.
- `PhysicsCommands` is the public queue for forces, impulses, velocity changes,
  teleports, and motion-type changes.
- `PhysicsCollisionEvents` exposes collisions started or ended by the most
  recent fixed step.
- `PhysicsDebugRenderSystem` optionally draws box colliders as green wireframes.

### Pose ownership

- A dynamic body's pose is controlled by Jitter. Move it with a force, impulse,
  or velocity command.
- A kinematic body's pose is controlled with
  `PhysicsCommands.SetKinematicTarget`. The physics system converts the target
  into linear and angular velocity before the Jitter step, so it still
  interacts correctly with dynamic bodies.
- A static body keeps its initial pose. Use an explicit `Teleport` command for
  a rare reposition rather than editing `Transform3D`.
- After every fixed step, physics writes position and rotation to `Transform3D`.
  Rendering interpolates the previous and current `PhysicsPose3D`, which keeps
  movement smooth when render FPS differs from the fixed 60 Hz simulation.
- `Transform3D.Scale` is visual only. Collider dimensions come from the collider
  component and do not silently change with the mesh scale.

Therefore gameplay systems must not directly edit `Transform3D.Position` or
`Rotation` on an entity with `PhysicsBody3D`.

### Commands and collisions

Retrieve the scene resources from `World` inside a system:

```csharp
var commands = world.GetResource<PhysicsCommands>();
commands.ApplyImpulse(cube, new Vector3(0f, 5f, 0f));
commands.SetKinematicTarget(platform, targetPosition, targetRotation);

var collisions = world.GetResource<PhysicsCollisionEvents>();
foreach (var collision in collisions.Started)
{
    // React to a collision that began during this fixed tick.
}
```

Queued commands run immediately before the next Jitter step. A kinematic body
stops when a fixed tick has no new target command. A fixed-update system that
creates kinematic targets should run before `PhysicsSystem`; a
system that consumes `PhysicsCollisionEvents` should run after it. The bootstrap
scene currently uses order `-100` for test motion, `0` for physics, `1000` for
mesh rendering, and `1100` for collider debug rendering.

Removing `PhysicsBody3D`, `BoxCollider3D`, `Transform3D`, or the whole entity
removes its native body synchronously through the ECS lifecycle notification.
If that body was touching another body, an ended-collision event is generated
because Jitter itself does not emit one for explicit body removal.

`MeshData` remains rendering geometry, not physics geometry. Terrain-specific
height, voxel, or density data and matching terrain colliders will be introduced
with the terrain feature rather than coupled to the general mesh renderer.
