# 5. Фіксований крок фізики

## Місце фізики серед систем

~~~mermaid
sequenceDiagram
    autonumber
    participant K as Умовна gameplay-система order менше 0
    participant C as PhysicsCommands resource
    participant P as PhysicsSystem order 0
    participant S as PhysicsSimulation
    participant J as Jitter World
    participant E as PhysicsCollisionEvents
    participant W as ECS World

    K->>C: наприклад, SetKinematicTarget(entity, target)
    P->>W: знайти та синхронізувати physics entities
    P->>C: Drain у FIFO-порядку
    C-->>P: SetKinematicTarget command
    P->>S: розрахувати velocity та angular velocity
    P->>S: Step(fixed delta)
    S->>J: World.Step(delta)
    J-->>S: нові пози, швидкості та collision callbacks
    P->>E: Publish()
    P->>S: TryGetPose для кожного physics entity
    S-->>P: position + rotation
    P->>W: оновити Transform3D та PhysicsPose3D
~~~

PhysicsSystem виконується тільки у FixedUpdate. Тому Jitter завжди отримує
однаковий delta time, незалежно від FPS рендерингу.

Gameplay-система на схемі є умовним прикладом. Вона показує, де має працювати
майбутня логіка руху kinematic entity, але не є обов'язковою частиною рушія.

## Повний порядок усередині PhysicsSystem

~~~mermaid
flowchart TD
    A["1. CleanupInactivePoses"] --> B["2. EnsurePhysicsBodies"]
    B --> C["3. CapturePreviousPoses"]
    C --> D["4. StopKinematicBodies"]
    D --> E["5. Drain PhysicsCommands"]
    E --> F["6. PhysicsSimulation.Step"]
    F --> G["7. PhysicsCollisionEvents.Publish"]
    G --> H["8. SynchronizeTransforms"]

    B -. "створює / оновлює / перебудовує" .-> Native["Jitter RigidBody"]
    E -. "force / impulse / teleport / target" .-> Native
    F --> Native
    Native -. "нова pose" .-> H

    classDef ecs fill:#fef3c7,stroke:#d97706,color:#451a03
    classDef physics fill:#fee2e2,stroke:#dc2626,color:#450a0a
    classDef native fill:#fecaca,stroke:#991b1b,color:#450a0a
    class A,B,C,D,E,G,H physics
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
- підтримуваний collider, зараз BoxCollider3D.

Потім PhysicsSimulation.EnsureBoxBody:

- створює відсутній RigidBody;
- перебудовує його, якщо змінився розмір collider;
- інакше оновлює friction, restitution, gravity, mass і motion type.

Після створення або перебудови PhysicsPose3D скидається до поточної пози, щоб
рендерер не інтерполював від старого, вже неактуального стану.

### 3. CapturePreviousPoses

Перед новим кроком:

~~~text
PreviousPosition = CurrentPosition
PreviousRotation = CurrentRotation
~~~

Таким чином зберігається початок відрізка, по якому пізніше інтерполює
рендерер.

### 4. StopKinematicBodies

Усі kinematic bodies отримують нульові linear та angular velocity.

Це робиться перед командами. Якщо гра поставить новий SetKinematicTarget,
команда задасть швидкості на поточний tick. Якщо команда не надійшла, платформа
не продовжить рух зі старою швидкістю.

### 5. Drain PhysicsCommands

Команди виконуються в порядку додавання до Queue:

| Команда | Дія |
|---|---|
| ApplyForce | додає силу |
| ApplyTorque | додає обертальний момент |
| ApplyImpulse | миттєво змінює швидкість |
| ApplyImpulse at position | додає лінійний та обертальний ефект |
| SetVelocity | прямо задає лінійну швидкість |
| Teleport | миттєво змінює pose та очищає contact cache |
| SetKinematicTarget | задає швидкості для досягнення target за один tick |
| SetMotionType | перемикає Dynamic, Kinematic або Static |

Якщо Entity вже не існує або втратила PhysicsBody3D, команда безпечно
пропускається.

#### SetKinematicTarget

Для позиції:

~~~text
linearVelocity = (targetPosition - currentPosition) / fixedDelta
~~~

Для повороту PhysicsSimulation:

1. знаходить quaternion-різницю current → target;
2. обирає коротший шлях обертання;
3. перетворює quaternion на axis + angle;
4. рахує angularVelocity = axis × angle / fixedDelta.

Саме швидкості, а не пряме переставлення body, дозволяють Jitter коректно
розрахувати контакт рухомої платформи з dynamic body.

### 6. PhysicsSimulation.Step

PhysicsSimulation викликає Jitter World.Step з фіксованим delta. Усередині
Jitter:

- інтегруються сили, gravity та швидкості;
- шукаються перетини collider;
- розв'язуються контакти й обмеження;
- оновлюються пози та орієнтації bodies;
- викликаються BeginCollide та EndCollide callbacks.

Jitter типи залишаються всередині PhysicsSimulation. Перетворення Vector3 і
Quaternion виконує PhysicsConvert.

### 7. Publish collision events

Callbacks Jitter спочатку записують зміни у pending lists:

~~~text
pendingStarted
pendingEnded
~~~

Після завершення Step метод Publish переносить їх у публічні списки Started та
Ended і очищає pending. Система з order більшим за PhysicsSystem може прочитати
узгоджений набір подій саме останнього fixed tick.

### 8. SynchronizeTransforms

Після Jitter step для кожної фізичної Entity:

~~~text
Jitter RigidBody.Position    → Transform3D.Position
Jitter RigidBody.Orientation → Transform3D.Rotation
                              → PhysicsPose3D.Current
~~~

Transform3D тепер містить останню завершену фізичну позу. PhysicsPose3D містить
і Previous, і Current, щоб Draw міг побудувати проміжну картинку.

## Три типи руху

| Тип | Хто задає рух | Як взаємодіяти |
|---|---|---|
| Dynamic | Jitter | сили, impulse або velocity |
| Kinematic | gameplay через target, рух виконує Jitter | SetKinematicTarget кожен потрібний tick |
| Static | не рухається під час нормальної симуляції | початкова pose; для рідкісного переносу Teleport |

Для Entity з PhysicsBody3D не слід напряму змінювати Position або Rotation у
Transform3D: наступна синхронізація перезапише їх даними Jitter.

## Межі даних

~~~mermaid
flowchart LR
    Gameplay["Gameplay systems<br/>MonoGame types"] --> Components["ECS components<br/>налаштування"]
    Gameplay --> Commands["PhysicsCommands<br/>одноразові дії"]
    Components --> Bridge["PhysicsSystem"]
    Commands --> Bridge
    Bridge --> Simulation["PhysicsSimulation<br/>entity ↔ body mapping"]
    Simulation --> Jitter["Jitter World<br/>native types"]
    Jitter --> Simulation
    Simulation --> Bridge
    Bridge --> Transform["Transform3D<br/>PhysicsPose3D"]
    Transform --> Render["Render systems"]
~~~

## Відповідні файли

- [PhysicsSystem.cs](../../Engine/Physics/PhysicsSystem.cs)
- [PhysicsSimulation.cs](../../Engine/Physics/PhysicsSimulation.cs)
- [PhysicsCommands.cs](../../Engine/Physics/PhysicsCommands.cs)
- [PhysicsCollisionEvents.cs](../../Engine/Physics/PhysicsCollisionEvents.cs)
- [PhysicsConvert.cs](../../Engine/Physics/PhysicsConvert.cs)
