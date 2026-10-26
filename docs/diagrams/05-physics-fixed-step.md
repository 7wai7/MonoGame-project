# 5. Фіксований крок фізики

## Місце фізики серед систем

~~~mermaid
sequenceDiagram
    autonumber
    participant K as Умовна gameplay-система order менше 0
    participant C as PhysicsCommands resource
    participant P as PhysicsSystem order 0
    participant S as JoltPhysicsSimulation
    participant J as Jolt PhysicsSystem
    participant E as PhysicsCollisionEvents
    participant W as ECS World

    K->>C: наприклад, SetKinematicTarget(entity, target)
    P->>W: знайти та синхронізувати physics entities
    P->>C: Drain у FIFO-порядку
    C-->>P: SetKinematicTarget command
    P->>S: MoveKinematic до target за fixed delta
    P->>S: Step(fixed delta)
    S->>J: Update(delta, jobs)
    J-->>S: нові пози, швидкості та contact callbacks
    P->>E: Publish()
    P->>S: TryGetPose для кожного physics entity
    S-->>P: position + rotation
    P->>W: оновити Transform3D та PhysicsPose3D
~~~

PhysicsSystem виконується тільки у FixedUpdate. Тому Jolt завжди отримує
однаковий delta time, незалежно від FPS рендерингу.

Gameplay-система на схемі є умовним прикладом. Вона показує, де має працювати
майбутня логіка руху kinematic entity, але не є обов'язковою частиною рушія.

## Повний порядок усередині PhysicsSystem

~~~mermaid
flowchart TD
    A["1. CleanupInactivePoses"] --> B["2. EnsurePhysicsBodies"]
    B --> Terrain["3. EnsureTerrainBodies"]
    Terrain --> C["4. CapturePreviousPoses"]
    C --> D["5. StopKinematicBodies"]
    D --> E["6. Drain PhysicsCommands"]
    E --> F["7. JoltPhysicsSimulation.Step"]
    F --> G["8. PhysicsCollisionEvents.Publish"]
    G --> H["9. SynchronizeTransforms"]

    B -. "створює body / оновлює shape" .-> Native["Jolt Body"]
    Terrain -. "створює heightfield body" .-> Native
    E -. "force / impulse / teleport / target" .-> Native
    F --> Native
    Native -. "нова pose" .-> H

    classDef ecs fill:#fef3c7,stroke:#d97706,color:#451a03
    classDef physics fill:#fee2e2,stroke:#dc2626,color:#450a0a
    classDef native fill:#fecaca,stroke:#991b1b,color:#450a0a
    class A,B,Terrain,C,D,E,G,H physics
    class F,Native native
~~~

### 1. CleanupInactivePoses

Коли фізичний компонент видаляється, lifecycle callback не має права одразу
видалити інший ECS-компонент. Тому PhysicsPose3D лише позначається неактивним і
потрапляє до pendingPoseRemoval.

На початку наступного physics tick PhysicsSystem безпечно видаляє такі
PhysicsPose3D.

### 2. EnsurePhysicsBodies

PhysicsSystem знаходить усі Entity з PhysicsBody3D і перевіряє, що вони також
мають:

- Transform3D з початковою або останньою фізичною позою;
- рівно один підтримуваний collider: BoxCollider3D, SphereCollider3D,
  CylinderCollider3D, CapsuleCollider3D або ConeCollider3D.

Потім JoltPhysicsSimulation.EnsureBody:

- створює відсутній Jolt body;
- створює новий shape і встановлює його у тому самому body, якщо змінився
  розмір або тип collider;
- інакше оновлює friction, restitution, gravity, mass і motion type.

PhysicsPose3D скидається до поточної пози лише після створення нового body.
Зміна shape зберігає body і його pose, тому interpolation не потрібно
починати заново.

### 3. EnsureTerrainBodies

`EnsureTerrainBodies` окремо створює або оновлює статичні heightfield bodies
для Entity з `TerrainCollider3D`. Вони не мають `PhysicsBody3D`.

### 4. CapturePreviousPoses

Перед новим кроком:

~~~text
PreviousPosition = CurrentPosition
PreviousRotation = CurrentRotation
~~~

Таким чином зберігається початок відрізка, по якому пізніше інтерполює
рендерер.

### 5. StopKinematicBodies

Усі kinematic bodies отримують нульові linear та angular velocity.

