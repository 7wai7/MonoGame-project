# Architecture

Detailed Ukrainian diagrams and step-by-step explanations are available in the
[visual architecture guide](diagrams/README.md). It covers the engine lifecycle,
main loop, ECS storage and queries, Jolt synchronization, body lifecycle, and
render interpolation.

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
the hook `PhysicsSystem` uses to remove a Jolt body immediately when its
`PhysicsBody3D`, transform, or owning entity is removed.

## Input routing

`InputService` reads one keyboard, mouse, and gamepad snapshot before scene
systems update. It also owns the platform details of relative mouse input:
focus events, cursor visibility, SDL relative-mouse mode, and mouse delta. It
does not decide which gameplay or UI feature should receive that input.

`InputContextStack` makes that routing decision. An `InputContext` is only an
immutable description of an input layer; it is not a system and does not own
the cursor. The context at the top of the stack determines whether the cursor
is `Free` or `Relative`. A context with `BlocksLowerInput` also prevents systems
associated with contexts below it from acting on the same snapshot. Systems
call `CanReceiveInput(...)` before interpreting input for their context.

The bootstrap scene resets the stack to `InspectViewport`. `PauseMenuSystem`
handles each Escape key press and uses `PauseMenu` to release the cursor and
block inspect camera input while its Gum view is visible:

```csharp
context.InputContexts.Push(InputContexts.PauseMenu); // Free cursor; camera blocked.
context.InputContexts.Pop(InputContexts.PauseMenu);  // Inspect viewport and mouse capture resume.
```

The menu's exit button requests application shutdown through
`ApplicationControl`; `MainGame` performs the actual `Exit()` call after Gum
processes the click. The menu currently overlays the scene; it does not pause
the physics simulation.

`GameUiTheme` creates styled Gum overlays, panels, and buttons with caller-supplied
sizes, text, and actions. The scene stores one theme as a `World` resource, so
its views share the same GPU textures. `PauseMenuView` only arranges these
elements; the world disposes the theme after its systems and views shut down.

The stack selects a mouse mode; `InputService` owns the actual cursor capture.
When an active window switches to `Free`, it releases capture and places the
cursor at the center of the game window.
Switching from `Free` to `Relative` captures the mouse immediately while the
game window has active input, including when the menu closes with Escape. At
initial startup there is no active input yet, and merely regaining focus does
not switch modes, so those cases still require a left click inside the window.
Scene changes clear the entire stack before the new scene initializes its root
context.

## 3D rendering

An active camera entity has `Transform3D` and `Camera3D`. A renderable entity
has `Transform3D` and `MeshRenderer`. `MeshRenderSystem` finds the camera,
creates its view/projection matrices, then draws every visible mesh using depth
testing and `BasicEffect` lighting. A `MeshRenderer` can optionally reference a
diffuse texture; the scene that loaded it owns its lifetime.

`InspectCameraController3D` is an opt-in camera controller for examining a
scene and is independent from future character or vehicle controls. While it is
enabled on the active camera, the mouse controls its view, WASD moves
horizontally, and Q/E moves down or up. A left click inside the game window
activates this input and captures the cursor; losing focus releases it and
requires another click.
After selecting a mesh, C switches between free flight and orbiting that
entity at the controller's initial `OrbitDistance` (10 units by default).
In orbit mode the mouse changes the viewing angle and the wheel changes the
distance; WASD/Q/E do not move the attached camera. Holding Left Ctrl releases
the cursor for the inspector and reserves the wheel for its component list.
`InspectCameraControlSystem` updates input during variable-rate update and
follows the target's interpolated pose before rendering. It has no physics
body, gravity, or collision response.

`MeshData` contains CPU-side vertices and indices and does not need a
`GraphicsDevice`. `ProceduralMeshFactory` creates box, sphere, cylinder,
capsule, and cone data. Its dimensions follow the matching collider semantics:
cylinder/capsule/cone use local Y, capsule length excludes its hemispheres, and
the cone origin is one quarter-height above its base. `Mesh` uploads the result to GPU
vertex/index buffers and owns only those GPU resources. `MeshData` is a general
rendering type; physics must not depend on it.

