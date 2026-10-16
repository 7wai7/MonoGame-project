# 7. Рендеринг та фізична інтерполяція

Фізика працює з фіксованою частотою 60 Hz, а Draw може викликатися з іншою
частотою. PhysicsPose3D з'єднує ці два часові ритми.

## Потік даних до GPU

~~~mermaid
flowchart LR
    subgraph Physics["FixedUpdate: фізика"]
        J["Jitter RigidBody<br/>нова position + rotation"]
        T["Transform3D<br/>остання завершена pose"]
        P["PhysicsPose3D<br/>Previous + Current"]
        J --> T
        J --> P
    end

    subgraph Render["Draw: рендеринг"]
        Time["RenderContext<br/>InterpolationAlpha"]
        Interpolate["Lerp position<br/>Slerp rotation"]
        WorldMatrix["World matrix"]
        Camera["Camera3D + Transform3D<br/>View + Projection"]
        Effect["BasicEffect"]
        Mesh["Mesh<br/>VertexBuffer + IndexBuffer"]
    end

    P --> Interpolate
    Time --> Interpolate
    Interpolate --> WorldMatrix
    T -. "Scale" .-> WorldMatrix
    WorldMatrix --> Effect
    Camera --> Effect
    Effect --> Mesh
    Mesh --> GPU["GraphicsDevice<br/>DrawIndexedPrimitives"]

    classDef physics fill:#fee2e2,stroke:#dc2626,color:#450a0a
    classDef render fill:#dcfce7,stroke:#16a34a,color:#052e16
    class J,T,P physics
    class Time,Interpolate,WorldMatrix,Camera,Effect,Mesh,GPU render
~~~

## Як оновлюється PhysicsPose3D

На початку кожного physics tick:

~~~text
Previous = Current
~~~

Після Jitter World.Step:

~~~text
Current = нова pose Jitter body
Transform3D = нова pose Jitter body
~~~

У Draw:

~~~text
renderPosition = Lerp(PreviousPosition, CurrentPosition, alpha)
renderRotation = Slerp(PreviousRotation, CurrentRotation, alpha)
~~~

Transform3D.Scale залишається візуальним значенням і додається до world matrix
окремо.

## Часова шкала

~~~mermaid
sequenceDiagram
    participant F0 as Fixed tick N
    participant State as PhysicsPose3D
    participant D1 as Draw alpha 0.25
    participant D2 as Draw alpha 0.70
    participant F1 as Fixed tick N+1

    F0->>State: Previous = 2.0<br/>Current = 3.0
    State-->>D1: Lerp(2.0, 3.0, 0.25)
    Note over D1: намалювати 2.25
    State-->>D2: Lerp(2.0, 3.0, 0.70)
    Note over D2: намалювати 2.70
    F1->>State: Previous = 3.0<br/>Current = 4.0
~~~

Фізичне тіло вже знаходиться у Current = 3.0, але візуально рендериться між
попередньою та поточною позами. Це стандартно додає затримку приблизно одного
physics tick, зате рух не стрибає між дискретними станами.

## Звідки береться alpha

Після виконання всіх можливих fixed ticks в accumulator залишається час, якого
ще недостатньо для наступного tick:

~~~text
alpha = accumulator / fixedTimeStep
~~~

Тому alpha зазвичай лежить у межах від 0 до 1. Перед використанням renderer
додатково обмежує його через Clamp.

| alpha | Візуальна pose |
|---:|---|
| 0.0 | точно Previous |
| 0.25 | чверть шляху |
| 0.5 | середина |
| 0.75 | три чверті шляху |
| 1.0 | точно Current |

## Що робить MeshRenderSystem

