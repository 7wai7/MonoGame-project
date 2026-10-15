# 3. Життєвий цикл ECS-сутності

## Загальна схема

~~~mermaid
flowchart TD
    A["World.CreateEntity()"] --> B{"Є вільний Id?"}
    B -->|"ні"| C["створити новий Id<br/>Generation = 1"]
    B -->|"так"| D["взяти Id зі stack freeIds<br/>залишити вже збільшену Generation"]
    C --> E["alive = true<br/>EntityCount++<br/>StructureVersion++"]
    D --> E
    E --> F["повернути Entity(Id, Generation)"]

    F --> G["World.Add Transform3D"]
    G --> H["ComponentPool Transform3D<br/>додає дані у dense масив"]
    H --> I["World.Add інші компоненти"]
    I --> J["системи знаходять Entity через Query"]
    J --> K{"Потрібна структурна зміна<br/>під час query?"}
    K -->|"ні"| L["читати або змінювати<br/>значення компонентів"]
    K -->|"так"| M["записати дію в<br/>EntityCommandBuffer"]
    M --> N["після query: Playback(World)"]
    L --> O{"Сутність ще потрібна?"}
    N --> O
    O -->|"так"| J
    O -->|"ні"| P["World.DestroyEntity(entity)"]
    P --> Q["EntityDestroying event"]
    Q --> R["ComponentRemoving event<br/>для кожного компонента"]
    R --> S["видалити компоненти з pools"]
    S --> T["alive = false<br/>Generation++<br/>Id → freeIds"]
    T --> U["старий Entity handle<br/>більше не валідний"]

    classDef entity fill:#fef3c7,stroke:#d97706,color:#451a03
    classDef event fill:#fee2e2,stroke:#dc2626,color:#450a0a
    classDef safe fill:#dcfce7,stroke:#16a34a,color:#052e16
    class A,B,C,D,E,F,G,H,I,J,K,L,M,N,O,S,T,U entity
    class P,Q,R event
    class N safe
~~~

## 1. Entity — це handle, а не об'єкт

Entity містить лише два числа:

~~~text
Entity
├── Id
└── Generation
~~~

У ньому немає позиції, mesh або фізичного тіла. Усі дані зберігаються окремо
в ComponentPool відповідного типу.

Наприклад:

~~~csharp
var cube = World.CreateEntity();       // Entity(2:7)
World.Add(cube, new Transform3D(...));
World.Add(cube, new PhysicsBody3D(...));
World.Add(cube, new BoxCollider3D(...));
~~~

Змінна cube є лише ключем, за яким World знаходить компоненти.

## 2. Створення сутності

World.CreateEntity виконує такі дії:

1. Перевіряє, що World ще не знищений.
2. Перевіряє, що зараз не виконується lifecycle callback.
3. Намагається взяти вільний Id зі stack **freeIds**.
4. Якщо вільного Id немає, створює новий Id і початкову Generation 1.
5. Позначає Id як alive.
6. Збільшує EntityCount та StructureVersion.
7. Повертає Entity з актуальними Id і Generation.

## 3. Додавання компонентів

Кожен тип компонента має свій незалежний pool:

~~~text
ComponentPool<Transform3D>
ComponentPool<MeshRenderer>
ComponentPool<PhysicsBody3D>
ComponentPool<BoxCollider3D>
...
~~~

World.Add перевіряє, що Entity жива, знаходить або створює потрібний pool,
додає до нього пару Entity + Component і збільшує StructureVersion.

**Add** вимагає, щоб такого компонента ще не було. **Set** замінює значення
наявного компонента або додає його, якщо він відсутній.

## 4. Читання і зміна даних

World.Get повертає компонент через **ref**:

~~~csharp
ref var transform = ref World.Get<Transform3D>(cube);
transform.Position.Y += 1f;
~~~

Це змінює значення без копіювання структури назад у World. Така зміна не є
структурною: сутність усе ще має той самий набір компонентів.

Структурними є:

- CreateEntity і DestroyEntity;
- додавання нового компонента;
- видалення компонента;
- Set, якщо компонента раніше не було.

## 5. StructureVersion і безпечний Query

### Що таке StructureVersion

StructureVersion — це одне ціле число, яке зберігається всередині World. Воно
показує, скільки разів змінювалася **структура ECS**.

Gameplay-код не задає його вручну. World сам збільшує число після завершення
кожної структурної операції. DestroyEntity збільшує його один раз, навіть якщо
разом із Entity було видалено кілька компонентів; ClearEntities також збільшує
його один раз після повного очищення.