## Physics

The engine uses Jolt Physics. Gameplay and rendering code use MonoGame types
and ECS entities; native Jolt types remain private to `JoltPhysicsSimulation`.
`PhysicsSettings.UseMultithreading` controls the Jolt worker threads and is
disabled by default.

A primitive physical entity requires `Transform3D`, `PhysicsBody3D`, and exactly
one supported primitive collider component. For example, a physical box is
created like this:

```csharp
var cube = World.CreateEntity();
World.Add(cube, new Transform3D(new Vector3(0f, 4f, 0f)));
World.Add(cube, new PhysicsBody3D(PhysicsMotionType.Dynamic, mass: 1f));
World.Add(cube, new BoxCollider3D(Vector3.One));
```

The physics files have these responsibilities:

- `PhysicsBody3D` stores engine-facing body settings: motion type, mass,
  friction, restitution, and gravity participation.
- Collider components describe one primitive shape: `BoxCollider3D`,
  `SphereCollider3D`, `CylinderCollider3D`, `CapsuleCollider3D`, or
  `ConeCollider3D`. Cylinder, capsule, and cone use their local Y axis. Capsule
  `Length` is only its cylindrical section; total height is
  `Length + 2 * Radius`.
- `PhysicsPose3D` is internal runtime interpolation state maintained
  automatically by the physics system. Gameplay code does not add or edit it.
  The `PhysicsSystem` automatically adds it to every entity with a physical body
  and removes it itself.
- `JoltPhysicsSimulation` owns the native Jolt world, body mappings, collision
  callbacks, MonoGame/Jolt type conversion, and one heightfield per active
  terrain chunk.
- `PhysicsSystem` creates and removes native bodies, executes commands, steps
  Jolt once per fixed tick, publishes collision changes, and copies resulting
  poses back to ECS.
- `PhysicsCommands` is the public queue for forces, impulses, velocity changes,
  teleports, and motion-type changes.
- `PhysicsCollisionEvents` exposes collisions started or ended by the most
  recent fixed step.
- `PhysicsDebugRenderSystem` optionally draws all supported colliders as green
  wireframes.

Collider dimensions can be edited at runtime. Jolt replaces the shape
on the existing body when needed, preserving the body's identity; mass and
inertia are recalculated for dynamic bodies.

### Pose ownership

- A dynamic body's pose is controlled by Jolt. Move it with a force, impulse,
  or velocity command.
- A kinematic body's pose is controlled with
  `PhysicsCommands.SetKinematicTarget`. The physics system converts the target
  into Jolt kinematic motion before the physics step, so it still
  interacts with dynamic bodies.
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

Queued commands run immediately before the next physics step. A kinematic body
stops when a fixed tick has no new target command. A future gameplay
fixed-update system that creates kinematic targets should run before
`PhysicsSystem`; a system that consumes `PhysicsCollisionEvents` should run
after it. Rendering systems run in the separate Draw phase.

Removing `PhysicsBody3D`, `Transform3D`, or the whole entity removes its native
body synchronously through the ECS lifecycle notification. Collider removal is
synchronized at the start of the next fixed tick, which allows gameplay to
replace one collider component with another between ticks without destroying
the body. At synchronization time, an entity with `PhysicsBody3D` must still
have exactly one supported collider. If an explicitly removed body was touching
another body, an ended-collision event is generated.

## Terrain

`TerrainHeightField` is immutable CPU-side height data for one square chunk. It
also samples a one-cell border around the chunk so `TerrainMeshFactory`
computes the same edge normals as its neighbors. `TerrainSurfaceBuilder`
creates an adaptive render `TerrainSurface`: nearly planar areas use fewer triangles,
while areas with more height variation retain detail. `maxHeightError` is the
maximum vertical difference from the original full-resolution triangulation.
Zero permits only exact planar simplification; a larger value removes more
geometry at the cost of precision.
Every sample along a chunk's outer edge is retained, and shared internal edges
are stitched, so independently simplified neighbors have no gaps. The renderer
turns its surface into `MeshData` and a GPU `Mesh`; physics builds a Jolt
heightfield directly from the original height samples, never from `MeshData`.
The near render LOD uses the exact source grid instead of
running adaptive simplification at an almost-zero error threshold. Middle and
far LODs use errors of 1 and 8 world units. Collision uses every source height
sample regardless of the render LOD. Far render
patches are additionally limited to 16×16 source cells, so even very flat
areas cannot collapse into one large triangle fan. The near LOD remains the
full source grid. The F3 debug wireframe generates triangle-edge indices lazily
from the source grid; Jolt itself uses its heightfield representation.

