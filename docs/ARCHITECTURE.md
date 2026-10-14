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
the hook the future physics system will use to remove a Jitter body when its
`PhysicsBody` component or owning entity is removed.

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

## Physics integration contract

Jitter has not been added yet. When it is integrated, entities with a physics
body will follow these ownership rules:

- dynamic bodies receive forces, impulses, or velocities through the physics
  API;
- kinematic bodies receive a target pose or velocity through the physics API;
- static bodies keep their initial pose and move only through an explicit rare
  reposition/teleport operation;
- after each fixed physics step, the physics system writes body position and
  rotation to `Transform3D`;
- `Transform3D.Scale` is visual and does not resize a physics shape.

Game systems must not write `Transform3D.Position` or `Rotation` directly for an
entity that has a physics body. Terrain-specific height, voxel, or density data
will be introduced with the terrain feature rather than in the general renderer.
