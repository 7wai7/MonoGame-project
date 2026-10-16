# 6. Життєвий цикл фізичного тіла

ECS Entity і Jitter RigidBody — це два різні об'єкти. Їхній зв'язок створює та
підтримує PhysicsSimulation через приватний BodyEntry.

## Створення та синхронізація

~~~mermaid
flowchart TD
    A["PhysicsSystem знаходить<br/>Entity з PhysicsBody3D"] --> B{"Є Transform3D?"}
    B -->|"ні"| Error1["помилка конфігурації"]
    B -->|"так"| C{"Рівно один<br/>підтримуваний collider?"}
    C -->|"ні"| Error2["помилка конфігурації"]
    C -->|"так"| D["EnsureBody"]
    D --> E{"BodyEntry існує?"}
    E -->|"ні"| F["CreateBody"]
    F --> G["створити Jitter RigidBody"]
    G --> H["задати position, rotation,<br/>потрібний shape та settings"]
    H --> I["записати mapping<br/>Entity ↔ RigidBody"]
    I --> J["підписатися на collision callbacks"]
    J --> K["PhysicsBodyChange.Created"]

    E -->|"так"| L{"Тип shape збігається?"}
    L -->|"ні"| M["від'єднати старий shape"]
    M --> N["приєднати новий shape<br/>до того самого RigidBody"]
    N --> Q["ApplySettings + refresh shape"]
    Q --> R["PhysicsBodyChange.ShapeChanged"]

    L -->|"так"| O{"Розміри змінилися?"}
    O -->|"так"| P["змінити властивості<br/>наявного shape"]
    P --> Q
    O -->|"ні"| S["ApplySettings"]
    S --> T["PhysicsBodyChange.None"]

    K --> U["створити або скинути PhysicsPose3D"]
    R --> V["продовжити без скидання interpolation"]
    T --> V

    classDef ecs fill:#fef3c7,stroke:#d97706,color:#451a03
    classDef physics fill:#fee2e2,stroke:#dc2626,color:#450a0a
    classDef error fill:#fecaca,stroke:#991b1b,color:#450a0a
    class A,B,C,D,E,L,O,U,V ecs
    class F,G,H,I,J,K,M,N,P,Q,R,S,T physics
    class Error1,Error2 error
~~~

## Що зберігає BodyEntry

BodyEntry є приватним службовим записом PhysicsSimulation:

~~~text
BodyEntry
├── Body     → Jitter RigidBody
├── Shape    → приєднаний Jitter RigidBodyShape
└── Settings → остання синхронізована PhysicsBody3D
~~~

PhysicsSimulation має два словники:

~~~text
Entity    → BodyEntry
RigidBody → Entity
~~~

Перший потрібен для команд і синхронізації pose. Другий потрібен, щоб collision
callback Jitter можна було перетворити назад на пару ECS Entity.

## Навіщо PhysicsBodyChange

EnsureBody повертає один із трьох результатів:

| Результат | Що сталося | Дія PhysicsSystem |
|---|---|---|
| None | body уже існує; shape не змінювався | залишити історію pose |
| Created | створено новий RigidBody | скинути Previous і Current |
| ShapeChanged | shape змінено або замінено в наявному body | залишити історію pose |

Сигнал потрібен, щоб PhysicsSystem відрізняв справжнє створення native body від
зміни його collision geometry. У другому випадку pose самого body не змінюється.

## Як collider змінюється без перебудови body

Зміна friction або restitution не змінює геометрію, тому їх можна записати у
наявний RigidBody.

Jitter дозволяє змінювати `Size`, `Radius`, `Height` і `Length` у підтримуваних
primitive shapes. Якщо змінюється лише розмір, PhysicsSimulation:

1. змінює властивості вже приєднаного shape;
2. для Dynamic body перераховує mass та inertia з тією самою заданою масою;
3. оновлює broad-phase bounds і contact cache;
4. залишає той самий RigidBody, його pose, velocity та constraints.

Якщо тип collider змінюється, наприклад `BoxCollider3D` на
`SphereCollider3D`, PhysicsSimulation від'єднує старий shape та приєднує новий
до **того самого** RigidBody. Видаляти й заново створювати body не потрібно.

Render Mesh при цьому не перебудовується автоматично. Розміри collider і
Transform3D.Scale навмисно є незалежними даними.

## Підтримувані primitive colliders

| ECS-компонент | Параметри | Локальна вісь |
|---|---|---|
| BoxCollider3D | повний `Size` по X/Y/Z | — |
| SphereCollider3D | `Radius` | — |
| CylinderCollider3D | `Radius`, повний `Height` | Y |
| CapsuleCollider3D | `Radius`, `Length` циліндричної частини | Y |
| ConeCollider3D | `Radius`, повний `Height` | Y |

Для capsule повна висота дорівнює `Length + 2 * Radius`. На одній фізичній
Entity має бути рівно один компонент collider. Convex hull, triangle mesh,
compound і terrain shapes потребують окремого представлення geometry та не є
частиною цього набору primitive colliders.

Jitter розташовує локальний origin cone у його центрі мас: base лежить на
`-Height / 4`, а apex — на `3 * Height / 4`. Debug wireframe враховує це, тому
він збігається з collision shape.

## Оновлення налаштувань

Кожен fixed tick ApplySettings порівнює поточний PhysicsBody3D з кешованим:

- MotionType оновлюється за потреби;
- Friction, Restitution та AffectedByGravity записуються в body;
- mass/inertia перераховуються для Dynamic body, якщо змінилася Mass або shape,
  а також під час переходу з Static/Kinematic у Dynamic.

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

    G->>W: Remove PhysicsBody3D<br/>або transform / entity
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

Видалення collider обробляється інакше: воно синхронізується на початку
наступного fixed tick. Завдяки цьому між двома ticks можна послідовно видалити
`BoxCollider3D`, додати `SphereCollider3D`, а потім зберегти наявний RigidBody.
Якщо до синхронізації нового collider не додано або одночасно залишилося більше
одного collider, PhysicsSystem видаляє native body та повідомляє про неправильну
конфігурацію.

Якщо видалити лише Transform3D, але залишити PhysicsBody3D на живій Entity,
native body зникне одразу, а наступний EnsurePhysicsBodies повідомить про
неповну конфігурацію помилкою. Фізична Entity повинна або мати весь
обов'язковий набір компонентів, або не мати PhysicsBody3D.

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
- [SphereCollider3D.cs](../../Engine/Physics/Components/SphereCollider3D.cs)
- [CylinderCollider3D.cs](../../Engine/Physics/Components/CylinderCollider3D.cs)
- [CapsuleCollider3D.cs](../../Engine/Physics/Components/CapsuleCollider3D.cs)
- [ConeCollider3D.cs](../../Engine/Physics/Components/ConeCollider3D.cs)
- [PhysicsPose3D.cs](../../Engine/Physics/Components/PhysicsPose3D.cs)
- [World.cs](../../Ecs/World.cs)