`TerrainRenderStreamingSystem` builds nine full-resolution meshes near the starting
camera before the first frame, then prioritizes missing visible chunks and
nearby LOD upgrades. One CPU mesh is generated at a time on a background
worker; GPU buffers and ECS `MeshRenderer` changes happen on the game thread.
Until a new LOD is ready, the previous mesh stays visible. Every LOD retains
the full source-grid border, so neighboring meshes share edge vertices.
Terrain outside the view is not built eagerly and may appear progressively
after startup or a rapid camera move. Loaded meshes are frustum-culled at draw.

`TerrainCollider3D` creates a static Jolt body for each active collision
chunk. It does not require `PhysicsBody3D`; terrain must have unit scale and
remain upright. `TerrainColliderStreamingSystem` adds this component within
one chunk of the active camera and each moving physics body, and removes it
elsewhere. The physics system registers and removes the native body through
the normal ECS component lifecycle. The render mesh does not depend on whether
the chunk currently has collision.
Multiple native contacts are aggregated into a single entity-pair collision
event. Jolt uses one `HeightFieldShape` per active chunk from the original
height samples, independently of render mesh LOD.

`dotnet run --project MonoGame-project.csproj -c Release -- --physics-smoke`
runs a short headless Jolt regression with and without worker threads. It checks a sphere landing
across a sloped chunk seam, contact events, kinematic movement, all five
primitive shapes, motion-type changes, and body cleanup. The smoke test is not
a full-world performance benchmark.

The bootstrap scene loads the supplied 4096×4096 OpenEXR height and diffuse
images from `Assets/Terrains/Rugged Terrain with Rivers`. It maps the whole image
to a 5,000×5,000-world-unit terrain and multiplies raw EXR height values by
2,600 world units, without normalizing the image's minimum and maximum. These
numbers are scene assumptions, not verified dimensions of the downloaded asset;
adjust `TerrainWorldExtent`, `TerrainHeightScale`, and `TerrainHeightOffset` in
`BootstrapScene` after choosing a suitable gameplay scale.
The scene creates 32×32 chunks with 128×128 height cells each (about 1.22 world
units per cell). Render meshes and physics heightfields use the same CPU
heights. The camera's far plane and inspect movement speed are also increased
for this world scale.
The diffuse image is converted from linear EXR RGB to an sRGB 8-bit texture
with mipmaps. Each chunk's UVs refer to the whole image, not a repeated tile.
The source image resolution alone does not set heightfield sampling: the
chunk count and `TerrainCellsPerChunk` determine how many height samples
physics uses. Increasing the cell count retains more height detail in the
collider; render LOD error only changes the visible mesh.

`OpenExrScanlineReader` supports the provided single-part, uncompressed,
full-resolution float EXR channels. MonoGame's standard texture importer does
not import EXR, so the two source files are copied unchanged beside the built
game and loaded from there. Other EXR compression, tiling, channel formats, or
multipart files fail with a clear exception. The graphics profile is HiDef
because the full 4096×4096 diffuse texture exceeds Reach's texture-size limit.
Collision and render meshes stream independently. Texture blending and
vegetation are not implemented; the diffuse image still stretches over the
whole world, so nearby texture detail remains limited. Inspect picking can
select a loaded terrain chunk but does not draw its enlarged-mesh outline,
even when its collision component has streamed out. Press F3 to toggle the
terrain collider's CPU-sampled triangle wireframe in the existing physics
debug renderer. With Jolt this visualizes the source height grid, not Jolt's
internal compressed representation. The wireframe is slightly raised to remain
visible over the terrain surface; the actual collider is not moved.
