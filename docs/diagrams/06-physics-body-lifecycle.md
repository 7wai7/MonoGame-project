# 6. Життєвий цикл фізичного тіла

ECS `Entity` містить компоненти гри, а Jolt `Body` живе всередині фізичного
рушія. Їх пов'язує `JoltPhysicsSimulation`: ігровий код не зберігає native body
у компоненті й звертається до фізики через `PhysicsSystem` та
`PhysicsCommands`.

## Створення primitive body

~~~mermaid
flowchart TD
    A["PhysicsSystem: Entity з PhysicsBody3D"] --> B{"Є Transform3D і<br/>рівно один primitive collider?"}
    B -->|"ні"| Error["помилка конфігурації"]
    B -->|"так"| C["JoltPhysicsSimulation.EnsureBody"]
    C --> D{"Є BodyEntry для Entity?"}
    D -->|"ні"| E["створити Jolt Shape і Body"]
    E --> F["записати Entity → BodyEntry<br/>та BodyID → Entity"]
    F --> G["PhysicsBodyChange.Created"]
    G --> H["створити або скинути PhysicsPose3D"]
    D -->|"так"| I{"Тип або розміри<br/>collider змінилися?"}
    I -->|"так"| J["створити новий Shape<br/>SetShape у тому самому BodyID"]
    J --> K["оновити масу та інерцію<br/>для Dynamic body"]
    K --> L["PhysicsBodyChange.ShapeChanged"]
    I -->|"ні"| M["оновити змінені settings"]
    M --> N["PhysicsBodyChange.None"]
    L --> O["залишити історію PhysicsPose3D"]
    N --> O
~~~

На одній primitive Entity мають бути `Transform3D`, `PhysicsBody3D` і рівно
один із `BoxCollider3D`, `SphereCollider3D`, `CylinderCollider3D`,
`CapsuleCollider3D`, `ConeCollider3D`. `PhysicsSystem` перевіряє цей набір
перед створенням тіла.

`BodyEntry` — приватний запис усередині `JoltPhysicsSimulation`:

~~~text
BodyEntry
├── Id       → Jolt BodyID
├── Collider → останні тип і розміри колайдера
└── Settings → останній синхронізований PhysicsBody3D

Entity → BodyEntry
BodyID → Entity
~~~

Перший словник потрібен для команд і синхронізації поз. Другий дозволяє
перетворити native contact callback на пару ECS-сутностей.

## Навіщо PhysicsBodyChange

| Результат `EnsureBody` | Що сталося | Дія `PhysicsSystem` |
|---|---|---|
| `Created` | створено новий Jolt Body | скинути `Previous` і `Current` у `PhysicsPose3D` |
| `ShapeChanged` | новий shape встановлено у тому самому body | залишити історію поз |
| `None` | геометрія не змінювалася | залишити історію поз |

Коли змінюється лише friction, restitution, mass, gravity participation або
motion type, `ApplySettings` оновлює властивості тіла. Зміна типу чи розміру
колайдера створює новий Jolt shape, але **не новий body**: `SetShape` зберігає
`BodyID` і позу. Для Dynamic body потім оновлюються mass та inertia.
Поточні контакти цієї Entity очищаються та будуть визначені знову під час
наступного кроку фізики.

Render `Mesh` при цьому не перебудовується автоматично. Візуальна геометрія й
колайдер — незалежні дані; `Transform3D.Scale` не змінює розмір колайдера.

| Primitive collider | Параметри | Локальна вісь |
|---|---|---|
| `BoxCollider3D` | повний `Size` по X/Y/Z | — |
| `SphereCollider3D` | `Radius` | — |
| `CylinderCollider3D` | `Radius`, повний `Height` | Y |
| `CapsuleCollider3D` | `Radius`, `Length` циліндричної частини | Y |
| `ConeCollider3D` | `Radius`, повний `Height` | Y |

Повна висота capsule — `Length + 2 * Radius`. Локальний origin cone у цьому
рушії — на чверть висоти над base: base має `Y = -Height / 4`, apex —
`Y = 3 * Height / 4`. Render mesh, Jolt shape і debug wireframe використовують
одну домовленість.

## Окреме terrain body