~~~mermaid
flowchart TD
    A["Draw(World, RenderContext)"] --> B{"Content завантажений?"}
    B -->|"ні"| Stop["нічого не малювати"]
    B -->|"так"| C["знайти першу активну<br/>Transform3D + Camera3D"]
    C --> D{"Камеру знайдено?"}
    D -->|"ні"| Stop
    D -->|"так"| E["перевірити FOV, clipping planes,<br/>Up та position != target"]
    E --> F["налаштувати GPU states"]
    F --> G["створити View та Projection"]
    G --> H["Query Transform3D + MeshRenderer"]
    H --> I{"Mesh видимий та валідний?"}
    I -->|"ні"| H
    I -->|"так"| J{"Є активна PhysicsPose3D?"}
    J -->|"так"| K["World = interpolated physics pose + scale"]
    J -->|"ні"| L["World = Transform3D.WorldMatrix"]
    K --> M["задати Tint та Alpha"]
    L --> M
    M --> N["BasicEffect pass.Apply"]
    N --> O["Mesh.Draw"]
    O --> H

    classDef render fill:#dcfce7,stroke:#16a34a,color:#052e16
    classDef decision fill:#fef3c7,stroke:#d97706,color:#451a03
    class A,C,E,F,G,H,K,L,M,N,O,Stop render
    class B,D,I,J decision
~~~

Не-фізичні Entity не мають PhysicsPose3D, тому renderer просто використовує
Transform3D.WorldMatrix.

## Камера

Активна камера — це звичайна ECS Entity:

~~~text
Camera Entity
├── Transform3D.Position → положення ока
└── Camera3D
    ├── Target           → куди дивиться
    ├── Up               → напрям верху
    ├── FieldOfView
    ├── NearPlane
    ├── FarPlane
    └── IsActive
~~~

Матриці будуються так:

~~~text
View       = CreateLookAt(position, target, up)
Projection = CreatePerspectiveFieldOfView(fov, aspect, near, far)
~~~

Керування камерою поки не реалізоване: BootstrapScene створює її зі сталою
позицією.

## CPU geometry та GPU Mesh

~~~mermaid
flowchart LR
    Factory["ProceduralMeshFactory"] --> Data["MeshData<br/>CPU vertices + indices"]
    Data --> Upload["new Mesh(GraphicsDevice, data)"]
    Upload --> VB["VertexBuffer на GPU"]
    Upload --> IB["IndexBuffer на GPU"]
    VB --> Draw["DrawIndexedPrimitives"]
    IB --> Draw
~~~

- MeshData можна створювати без GraphicsDevice.
- ProceduralMeshFactory створює box, sphere, cylinder, capsule і cone. Кількість
  сегментів curved shapes задається аргументами фабричних методів.
- Mesh завантажує дані у GPU buffers і володіє ними.
- MeshRenderer пов'язує готовий Mesh з Entity та її tint.
- UnloadContent видаляє MeshRenderer з Entity, а потім Dispose звільняє GPU
  buffers.

Фізичні collider-компоненти не залежать від MeshData або Mesh. Візуальна та
collision geometry можуть збігатися, але це різні відповідальності.

## Debug rendering

PhysicsDebugRenderSystem окремо проходить Entity з кожним підтримуваним
primitive collider і будує зелений wireframe для box, sphere, cylinder,
capsule або cone. Для фізичних Entity він використовує ту саму PhysicsPose3D
interpolation, тому wireframe і mesh залишаються візуально синхронними.

## Відповідні файли

- [MeshRenderSystem.cs](../../Engine/Rendering/ThreeD/MeshRenderSystem.cs)
- [PhysicsDebugRenderSystem.cs](../../Engine/Physics/PhysicsDebugRenderSystem.cs)
- [PhysicsPose3D.cs](../../Engine/Physics/Components/PhysicsPose3D.cs)
- [Transform3D.cs](../../Engine/Rendering/ThreeD/Transform3D.cs)
- [Camera3D.cs](../../Engine/Rendering/ThreeD/Camera3D.cs)
- [Mesh.cs](../../Engine/Rendering/ThreeD/Mesh.cs)
- [MeshData.cs](../../Engine/Rendering/ThreeD/MeshData.cs)
- [ProceduralMeshFactory.cs](../../Engine/Rendering/ThreeD/ProceduralMeshFactory.cs)