Під структурою мається на увазі:

- які Entity існують;
- які компоненти має кожна Entity;
- які Entity знаходяться у dense-масивах ComponentPool.

Це не версія конкретної Entity і не заміна Generation:

- **Generation** допомагає визначити, чи досі валідний один Entity handle;
- **StructureVersion** допомагає визначити, чи не змінилося розташування даних
  під час обходу Query.

Наприклад, якщо StructureVersion дорівнює 12:

| Операція | Нове значення | Чому |
|---|---:|---|
| змінити Transform3D.Position через ref | 12 | набір компонентів не змінився |
| замінити наявний компонент через Set | 12 | компонент залишився у тому самому pool |
| SetResource або RemoveResource | 12 | ресурси не беруть участі в EntityQuery |
| CreateEntity | 13 | з'явилася нова Entity |
| Add нового компонента | 14 | Entity увійшла до нового pool |
| Remove компонента | 15 | Entity вийшла з pool |
| DestroyEntity | 16 | Entity та її компоненти зникли |

ClearEntities також збільшує StructureVersion. Значення числа саме по собі не
описує, **що** змінилося. Воно використовується як простий сигнал:

~~~text
версія та сама     → структура не змінювалася
версія відрізняється → структура змінювалася
~~~

Число зручніше за bool-прапорець: кожен Enumerator просто запам'ятовує своє
початкове значення і порівнює його з поточним. Нічого не потрібно скидати назад
у false, і кілька одночасно створених Enumerator можуть незалежно перевіряти
той самий World.

### Де зберігаються дані Query

Query не створює список результатів і не копіює компоненти. Дані залишаються у
World та його ComponentPool.

| Об'єкт | Де живе | Що зберігає |
|---|---|---|
| World | у поточній GameScene | StructureVersion і dictionary тип компонента → pool |
| ComponentPool&lt;T&gt; | всередині World | sparse, entities та components масиви |
| EntityQuery | тимчасове значення, повернене World.Query | посилання на World і потрібні pools |
| Enumerator | створюється на початку foreach | очікувану version, smallest pool, index і Current |
| EntityCommandBuffer | створює система за потреби | список відкладених дій над World |

~~~mermaid
flowchart LR
    W["World<br/>StructureVersion = 12<br/>componentPools dictionary"]
    PT["ComponentPool Transform3D<br/>entities + components + sparse"]
    PB["ComponentPool PhysicsBody3D<br/>entities + components + sparse"]
    Q["EntityQuery<br/>посилання на World, PT і PB"]
    E["Enumerator<br/>expectedVersion = 12<br/>smallest = PB<br/>index = -1"]

    W --> PT
    W --> PB
    W -->|"World.Query створює"| Q
    PT --> Q
    PB --> Q
    Q -->|"foreach викликає GetEnumerator"| E

    classDef world fill:#fef3c7,stroke:#d97706,color:#451a03
    classDef query fill:#e0e7ff,stroke:#4f46e5,color:#1e1b4b
    class W,PT,PB world
    class Q,E query
~~~

Важливий наслідок: EntityQuery — це лише маленький опис майбутнього обходу.
Справжні Entity та компоненти він читає безпосередньо з pools під час кожного
MoveNext.

### Конкретний приклад одного Query

Нехай World має StructureVersion 12, а pools виглядають так:

~~~text
Transform3D pool:   [E2, E4, E7]
PhysicsBody3D pool: [E4, E7]
~~~

Викликається:

~~~csharp
foreach (var entity in world.Query<Transform3D, PhysicsBody3D>())
{
    ref var transform = ref world.Get<Transform3D>(entity);
    transform.Scale *= 1.01f;
}
~~~

Порядок роботи:

1. **World.Query** повертає EntityQuery із посиланнями на World, Transform3D
   pool і PhysicsBody3D pool.
2. **foreach** викликає Query.GetEnumerator.
3. Enumerator записує expectedVersion = 12.
4. Enumerator обирає PhysicsBody3D як smallest pool, бо в ньому 2 Entity, а у
   Transform3D pool — 3.
5. Перший MoveNext порівнює World.StructureVersion 12 з expectedVersion 12.
6. Версії однакові, тому береться кандидат E4 із dense-масиву smallest pool.
7. Через sparse lookup перевіряється, що E4 є в обох pools.
8. Current стає E4, після чого тіло циклу отримує компоненти через World.Get.
9. Зміна Scale не змінює набір компонентів, тому StructureVersion залишається
   12.