`TerrainCollider3D` не є primitive collider і не потребує `PhysicsBody3D`.
`TerrainColliderStreamingSystem` додає цей компонент поблизу активної камери
та рухомих фізичних тіл. `PhysicsSystem.EnsureTerrainBodies` передає його до
`JoltPhysicsSimulation`, який створює статичний `HeightFieldShape` з кожного
висотного семпла чанка. Native `BodyID` та посилання на `TerrainHeightField`
зберігаються в окремому `TerrainBodyEntry`. Render LOD на цей колайдер не
впливає.

Коли streaming видаляє `TerrainCollider3D`, відповідне native body видаляється
синхронно через ECS lifecycle callback.

## Хто керує позою

Для існуючого body `Transform3D.Position` і `Rotation` не копіюються у Jolt
кожного кадру. Джерелом правди є Jolt:

- Dynamic body рухається від сил, імпульсів і швидкості.
- Kinematic body отримує ціль через `PhysicsCommands.SetKinematicTarget`;
  `JoltPhysicsSimulation` викликає `MoveKinematic` на поточний fixed tick.
- Static body не рухається під час звичайної симуляції; рідкісне перенесення
  виконується явною командою `Teleport`.

Після `Step` `PhysicsSystem` копіює native position/rotation у `Transform3D`
та `PhysicsPose3D.Current`. Тому gameplay-коду не слід змінювати позицію
фізичної Entity безпосередньо в `Transform3D`.

## Видалення та події контакту

~~~mermaid
sequenceDiagram
    autonumber
    participant G as Gameplay / World
    participant P as PhysicsSystem
    participant S as JoltPhysicsSimulation
    participant J as Jolt world

    G->>P: ComponentRemoving для PhysicsBody3D,<br/>TerrainCollider3D або Transform3D
    P->>S: RemoveBody(entity)
    S->>S: прибрати active contact pairs<br/>і записати pending Ended
    S->>J: RemoveAndDestroyBody(BodyID)
    P->>P: позначити PhysicsPose3D неактивною
    Note over P: на початку наступного fixed tick
    P->>G: видалити PhysicsPose3D, якщо Entity жива
~~~

World надсилає `ComponentRemoving` **до** видалення ECS-даних. Native body
зникає одразу, тож не може створити «примарний» контакт у наступному tick.
Видалення `PhysicsPose3D` відкладається: усередині lifecycle callback World
не дозволяє вкладені структурні зміни. Якщо зникає ціла Entity, повторний
`RemoveBody` просто поверне `false`.

Видалення лише primitive collider синхронізується на початку наступного fixed
tick. Між двома ticks можна замінити, наприклад, `BoxCollider3D` на
`SphereCollider3D` без видалення body. Якщо після заміни немає рівно одного
primitive collider, `PhysicsSystem` видаляє body та повідомляє про помилку
конфігурації.

Jolt може повідомити про кілька native контактів між одними й тими самими
Entity. `JoltPhysicsSimulation` рахує їх, а після `Step` публікує тільки зміну
стану пари: `Started` при першому контакті, `Ended` після останнього. При
явному видаленні body `Ended` додається вручну й стає видимим після наступного
`PhysicsCollisionEvents.Publish()`.

## Зміна MotionType

Рекомендований шлях — `PhysicsCommands.SetMotionType`:

1. Gameplay додає команду.
2. `PhysicsSystem` забирає її перед Jolt step.
3. `JoltPhysicsSimulation` змінює native motion type та object layer.
4. `PhysicsSystem` оновлює `PhysicsBody3D.MotionType`.

Так ECS-компонент і native body змінюються в одному визначеному місці.

## Відповідні файли

- [JoltPhysicsSimulation.cs](../../Engine/Physics/JoltPhysicsSimulation.cs)
- [PhysicsSystem.cs](../../Engine/Physics/PhysicsSystem.cs)
- [PhysicsBody3D.cs](../../Engine/Physics/Components/PhysicsBody3D.cs)
- [TerrainCollider3D.cs](../../Engine/Physics/Components/TerrainCollider3D.cs)
- [PhysicsCommands.cs](../../Engine/Physics/PhysicsCommands.cs)
- [PhysicsCollisionEvents.cs](../../Engine/Physics/PhysicsCollisionEvents.cs)
- [PhysicsPose3D.cs](../../Engine/Physics/Components/PhysicsPose3D.cs)
- [World.cs](../../Ecs/World.cs)
