# 1. Загальна архітектура

## Карта залежностей

~~~mermaid
flowchart TB
    Program["Program.cs<br/>створює та запускає MainGame"]

    subgraph Host["MonoGame host"]
        MainGame["MainGame<br/>вікно, GraphicsDevice, lifecycle"]
        MonoLoop["Microsoft.Xna.Framework.Game<br/>платформний цикл"]
    end

    subgraph Engine["Engine"]
        GameEngine["GameEngine<br/>час, сцена, main loop"]
        EngineContext["EngineContext<br/>GraphicsDevice, Content, Window, Services"]
        FrameTime["FrameTime"]
        FixedTime["FixedTime"]
        RenderContext["RenderContext"]
    end

    subgraph Scene["Поточна GameScene"]
        Bootstrap["BootstrapScene<br/>composition root поточної сцени"]
        Scheduler["SystemScheduler<br/>порядок систем"]
        World["World<br/>сутності, компоненти, ресурси"]
    end

    subgraph Systems["Системи"]
        GameplayMotion["Умовна gameplay-система<br/>керування kinematic body"]
        PhysicsSystem["PhysicsSystem"]
        MeshRender["MeshRenderSystem"]
        DebugRender["PhysicsDebugRenderSystem"]
    end

    subgraph Physics["Physics boundary"]
        Commands["PhysicsCommands"]
        Simulation["PhysicsSimulation"]
        Collisions["PhysicsCollisionEvents"]
        Jitter["Jitter World + RigidBody"]
    end

    subgraph Rendering["3D rendering"]
        Camera["Camera3D"]
        Transform["Transform3D"]
        MeshRenderer["MeshRenderer"]
        Mesh["Mesh<br/>GPU buffers"]
        GPU["GraphicsDevice"]
    end

    Program --> MainGame
    MonoLoop --> MainGame
    MainGame --> GameEngine
    MainGame --> EngineContext
    EngineContext --> GameEngine
    GameEngine --> Bootstrap
    GameEngine --> FrameTime
    GameEngine --> FixedTime
    GameEngine --> RenderContext
    Bootstrap --> Scheduler
    Bootstrap --> World
    Scheduler -. "можлива майбутня система" .-> GameplayMotion
    Scheduler --> PhysicsSystem
    Scheduler --> MeshRender
    Scheduler --> DebugRender
    GameplayMotion -. "SetKinematicTarget" .-> Commands
    PhysicsSystem <--> World
    PhysicsSystem --> Commands
    PhysicsSystem --> Simulation
    PhysicsSystem --> Collisions
    Simulation --> Jitter
    MeshRender --> World
    MeshRender --> Camera
    MeshRender --> Transform
    MeshRender --> MeshRenderer
    MeshRenderer --> Mesh
    Mesh --> GPU
    DebugRender --> GPU

    classDef host fill:#dbeafe,stroke:#2563eb,color:#172554
    classDef engine fill:#e0e7ff,stroke:#4f46e5,color:#1e1b4b
    classDef ecs fill:#fef3c7,stroke:#d97706,color:#451a03
    classDef physics fill:#fee2e2,stroke:#dc2626,color:#450a0a
    classDef render fill:#dcfce7,stroke:#16a34a,color:#052e16

    class Program,MainGame,MonoLoop host
    class GameEngine,EngineContext,FrameTime,FixedTime,RenderContext,Bootstrap,Scheduler engine
    class World ecs
    class GameplayMotion,PhysicsSystem,Commands,Simulation,Collisions,Jitter physics
    class MeshRender,DebugRender,Camera,Transform,MeshRenderer,Mesh,GPU render
~~~

## Що означає кожен рівень

### 1. Program і MainGame

**Program.cs** створює один об'єкт **MainGame** і викликає його **Run()**.
Подальшим циклом керує MonoGame.

**MainGame** є адаптером між MonoGame та нашим рушієм. Він:

- створює вікно і GraphicsDeviceManager;
- формує EngineContext;
- передає Initialize, LoadContent, Update, Draw і UnloadContent у GameEngine;
- перевіряє Escape і завершує програму;
- володіє SpriteBatch, хоча основний 3D-рендеринг його поки не використовує.

### 2. GameEngine

**GameEngine** не знає правил конкретної гри. Він:

- володіє активною GameScene;
- перетворює час MonoGame на FrameTime та FixedTime;
- накопичує час для стабільних фізичних кроків;
- безпечно перемикає сцени;
- передає сцені команди Update, FixedUpdate і Draw.

### 3. GameScene

Кожна сцена має власні:

- **World** — усі ECS-сутності, компоненти й ресурси сцени;
- **SystemScheduler** — впорядкований список систем.

Тому при майбутньому переході, наприклад, з меню на ігровий світ можна
замінити всю сцену разом із її даними та системами.

### 4. SystemScheduler

Scheduler зберігає системи за числовим **order**. Якщо order однаковий,
залишається порядок додавання. У кожній фазі він викликає тільки системи, що
реалізують відповідний інтерфейс:

| Фаза | Інтерфейс | Призначення |
|---|---|---|
| Initialize | IGameSystem | створення довгоживучого стану |
| LoadContent | IContentSystem | завантаження GPU/content ресурсів |
| Update | IUpdateSystem | логіка зі змінним delta time |
| FixedUpdate | IFixedUpdateSystem | стабільна симуляція, зокрема фізика |
| Draw | IRenderSystem | рендеринг |
| Shutdown | IGameSystem | відписки та звільнення стану |

### 5. World

World є центром даних, але не містить ігрової поведінки. У ньому знаходяться:

- легкі Entity handle з Id і Generation;
- окремий ComponentPool для кожного типу компонента;
- ресурси сцени, наприклад PhysicsCommands;
- lifecycle events для синхронного прибирання зовнішніх об'єктів.

### 6. Межа фізики

Ігровий код не працює з RigidBody напряму:

1. Компоненти описують бажану конфігурацію тіла.
2. PhysicsCommands описує одноразові дії.
3. PhysicsSystem синхронізує ECS з PhysicsSimulation.
4. PhysicsSimulation володіє справжнім Jitter World і RigidBody.
5. Після кроку результат повертається в Transform3D і PhysicsPose3D.

Ця межа не дозволяє типам Jitter поширитися по всьому ігровому коду.

### 7. Межа рендерингу

MeshRenderSystem читає ECS, але не змінює фізику:

1. знаходить активні Transform3D + Camera3D;
2. знаходить видимі Transform3D + MeshRenderer;
3. за потреби бере інтерпольовану PhysicsPose3D;
4. задає BasicEffect матриці World, View і Projection;
5. просить Mesh виконати DrawIndexedPrimitives через GraphicsDevice.

## Головне правило залежностей

Ігрова логіка працює через ECS-компоненти та engine API. Jitter і GPU-буфери
залишаються деталями відповідно PhysicsSimulation та Mesh. Завдяки цьому
майбутні gameplay-системи не повинні знати, як усередині влаштована фізика або
відправлення вершин на відеокарту.

## Відповідні файли

- [Program.cs](../../Program.cs)
- [MainGame.cs](../../MainGame.cs)
- [GameEngine.cs](../../Engine/GameEngine.cs)
- [GameScene.cs](../../Engine/Scenes/GameScene.cs)
- [SystemScheduler.cs](../../Engine/Systems/SystemScheduler.cs)
- [World.cs](../../Ecs/World.cs)
- [BootstrapScene.cs](../../Game/BootstrapScene.cs)
