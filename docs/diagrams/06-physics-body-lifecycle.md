# 6. Життєвий цикл фізичного тіла

ECS Entity і Jitter RigidBody — це два різні об'єкти. Їхній зв'язок створює та
підтримує PhysicsSimulation через приватний BodyEntry.

## Створення та синхронізація

~~~mermaid
flowchart TD
    A["PhysicsSystem знаходить<br/>Entity з PhysicsBody3D"] --> B{"Є Transform3D?"}
    B -->|"ні"| Error1["помилка конфігурації"]
    B -->|"так"| C{"Є BoxCollider3D?"}
    C -->|"ні"| Error2["помилка конфігурації"]
    C -->|"так"| D["EnsureBoxBody"]
    D --> E{"BodyEntry існує?"}
    E -->|"ні"| F["CreateBoxBody"]
    F --> G["створити Jitter RigidBody"]
    G --> H["задати position, rotation<br/>BoxShape та settings"]
    H --> I["записати mapping<br/>Entity ↔ RigidBody"]
    I --> J["підписатися на collision callbacks"]
    J --> K["PhysicsBodyChange.Created"]

    E -->|"так"| L{"Collider size змінився?"}
    L -->|"так"| M["RebuildBoxBody"]
    M --> N["зберегти pose та velocities"]
    N --> O["видалити старе body"]
    O --> P["створити нове body з новим shape"]
    P --> Q["відновити velocities"]
    Q --> R["PhysicsBodyChange.Rebuilt"]

    L -->|"ні"| S["ApplySettings"]
    S --> T["PhysicsBodyChange.None"]

    K --> U["створити або скинути PhysicsPose3D"]
    R --> U
    T --> V["продовжити без скидання interpolation"]

    classDef ecs fill:#fef3c7,stroke:#d97706,color:#451a03
    classDef physics fill:#fee2e2,stroke:#dc2626,color:#450a0a
    classDef error fill:#fecaca,stroke:#991b1b,color:#450a0a
    class A,B,C,D,E,L,U,V ecs
    class F,G,H,I,J,K,M,N,O,P,Q,R,S,T physics
    class Error1,Error2 error
~~~

## Що зберігає BodyEntry

BodyEntry є приватним службовим записом PhysicsSimulation:

~~~text
BodyEntry
├── Body         → Jitter RigidBody
├── Settings     → остання синхронізована PhysicsBody3D
└── ColliderSize → останній синхронізований розмір BoxCollider3D
~~~

PhysicsSimulation має два словники:

~~~text
Entity    → BodyEntry
RigidBody → Entity
~~~

Перший потрібен для команд і синхронізації pose. Другий потрібен, щоб collision
callback Jitter можна було перетворити назад на пару ECS Entity.

## Навіщо PhysicsBodyChange

EnsureBoxBody повертає один із трьох результатів:

| Результат | Що сталося | Дія PhysicsSystem |
|---|---|---|
| None | body уже існує; shape не змінювався | залишити історію pose |
| Created | створено новий RigidBody | скинути Previous і Current |
| Rebuilt | shape вимагав нового RigidBody | скинути Previous і Current |

Без цього сигналу після перебудови renderer міг би інтерполювати нове тіло від
пози старого стану та показати короткий неправильний рух.

## Чому collider перебудовує body

Зміна friction або restitution не змінює геометрію, тому їх можна записати у
наявний RigidBody.

Зміна BoxCollider3D.Size означає інший BoxShape. Поточна реалізація створює
новий native body:

1. запам'ятовує position та rotation;
2. для не-static body запам'ятовує linear й angular velocity;
3. прибирає collision pairs старого body;
4. відписує callbacks і видаляє старий body з Jitter World;
5. створює body з новим BoxShape;
6. відновлює швидкості, якщо нове body не Static.

Render Mesh при цьому не перебудовується автоматично. BoxCollider3D.Size і
Transform3D.Scale навмисно є незалежними даними.

## Оновлення налаштувань

Кожен fixed tick ApplySettings порівнює поточний PhysicsBody3D з кешованим:

- MotionType оновлюється за потреби;
- Friction, Restitution та AffectedByGravity записуються в body;
- mass/inertia перераховуються для Dynamic body, якщо змінилася Mass.

Pose Transform3D не копіюється в уже існуючий Jitter body. Для фізичної Entity
джерелом правди є Jitter, тому рух виконується через PhysicsCommands.

## Видалення

~~~mermaid
sequenceDiagram
    autonumber
    participant G as Gameplay / World
    participant W as ECS World
    participant P as PhysicsSystem
    participant S as PhysicsSimulation
    participant J as Jitter World

    G->>W: Remove PhysicsBody3D<br/>або collider / transform / entity
    W->>P: ComponentRemoving(entity, type)
    Note over W,P: Компонент і Entity ще доступні
    P->>S: RemoveBody(entity)
    S->>S: знайти active collision pairs
    S->>S: додати штучні pending Ended events
    S->>J: unsubscribe callbacks + Remove(body)
    P->>W: PhysicsPose3D.IsActive = false
    P->>P: додати Entity у pendingPoseRemoval
    W->>W: завершити видалення компонента / Entity
    Note over P,W: На початку наступного physics tick
    P->>W: Remove PhysicsPose3D, якщо Entity ще жива
~~~

Native body видаляється **синхронно** під час ComponentRemoving. Воно не може
залишитися у Jitter World ще на один tick і створити «примарне» зіткнення.

PhysicsPose3D видаляється пізніше, бо World забороняє вкладені структурні зміни
всередині lifecycle callback. До видалення він неактивний, тому renderer його
не використовує.

Якщо видаляється ціла Entity, World викликає ComponentRemoving для всіх її
компонентів. Повторний RemoveBody для тієї самої Entity просто повертає false.

Якщо видалити лише Transform3D або collider, але залишити PhysicsBody3D на
живій Entity, native body зникне одразу, а наступний EnsurePhysicsBodies
повідомить про неповну конфігурацію помилкою. Фізична Entity повинна або мати
весь обов'язковий набір компонентів, або не мати PhysicsBody3D.

## Зіткнення під час видалення

Jitter не зобов'язаний викликати EndCollide, коли body явно прибирається.
PhysicsSimulation сам знаходить усі active pairs з цією Entity та додає Ended
до pending events.

Native body зникає одразу, а накопичені Ended стають видимими у
PhysicsCollisionEvents після наступного Publish у physics tick.

## Зміна MotionType

Рекомендований шлях — PhysicsCommands.SetMotionType:

1. gameplay додає команду;
2. PhysicsSystem забирає її перед Jitter step;
3. PhysicsSimulation змінює native MotionType;
4. PhysicsSystem оновлює PhysicsBody3D.MotionType;
5. подальший рух підпорядковується правилам нового типу.

Так ECS-компонент і native body змінюються в одному визначеному місці.

## Відповідні файли

- [PhysicsSimulation.cs](../../Engine/Physics/PhysicsSimulation.cs)
- [PhysicsSystem.cs](../../Engine/Physics/PhysicsSystem.cs)
- [PhysicsBody3D.cs](../../Engine/Physics/Components/PhysicsBody3D.cs)
- [BoxCollider3D.cs](../../Engine/Physics/Components/BoxCollider3D.cs)
- [PhysicsPose3D.cs](../../Engine/Physics/Components/PhysicsPose3D.cs)
- [World.cs](../../Ecs/World.cs)
