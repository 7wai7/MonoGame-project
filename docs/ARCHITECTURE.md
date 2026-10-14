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

## 3D rendering

An active camera entity has `Transform3D` and `Camera3D`. A renderable entity
has `Transform3D` and `MeshRenderer`. `MeshRenderSystem` finds the camera,
creates its view/projection matrices, then draws every visible mesh using depth
testing and `BasicEffect` lighting. `ProceduralMeshFactory` currently creates a
box and is the starting point for future procedural terrain meshes.
