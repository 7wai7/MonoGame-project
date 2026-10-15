# 4. Sparse set та EntityQuery

## Два масиви з різними завданнями

Кожен ComponentPool зберігає три пов'язані масиви:

~~~mermaid
flowchart LR
    Id["Entity.Id"] --> Sparse["sparse[Id]<br/>dense index + 1"]
    Sparse --> DenseEntity["entities[denseIndex]<br/>повний Entity handle"]
    DenseEntity --> DenseComponent["components[denseIndex]<br/>значення компонента"]

    classDef sparse fill:#fef3c7,stroke:#d97706,color:#451a03
    classDef dense fill:#dcfce7,stroke:#16a34a,color:#052e16
    class Id,Sparse sparse
    class DenseEntity,DenseComponent dense
~~~

- **sparse** індексується за Entity.Id і швидко відповідає, де шукати компонент;
- **entities** щільно зберігає Entity, які мають цей компонент;
- **components** у тих самих dense-індексах зберігає значення компонентів.

Значення sparse дорівнює **denseIndex + 1**, тому що нуль означає «компонента
немає».

## Початковий приклад

Нехай Transform3D мають Entity 2, 7 і 5. Pool може виглядати так:

### sparse

| Entity.Id | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Значення | 0 | 0 | 1 | 0 | 0 | 3 | 0 | 2 |

### dense

| denseIndex | entities | components |
|---:|---|---|
| 0 | Entity(2:1) | Transform A |
| 1 | Entity(7:3) | Transform B |
| 2 | Entity(5:2) | Transform C |

Щоб знайти Transform для Entity(7:3):

1. Беремо sparse[7], отримуємо 2.
2. Віднімаємо 1, отримуємо denseIndex 1.
3. Перевіряємо, що entities[1] справді дорівнює Entity(7:3).
4. Повертаємо components[1], тобто Transform B.

Остання перевірка включає Generation, тому stale Entity з тим самим Id не
отримає компонент нової сутності.

## Чому dense-масив щільний

Система часто обходить усі Transform3D. Послідовні дані:

~~~text
Transform A, Transform B, Transform C, Transform D...
~~~

краще використовують кеш процесора, ніж розкидані об'єкти та порожні місця.
Тому iteration проходить dense-масив від 0 до Count - 1.

## Видалення через swap-back

Видалимо Entity(7:3), яка знаходиться посередині:

### Крок 1. Початковий стан

Count дорівнює 3. Entity(7:3) знаходиться за denseIndex 1, а останній активний
елемент — Entity(5:2) за denseIndex 2.

**Dense-масиви:**

| denseIndex | entities | components | Стан |
|---:|---|---|---|
| 0 | Entity(2:1) | Transform A | залишається |
| 1 | Entity(7:3) | Transform B | видаляємо |
| 2 | Entity(5:2) | Transform C | останній елемент |

**Відповідні значення sparse:**

| Entity.Id | sparse[Id] | На який denseIndex вказує |
|---:|---:|---:|
| 2 | 1 | 0 |
| 5 | 3 | 2 |
| 7 | 2 | 1 |

### Крок 2. Знаходимо потрібні індекси

ComponentPool обчислює:

| Змінна | Обчислення | Значення |
|---|---|---:|
| removedIndex | sparse[7] - 1 | 1 |
| lastIndex | Count - 1 | 2 |

removedIndex не дорівнює lastIndex. Отже, видаляється не останній елемент і
порожнє місце потрібно заповнити.

### Крок 3. Копіюємо останній елемент у місце видаленого

Entity(5:2) і Transform C копіюються з index 2 в index 1:

| denseIndex | entities | components | Що сталося |
|---:|---|---|---|
| 0 | Entity(2:1) | Transform A | без змін |
| 1 | Entity(5:2) | Transform C | скопійовано з index 2 |
| 2 | Entity(5:2) | Transform C | старе місце ще не очищене |

Дублікат на index 2 існує лише всередині методу Remove протягом кількох
операцій. Зовнішній код у цей момент pool не бачить.

### Крок 4. Виправляємо sparse для переміщеної Entity

Entity(5:2) тепер знаходиться за denseIndex 1. Sparse зберігає index + 1, тому:

~~~text
sparse[5] = 1 + 1 = 2
~~~

| Entity.Id | sparse[Id] до | sparse[Id] після | Новий denseIndex |
|---:|---:|---:|---:|
| 5 | 3 | 2 | 1 |

Без цього оновлення наступний Get(Entity(5:2)) продовжив би шукати компонент
за старим index 2.

### Крок 5. Позначаємо Entity(7:3) відсутньою

Нуль у sparse означає «компонента немає»:

| Entity.Id | sparse[Id] до | sparse[Id] після |
|---:|---:|---:|
| 7 | 2 | 0 |

Після цього Has(Entity(7:3)) поверне false.

### Крок 6. Очищаємо останній slot і зменшуємо Count