10. Наступний MoveNext знову перевіряє version і переходить до кандидата E7.

Спрощено MoveNext виглядає так:

~~~mermaid
flowchart TD
    A["MoveNext"] --> B{"World.StructureVersion<br/>дорівнює expectedVersion?"}
    B -->|"ні"| C["кинути InvalidOperationException"]
    B -->|"так"| D["index++"]
    D --> E{"У smallest pool<br/>залишився кандидат?"}
    E -->|"ні"| F["повернути false<br/>foreach завершено"]
    E -->|"так"| G["взяти Entity з<br/>smallest.entities[index]"]
    G --> H{"Entity присутня<br/>у всіх потрібних pools?"}
    H -->|"ні"| D
    H -->|"так"| I["Current = Entity<br/>повернути true"]
~~~

### Від якої помилки захищає перевірка

Уявімо, що PhysicsBody3D pool зберігає:

~~~text
index 0 → E4
index 1 → E7
~~~

Під час обробки E4 код одразу виконує Remove&lt;PhysicsBody3D&gt;(E4).
ComponentPool застосовує swap-back:

~~~text
до Remove:    [E4, E7]
після Remove: [E7]
~~~

E7 перемістилася з index 1 на index 0. Але Enumerator уже обробив index 0 і
на наступному MoveNext збільшив би index до 1. Без захисту він пропустив би E7.

Remove збільшує World.StructureVersion з 12 до 13. Тому наступний MoveNext
бачить:

~~~text
expectedVersion = 12
actualVersion   = 13
~~~

і кидає зрозумілу помилку замість тихого пропуску Entity або читання
переставлених даних.

StructureVersion є **перевіркою помилки**, а не дозволом змінювати структуру
всередині foreach. Якщо після структурної зміни одразу зробити break, наступний
MoveNext може не викликатися, але покладатися на це все одно не можна.

### Як правильно змінювати структуру

Потрібні операції спочатку записуються в EntityCommandBuffer, не торкаючись
World:

~~~csharp
var commands = world.CreateCommandBuffer();

foreach (var entity in world.Query<Transform3D, PhysicsBody3D>())
{
    var transform = world.Get<Transform3D>(entity);
    if (transform.Position.Y < -100f)
    {
        commands.Destroy(entity);
    }
}

commands.Playback(world);
~~~

Поки працює foreach, buffer лише накопичує команди, тому StructureVersion не
змінюється. Після завершення Query метод Playback виконує DestroyEntity, Add,
Remove та інші заплановані дії. Тепер перестановки у dense-масивах безпечні,
бо старий Enumerator уже більше не використовується.

## 6. Видалення та lifecycle events

DestroyEntity має навмисно строгий порядок:

1. **EntityDestroying** викликається, поки сутність і всі її компоненти ще
   доступні.
2. **ComponentRemoving** викликається окремо для кожного наявного компонента.
3. Компоненти фізично видаляються зі своїх pools.
4. alive стає false.
5. Generation збільшується.
6. Id додається до freeIds.

PhysicsSystem підписаний на ComponentRemoving. Якщо видаляється Transform3D,
PhysicsBody3D або BoxCollider3D, він одразу видаляє відповідний Jitter body,
поки ще має достатньо контексту.

Всередині lifecycle callback заборонено робити іншу структурну зміну. Це
запобігає рекурсивному видаленню та частково зміненому World.

## 7. Навіщо Generation

Приклад:

| Момент | Значення |
|---|---|
| Створили ворога | Entity(2:7) |
| Десь зберегли старе посилання | Entity(2:7) |
| Ворога видалили | Id 2 отримав Generation 8 |
| Створили монету з повторним Id | Entity(2:8) |

Перевірка World.IsAlive порівнює і Id, і Generation:

~~~text
старий Entity(2:7) != новий Entity(2:8)
~~~

Без Generation старий handle ворога раптом почав би вказувати на нову монету.

## Відповідні файли

- [Entity.cs](../../Ecs/Entity.cs)
- [World.cs](../../Ecs/World.cs)
- [ComponentPool.cs](../../Ecs/ComponentPool.cs)
- [Queries.cs](../../Ecs/Queries.cs)
- [EntityCommandBuffer.cs](../../Ecs/EntityCommandBuffer.cs)
