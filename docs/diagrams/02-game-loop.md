# 2. Життєвий цикл та один кадр

## Запуск і завершення

~~~mermaid
sequenceDiagram
    autonumber
    participant P as Program
    participant M as MonoGame / MainGame
    participant E as GameEngine
    participant S as GameScene
    participant SS as SystemScheduler

    P->>M: new MainGame()
    M->>E: new GameEngine(BootstrapScene, fixedStep, maxFrameTime)
    P->>M: Run()
    M->>M: Initialize()
    M->>E: Initialize(EngineContext)
    E->>S: Initialize(context)
    S->>S: ConfigureSystems()
    S->>SS: Initialize(World, context)
    M->>M: base.Initialize()
    Note over M: MonoGame викликає LoadContent
    M->>E: LoadContent()
    E->>S: LoadContent(context)
    S->>SS: LoadContent(ContentManager)

    loop Поки вікно відкрите
        M->>E: Update(gameTime)
        E->>S: Update(FrameTime)
        E->>S: FixedUpdate(FixedTime) 0..N разів
        M->>E: Draw(gameTime, spriteBatch)
        E->>S: Draw(RenderContext)
    end

    M->>E: UnloadContent()
    E->>S: UnloadContent()
    M->>E: Dispose()
    E->>S: Dispose()
    S->>SS: Shutdown()
    S->>S: World.Dispose()
~~~

Важлива деталь: **MainGame.Initialize** спочатку ініціалізує GameEngine і лише
потім викликає base.Initialize. MonoGame може перейти до LoadContent із базової
ініціалізації, тому рушій уже має бути готовим.

## Що відбувається під час Update

~~~mermaid
flowchart TD
    A["MonoGame передає ElapsedGameTime"] --> B{"Час коректний?"}
    B -->|"менше 0"| C["elapsed = 0"]
    B -->|"більше MaximumFrameTime"| D["обрізати до 250 ms"]
    B -->|"у межах"| E["залишити elapsed"]
    C --> F["frameTotal += elapsed<br/>accumulator += elapsed"]
    D --> F
    E --> F
    F --> G["створити FrameTime"]
    G --> H["Scene.Update<br/>усі IUpdateSystem"]
    H --> I{"accumulator >= fixedStep?"}
    I -->|"так"| J["створити FixedTime"]
    J --> K["Scene.FixedUpdate<br/>усі IFixedUpdateSystem"]
    K --> L["accumulator -= fixedStep"]
    L --> I
    I -->|"ні"| M["Update завершено"]
    M --> N["alpha = accumulator / fixedStep"]
    N --> O["під час Draw створити RenderContext"]
    O --> P["Scene.Draw<br/>усі IRenderSystem"]

    classDef engine fill:#dbeafe,stroke:#2563eb,color:#172554
    classDef update fill:#e0e7ff,stroke:#4f46e5,color:#1e1b4b
    classDef physics fill:#fee2e2,stroke:#dc2626,color:#450a0a
    classDef render fill:#dcfce7,stroke:#16a34a,color:#052e16
    class A,B,C,D,E,F,G,M engine
    class H update
    class I,J,K,L physics
    class N,O,P render
~~~

### Порядок одного кадру

1. MonoGame викликає **MainGame.Update**.
2. MainGame перевіряє Escape і передає GameTime у GameEngine.
3. GameEngine обмежує надто великий elapsed значенням MaximumFrameTime. Це
   не дозволяє після паузи виконати сотні фізичних кроків.
4. Час додається в **accumulator**.
5. Спочатку виконується один змінний **Scene.Update**.
6. Поки в accumulator є хоча б один fixedStep, виконується **Scene.FixedUpdate**.
7. Невикористаний залишок часу залишається до наступного кадру.
8. Коли MonoGame викликає Draw, рушій рахує **InterpolationAlpha**.
9. RenderSystem використовує alpha, щоб намалювати проміжну фізичну позу.

Зараз fixedStep дорівнює приблизно **16.667 ms**, тобто фізика працює на
частоті 60 Hz.

## Числовий приклад

Припустимо, до початку кадру accumulator дорівнював 0, а кадр зайняв 40 ms:

| Крок | accumulator |
|---|---:|
| Додали час кадру | 40.000 ms |
| Після fixed tick №1 | 23.333 ms |
| Після fixed tick №2 | 6.666 ms |
| Третього tick немає | 6.666 ms |

Отже:

~~~text
InterpolationAlpha = 6.666 / 16.667 ≈ 0.4
~~~

Рендерер покаже стан приблизно на 40% шляху між попередньою і поточною
фізичними позами.

## Порядок систем

Числовий order визначає порядок лише всередині відповідної фази. Наприклад,
майбутня gameplay-система, яка керує kinematic body, має виконуватися перед
PhysicsSystem:

| order | Система | Активна фаза |
|---:|---|---|
| менше 0 | GameplayKinematicSystem — умовний приклад | FixedUpdate |
| 0 | PhysicsSystem | FixedUpdate |
| 1000 | MeshRenderSystem | Draw |
| 1100 | PhysicsDebugRenderSystem | Draw |

Назва GameplayKinematicSystem тут умовна: окремого обов'язкового класу з такою
назвою рушій не вимагає. Важливе саме правило порядку — система встигає
поставити SetKinematicTarget до того, як PhysicsSystem спорожнить чергу команд
і виконає Jitter step.

## Update, FixedUpdate чи Draw?

- **Update** — введення, UI та логіка, якій потрібна реакція один раз на
  відображений кадр.
- **FixedUpdate** — фізика й детермінована симуляція зі стабільним delta time.
- **Draw** — лише побудова зображення; gameplay-стан тут змінювати не варто.

## Перемикання сцени

ChangeScene не замінює сцену посеред виконання систем. Вона записує
**pendingScene**, а GameEngine застосовує її на початку наступного Update:

1. знищує стару сцену;
2. встановлює нову;
3. викликає Initialize;
4. викликає LoadContent, якщо content рушія вже завантажений;
5. обнуляє accumulator, щоб нова сцена не успадкувала старий фізичний час.

## Відповідні файли

- [MainGame.cs](../../MainGame.cs)
- [GameEngine.cs](../../Engine/GameEngine.cs)
- [GameSettings.cs](../../Configuration/GameSettings.cs)
- [SystemScheduler.cs](../../Engine/Systems/SystemScheduler.cs)
- [FrameTime.cs](../../Engine/Time/FrameTime.cs)
- [FixedTime.cs](../../Engine/Time/FixedTime.cs)