~~~text
entities[2] = default
components[2] = default
Count = 2
~~~

Фінальний активний діапазон dense-масивів:

| denseIndex | entities | components |
|---:|---|---|
| 0 | Entity(2:1) | Transform A |
| 1 | Entity(5:2) | Transform C |

Фізична довжина масивів не зменшується — змінився Count, тому index 2 більше
не входить до активного діапазону.

Якби видалявся одразу останній елемент, removedIndex дорівнював би lastIndex.
Копіювання та виправлення sparse переміщеної Entity тоді не потрібні.

Порядок Entity після swap-back не гарантується, зате видалення не потребує
зсувати всі наступні елементи й зазвичай виконується за O(1).

## Як EntityQuery знаходить перетин

Уявімо два pools:

| Pool | Count | Dense entities |
|---|---:|---|
| Transform3D | 5 | E2, E7, E5, E9, E12 |
| PhysicsBody3D | 3 | E7, E8, E9 |

Для Query&lt;Transform3D, PhysicsBody3D&gt;:

### Крок 1. Обираємо найменший pool

Enumerator порівнює Count:

| Pool | Count | Результат |
|---|---:|---|
| Transform3D | 5 | більший |
| PhysicsBody3D | 3 | **smallest pool** |

Тому кандидатами будуть лише E7, E8 та E9. Query не потрібно починати з усіх
п'яти Entity у Transform3D pool.

### Крок 2. Перевіряємо першого кандидата E7

| Перевірка | sparse lookup | Результат |
|---|---|---|
| Transform3D.Has(E7) | sparse[7] → відповідний dense element E7 | true |
| PhysicsBody3D.Has(E7) | sparse[7] → відповідний dense element E7 | true |

Усі потрібні компоненти присутні, тому Enumerator встановлює Current = E7 і
повертає її у foreach.

### Крок 3. Перевіряємо другого кандидата E8

| Перевірка | sparse lookup | Результат |
|---|---|---|
| Transform3D.Has(E8) | sparse[8] = 0 | false |
| PhysicsBody3D.Has(E8) | sparse[8] → відповідний dense element E8 | true |

E8 не має Transform3D. Кандидат відкидається, Current не змінюється, а
Enumerator всередині того самого MoveNext переходить до наступного кандидата.

### Крок 4. Перевіряємо третього кандидата E9

| Перевірка | sparse lookup | Результат |
|---|---|---|
| Transform3D.Has(E9) | sparse[9] → відповідний dense element E9 | true |
| PhysicsBody3D.Has(E9) | sparse[9] → відповідний dense element E9 | true |

Обидві перевірки успішні, тому Current = E9 і foreach отримує E9.

### Крок 5. Завершуємо Query

Після E9 індекс досягає Count найменшого pool. MoveNext повертає false.

| Кандидат зі smallest pool | Має Transform3D | Має PhysicsBody3D | Результат |
|---|---|---|---|
| E7 | так | так | повернути |
| E8 | ні | так | пропустити |
| E9 | так | так | повернути |

Фінальний перетин:

~~~text
[E7, E9]
~~~

Query перевірив лише трьох кандидатів із найменшого pool. Кожна перевірка Has
використовує швидкий sparse lookup.

Для трьох або чотирьох типів логіка та сама:

1. обрати pool із найменшим Count;
2. взяти чергову Entity з його dense-масиву;
3. викликати Has у кожному потрібному pool;
4. повернути Entity лише тоді, коли всі перевірки успішні.

## Що відбувається у foreach

~~~csharp
foreach (var entity in world.Query<Transform3D, PhysicsBody3D>())
{
    ref var transform = ref world.Get<Transform3D>(entity);
    var body = world.Get<PhysicsBody3D>(entity);
}
~~~

Компілятор C# приблизно перетворює це на:

~~~text
enumerator = query.GetEnumerator()
while (enumerator.MoveNext())
{
    entity = enumerator.Current
    ...
}
~~~

MoveNext:

1. перевіряє StructureVersion;
2. бере наступного кандидата з smallest pool;
3. перевіряє наявність усіх компонентів;
4. встановлює Current;
5. повертає true або продовжує пошук.

## Вартість операцій

| Операція | Типова складність |
|---|---|
| Has / Get компонента | O(1) |
| Add компонента | O(1), іноді розширення масиву |
| Remove компонента | O(1) через swap-back |
| Query | O(розмір найменшого pool × кількість типів) |

## Важливі наслідки

- Порядок Entity у query не є стабільним після видалень.
- Не можна зберігати denseIndex надовго: swap-back може його змінити.
- Зберігати Entity handle можна, бо World перевіряє Generation.
- Значення компонента можна змінювати під час query.
- Набір компонентів під час query змінюють через EntityCommandBuffer після
  завершення обходу.

## Відповідні файли

- [ComponentPool.cs](../../Ecs/ComponentPool.cs)
- [Queries.cs](../../Ecs/Queries.cs)
- [World.cs](../../Ecs/World.cs)