Це робиться перед командами. Якщо гра поставить новий SetKinematicTarget,
команда задасть швидкості на поточний tick. Якщо команда не надійшла, платформа
не продовжить рух зі старою швидкістю.

### 6. Drain PhysicsCommands

Команди виконуються в порядку додавання до Queue:

| Команда | Дія |
|---|---|
| ApplyForce | додає силу |
| ApplyTorque | додає обертальний момент |
| ApplyImpulse | миттєво змінює швидкість |
| ApplyImpulse at position | додає лінійний та обертальний ефект |
| SetVelocity | прямо задає лінійну швидкість |
| Teleport | миттєво змінює pose та очищає contact cache |
| SetKinematicTarget | просить Jolt досягнути target за один tick через MoveKinematic |
| SetMotionType | перемикає Dynamic, Kinematic або Static |

Якщо Entity вже не існує або втратила PhysicsBody3D, команда безпечно
пропускається.

#### SetKinematicTarget

`PhysicsSystem` передає позицію, поворот і тривалість fixed tick у
`JoltPhysicsSimulation.SetKinematicTarget`. Той викликає Jolt `MoveKinematic`.
Jolt сам визначає рух, потрібний для контакту платформи з dynamic body;
рушій більше не рахує angular velocity вручну.

### 7. JoltPhysicsSimulation.Step

JoltPhysicsSimulation викликає Jolt `PhysicsSystem.Update` з фіксованим delta.
Усередині Jolt:

- інтегруються сили, gravity та швидкості;
- шукаються перетини collider;
- розв'язуються контакти й обмеження;
- оновлюються пози та орієнтації bodies;
- викликаються contact-added і contact-removed callbacks.

Jolt типи та перетворення між MonoGame і `System.Numerics` залишаються
всередині `JoltPhysicsSimulation`.

### 8. Publish collision events

Jolt може повідомити про кілька контактів між одними й тими самими Entity.
`JoltPhysicsSimulation` рахує активні контакти кожної пари й після `Step`
додає до pending lists тільки зміну стану пари:

~~~text
pendingStarted
pendingEnded
~~~

Після завершення Step метод Publish переносить їх у публічні списки Started та
Ended і очищає pending. Система з order більшим за PhysicsSystem може прочитати
узгоджений набір подій саме останнього fixed tick.

### 9. SynchronizeTransforms

Після Jolt step для кожної фізичної Entity:

~~~text
Jolt Body.Position → Transform3D.Position
Jolt Body.Rotation → Transform3D.Rotation
                   → PhysicsPose3D.Current
~~~

Transform3D тепер містить останню завершену фізичну позу. PhysicsPose3D містить
і Previous, і Current, щоб Draw міг побудувати проміжну картинку.

## Три типи руху

| Тип | Хто задає рух | Як взаємодіяти |
|---|---|---|
| Dynamic | Jolt | сили, impulse або velocity |
| Kinematic | gameplay через target, рух виконує Jolt | SetKinematicTarget кожен потрібний tick |
| Static | не рухається під час нормальної симуляції | початкова pose; для рідкісного переносу Teleport |

Для Entity з PhysicsBody3D не слід напряму змінювати Position або Rotation у
Transform3D: наступна синхронізація перезапише їх даними Jolt.

## Межі даних

~~~mermaid
flowchart LR
    Gameplay["Gameplay systems<br/>MonoGame types"] --> Components["ECS components<br/>налаштування"]
    Gameplay --> Commands["PhysicsCommands<br/>одноразові дії"]
    Components --> Bridge["PhysicsSystem"]
    Commands --> Bridge
    Bridge --> Simulation["JoltPhysicsSimulation<br/>entity ↔ body mapping"]
    Simulation --> Jolt["Jolt PhysicsSystem<br/>native types"]
    Jolt --> Simulation
    Simulation --> Bridge
    Bridge --> Transform["Transform3D<br/>PhysicsPose3D"]
    Transform --> Render["Render systems"]
~~~

## Відповідні файли

- [PhysicsSystem.cs](../../Engine/Physics/PhysicsSystem.cs)
- [JoltPhysicsSimulation.cs](../../Engine/Physics/JoltPhysicsSimulation.cs)
- [PhysicsCommands.cs](../../Engine/Physics/PhysicsCommands.cs)
- [PhysicsCollisionEvents.cs](../../Engine/Physics/PhysicsCollisionEvents.cs)
