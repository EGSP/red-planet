# System Design — red_planet

Живой документ по архитектуре. Движок: **Godot 4.7 (.NET) + C#**, платформа — только десктоп. Здесь зафиксированы подходы, которые мы обсудили, и то, как их реализовать: где какие слои, что делать нодой, что держать просто в памяти, как связывать сущности и как ужимать журналы, не ломая регистры.

Код в примерах — набросок (псевдо-C#), для передачи идеи, а не готовый к сборке.

---

## 1. Философия

В основе — модель метаданных 1С, перенесённая на игру. По-геймдевному это привычные паттерны под другими именами:

| 1С | В игре | Известный паттерн |
|---|---|---|
| Справочник | Config | data-driven определения (Resource/.tres) |
| Документ | Record | event sourcing — иммутабельное событие в журнале |
| Регистр накопления | Register | CQRS-проекция — производный агрегат |
| — | System | логика над мини-ECS |

Почему это уместно для стратегии-базостроя: **экономика базы — это по сути учёт.** Производство, добыча, стройка — документы; склады и население — регистры; типы юнитов и зданий — справочники. Мы мыслим ровно теми категориями, под которые заточена предметная область.

**Ценности, под которые оптимизируем** (важно — они диктуют решения ниже):

1. Точечный контроль хода исполнения — свой планировщик, а не неявный обход дерева нод.
2. Наблюдаемость зависимостей — единый композиционный корень.
3. Конфигурируемость под геймдизайн и DX — настройка в редакторе (сигналы, `[Export]`, `.tres`).
4. Производительность — по мере необходимости, не догма. Но для долгих партий и большого числа сущностей закладываем инкрементальность сразу.

**Чего пока НЕ делаем:** сетевых кривых/ChronoCam, DI-фреймворка, детерминированного lockstep. Игра одиночная/локальная, сервер и сеть — отложены.

---

## 2. Слои и поток данных

Слои:

- **Config (справочник)** — статические определения. Ассет `.tres`, грузится в память.
- **Record (документ)** — событие. Живёт в журнале в памяти.
- **Register (регистр)** — производный агрегат. Живёт в памяти, обновляется инкрементально.
- **System (система)** — логика. Нода со свойствами `[Export]`.
- **Entity + Components (мини-ECS)** — сущности мира. Видимые/физические — ноды; их данные — обычные C#-объекты.
- **Scheduler (планировщик)** — порядок систем и фазы кадра. Чистый объект в корне.
- **GameManager (корень)** — держит всё вышеперечисленное. Нода-автозагрузка.

Поток за один тик:

```
намерение (игрок/ИИ)
   → система валидирует и публикует ДОКУМЕНТ в журнал
      → РЕГИСТР впитывает документ дельтой (в момент публикации)
      → системы РЕАГИРУЮТ на документы кадра (спавн, апгрейд, VFX)
   → cleanup: транзиентные журналы чистятся, отложенные удаления применяются
отрисовка — отдельно, вне тика симуляции
```

Ключ развязки: **системы не дёргают друг друга напрямую — они общаются через документы.** Система стройки не знает про систему мира; она публикует `BuildingPlacedRecord`, а мир на него реагирует.

---

## 3. Где что живёт: нода, память или ассет

Главный принцип:

> **Нода — только там, где нужен движок:** отрисовка, физика, ввод, участие в дереве сцены, настройка в редакторе. Всё, что чистая игровая логика и данные, — обычные C#-объекты в памяти под композиционным корнем. Так data-слой остаётся независимым от движка и тестируемым, а ноды — это «кожа», связывающая его с Godot.

| Слой | Форма в Godot | Где живёт | Пример |
|---|---|---|---|
| GameManager (корень) | **нода-автозагрузка** (autoload) | в дереве, всегда | держит журнал, регистры, каталог, планировщик |
| Scheduler (планировщик) | чистый C# внутри GameManager | память | порядок систем, фазы |
| Journal + Records | чистый C# | память (в корне) | `Journal`, `BuildingPlacedRecord` |
| Registers | чистый C# | память (в корне) | `StockpileRegister` |
| Configs (справочники) | **Resource** (`.tres`) | ассет на диске → в память | `BuildingDef`, `UnitDef` |
| Systems | **нода** с `[Export]` | в сцене, добавляешь/настраиваешь | `BuildSystem`, `UnitSystem` |
| Видимые/физические сущности | **нода** (`Node2D`/`CharacterBody2D`) | в сцене | `Unit`, `Building` |
| Компоненты-данные сущности | чистый C#, которым владеет нода | память | `Health`, `Inventory` |
| Связи между сущностями | стабильный `EntityId`, не ссылка | — | документы несут id |
| События-связки | **сигналы Godot** | — | connect в редакторе или коде |

---

## 4. Слои подробно

### 4.1 GameManager — композиционный корень

Одна нода-автозагрузка держит всю инфраструктуру. Это намеренный выбор: в игре всё равно есть глобальный менеджер, и как единая точка сборки он делает зависимости видимыми, а не прячет их.

```csharp
public partial class GameManager : Node
{
    public static GameManager Instance { get; private set; }

    public Journal Journal { get; } = new();
    public RegisterRegistry Registers { get; } = new();
    public Catalog Catalog { get; } = new();
    public EntityRegistry Entities { get; } = new();
    public WorldGrid Grid { get; private set; }

    private readonly Scheduler _scheduler = new();
    private int _lastId;

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        Catalog.LoadAll();                        // справочники
        Registers.Add(new StockpileRegister());   // регистры
        Registers.Add(new PopulationRegister());
        Registers.WireAll(Journal);               // подписки регистров на потоки документов
    }

    public int NewId() => ++_lastId;              // выдача стабильных EntityId

    // Системы-ноды регистрируются из своего _Ready:
    public void RegisterSystem(GameSystem s, Phase phase) => _scheduler.Add(s, phase);

    public override void _PhysicsProcess(double dt) => _scheduler.RunFrame(dt);
}
```

Нюанс в рамках подхода: при росте графа зависимостей порядок инициализации регистров держать **явным** (как здесь — список `Add` по порядку), а не полагаться на удачную последовательность.

### 4.2 Справочники (Configs) — Resource / .tres

Статические определения. Класс — подкласс `Resource` (в `scripts/data/Configs`), экземпляры — `.tres` (в `resources/`). Каталог грузит их и кладёт в словарь по ключу — не линейным поиском по строке.

```csharp
[GlobalClass]
public partial class BuildingDef : Resource
{
    [Export] public string Id;
    [Export] public PackedScene Scene;      // сцена здания
    [Export] public int WoodCost;
    [Export] public Vector2I Size = new(1, 1);
}

public sealed class Catalog
{
    private readonly Dictionary<string, BuildingDef> _buildings = new();
    public BuildingDef Building(string id) => _buildings[id];

    public void LoadAll()
    {
        foreach (var def in LoadDir<BuildingDef>("res://resources/buildings/"))
            _buildings[def.Id] = def;
    }
}
```

Добавить новый тип здания = создать `.tres` в редакторе, кода не трогая.

### 4.3 Документы (Records) — журнал в памяти

Иммутабельное событие. Интерфейс `IRecord` живёт в `scripts/data`. Транзиентные (нужны один кадр) помечаем маркером — их чистим в cleanup.

```csharp
public interface IRecord { int SequenceId { get; set; } }
public interface ITransientRecord : IRecord { }   // живёт один кадр

public struct BuildingPlacedRecord : IRecord
{
    public int SequenceId { get; set; }
    public int BuildingId;      // стабильный EntityId, НЕ ссылка на ноду
    public string DefId;        // ключ справочника
    public Vector2I Cell;
    public int OwnerId;
}
```

Журнал — набор типизированных потоков. Поток и хранит записи (для чтения за кадр), и публикует событие (для инкрементальных регистров):

```csharp
public sealed class RecordStream<T> where T : IRecord
{
    private readonly List<T> _records = new();
    private int _lastSeq;

    public event Action<T> Published;             // регистры подписываются сюда
    public IReadOnlyList<T> Records => _records;   // системам, читающим за кадр

    public void Add(T rec)
    {
        rec.SequenceId = ++_lastSeq;
        _records.Add(rec);
        Published?.Invoke(rec);                    // проекция в момент публикации
    }

    public void Clear() => _records.Clear();       // фаза cleanup (для транзиентных)
}

public sealed class Journal
{
    private readonly Dictionary<Type, object> _streams = new();

    public RecordStream<T> Stream<T>() where T : IRecord
    {
        if (!_streams.TryGetValue(typeof(T), out var s))
            _streams[typeof(T)] = s = new RecordStream<T>();
        return (RecordStream<T>)s;
    }

    public void Publish<T>(T rec) where T : IRecord => Stream<T>().Add(rec);
}
```

### 4.4 Регистры (Registers) — инкрементальные проекции

Регистр держит агрегат и обновляет его **дельтой в момент публикации документа**, а не пересчётом из журнала. Это важнейшее изменение относительно прошлого прототипа: так регистр не зависит от истории журнала — и журнал можно резать свободно (см. раздел 5).

```csharp
public abstract class Register
{
    public event Action Changed;
    protected void NotifyChanged() => Changed?.Invoke();
    public abstract void Wire(Journal journal);   // подписки на потоки
}

public sealed class StockpileRegister : Register
{
    private readonly Dictionary<string, long> _amount = new();  // resId -> кол-во
    public long Get(string resId) => _amount.GetValueOrDefault(resId);

    public override void Wire(Journal j)
    {
        j.Stream<ResourceGainedRecord>().Published += r => Apply(r.ResId, +r.Amount);
        j.Stream<ResourceSpentRecord>().Published  += r => Apply(r.ResId, -r.Amount);
    }

    private void Apply(string resId, long delta)
    {
        _amount[resId] = Get(resId) + delta;
        NotifyChanged();
    }
}
```

`RegisterRegistry` хранит регистры по типу (`Dictionary<Type, Register>`), `Get<T>()` достаёт нужный, `WireAll(journal)` подписывает всех.

### 4.5 Системы (Systems) — ноды с `[Export]`

Система — нода: её добавляют в сцену и настраивают в инспекторе (это DX-выбор). Базовый класс даёт доступ к корню и точки входа по фазам.

```csharp
public partial class GameSystem : Node
{
    protected GameManager GM => GameManager.Instance;

    public override void _Ready() => OnRegister();
    protected virtual void OnRegister() {}         // подписки, регистрация в планировщике
    public virtual void Simulate(double dt) {}     // фаза симуляции
    public virtual void React(double dt) {}        // фаза реакции на документы кадра
}
```

Пример: система стройки валидирует запрос (через регистр и сетку) и публикует документы. Заметь — она не создаёт здание сама, только фиксирует факт:

```csharp
public partial class BuildSystem : GameSystem
{
    [Export] public string DefaultBuilding = "hq";

    public void RequestPlace(string defId, Vector2I cell, int ownerId)
    {
        var def = GM.Catalog.Building(defId);
        if (!GM.Grid.IsFree(cell, def.Size)) return;                     // занятость сетки
        if (GM.Registers.Get<StockpileRegister>().Get("wood") < def.WoodCost) return;

        GM.Journal.Publish(new ResourceSpentRecord  { ResId = "wood", Amount = def.WoodCost });
        GM.Journal.Publish(new BuildingPlacedRecord { BuildingId = GM.NewId(),
                                                      DefId = defId, Cell = cell, OwnerId = ownerId });
    }
}
```

А система мира реагирует на документ — вот тут появляется нода и занимается сетка:

```csharp
public partial class WorldSystem : GameSystem
{
    protected override void OnRegister()
    {
        GM.RegisterSystem(this, Phase.React);
        GM.Journal.Stream<BuildingPlacedRecord>().Published += Spawn;
    }

    private void Spawn(BuildingPlacedRecord r)
    {
        var def = GM.Catalog.Building(r.DefId);
        var node = def.Scene.Instantiate<Building>();
        node.Init(r.BuildingId, r.OwnerId);
        AddChild(node);
        GM.Entities.Add(r.BuildingId, node);        // связь id -> нода
        GM.Grid.Occupy(r.Cell, def.Size, r.BuildingId);
    }
}
```

### 4.6 Сущности и компоненты (мини-ECS)

- **Видимое/физическое** (юнит, здание, снаряд) — нода (`Node2D`, `CharacterBody2D`, `Area2D`), потому что нужны отрисовка, коллайдеры, рейкасты, сигналы движка.
- **Данные сущности** (`Health`, `Inventory`, `Owner`) — обычные C#-объекты, которыми владеет нода. Не плодим ноды на каждую характеристику.
- У сущности стабильный `EntityId`, по которому её находят из документов и регистра сущностей.

```csharp
public partial class Building : Node2D
{
    public int Id { get; private set; }
    public Owner Owner { get; private set; }
    public Health Health { get; private set; } = new(100);

    public void Init(int id, int ownerId) { Id = id; Owner = new(ownerId); }
}
```

### 4.7 Планировщик и фазы кадра

Свой планировщик даёт то, ради чего мы и отказались от голого `_Process`: явный порядок и фазы. Один `_PhysicsProcess` корня прогоняет кадр целиком.

```csharp
public enum Phase { Input, Simulate, React, Cleanup }

public sealed class Scheduler
{
    private readonly Dictionary<Phase, List<GameSystem>> _byPhase = new();

    public void Add(GameSystem s, Phase p) =>
        (_byPhase.TryGetValue(p, out var l) ? l : _byPhase[p] = new()).Add(s);

    public void RunFrame(double dt)
    {
        foreach (var s in Phase(Phase.Input))    s.Simulate(dt);   // сбор намерений
        foreach (var s in Phase(Phase.Simulate)) s.Simulate(dt);   // движение, добыча, бой → документы
        // регистры уже впитали документы в момент Publish (инкрементально)
        foreach (var s in Phase(Phase.React))    s.React(dt);      // реакция на документы кадра
        foreach (var s in Phase(Phase.Cleanup))  s.React(dt);      // отложенные удаления
        GameManager.Instance.Journal.ClearTransient();            // чистка транзиентных потоков
    }

    private List<GameSystem> Phase(Phase p) => _byPhase.GetValueOrDefault(p) ?? _empty;
    private static readonly List<GameSystem> _empty = new();
}
```

Отрисовка и интерполяция визуала — в `_Process` соответствующих нод, вне тика симуляции.

### 4.8 События-связки — сигналы Godot

Там, где нужна конфигурируемая связь (особенно UI и подача эффектов), используем **сигналы Godot** — их соединяют и в редакторе визуально, и в коде. Это ровно та настраиваемость под геймдизайн, к которой мы шли; в Godot она родная.

Правило разделения: **внутри симуляции связи идут через документы** (для сейва и порядка это надёжнее), **сигналы — на границе с презентацией** (регистр изменился → сигнал → полоска ресурса обновилась).

```csharp
public override void _Ready()
{
    GM.Registers.Get<StockpileRegister>().Changed += () => EmitSignal(SignalName.StockpileChanged);
}
```

---

## 5. Журналы: как ужимать и не сломать регистры

Наивный журнал только дописывается и растёт всю сессию. Резать его нельзя бездумно: потребители читают по курсору `SequenceId`, а регистры проецируются из событий — обрежешь не то, и проекция разъедется.

**Ключевой принцип:** регистр — это проекция журнала. Обрезать журнал безопасно только до точки, которую все проекции и потребители уже впитали. Отсюда две дороги: либо сделать регистры независимыми от истории, либо знать «водяной знак» — докуда дошли все.

### Стратегия 1 — Инкрементальные регистры (основная, делает журнал необязательным для регистров)

Регистр обновляет агрегат дельтой в момент публикации записи (раздел 4.4), а не пересчётом из журнала. Тогда его состояние **не зависит от того, лежит запись в журнале или нет** — журнал можно резать свободно, регистр самодостаточен.

Это дефолт. Пока регистры инкрементальные, обрезка журнала их в принципе не задевает.

### Стратегия 2 — Фазовая очистка транзиентных записей

Большинство документов транзиентны: `EnemyDiedRecord`, `DamageRecord`, `ClickRecord` нужны только в этом кадре, чтобы системы среагировали. Помечаем такие типы маркером `ITransientRecord`; в фазе cleanup их потоки чистятся целиком (`Clear`), потому что все, кто должен был, уже отработали за кадр. Курсоры не нужны.

```csharp
public void ClearTransient()
{
    foreach (var stream in _transientStreams) stream.Clear();
}
```

### Стратегия 3 — Водяной знак (для записей, живущих дольше кадра)

Если запись читают несколько потребителей в разном темпе, поток держит по курсору на потребителя и режет всё ниже минимального. Потребитель двигает свой курсор после обработки; компакция — до `min(курсоров)`.

```csharp
// абсолютные SequenceId у курсоров, чтобы не сбивались при обрезке
private readonly Dictionary<string, int> _cursor = new();

public IEnumerable<T> ReadNew(string consumer)
{
    int from = _cursor.GetValueOrDefault(consumer, 0);
    foreach (var r in _records) if (r.SequenceId > from) yield return r;
    _cursor[consumer] = _records.Count > 0 ? _records[^1].SequenceId : from;
}

public void Compact()
{
    int keep = _cursor.Count > 0 ? _cursor.Values.Min() : int.MaxValue;
    _records.RemoveAll(r => r.SequenceId <= keep);
}
```

Берём это только там, где инкрементального регистра и фазовой чистки не хватает.

### Стратегия 4 — Снапшоты + усечение (для сейвов и долгой истории)

Если история нужна для сохранения (а в будущем — для сети): периодически снимаем снапшот состояния регистров и усекаем журнал до точки снапшота. Восстановление = снапшот + хвост журнала. Это классический event-sourcing snapshotting. Для одиночной игры снапшот регистров — это и есть основа сейва.

### Стратегия 5 — Кольцевой буфер

Для потоков с фиксированным горизонтом (нужны последние N событий) — кольцевой буфер: старое затирается новым, роста нет.

### Правило, чтобы регистры не ломались

- **Регистр инкрементальный (Стратегия 1)** — обрезка журнала его не трогает. Так по умолчанию.
- **Если какой-то регистр всё же считается из журнала** (иногда так проще) — усечение обязано идти через водяной знак, который учитывает курсор этого регистра. Порядок: регистр фиксирует свой агрегат (снапшот) → и только потом разрешаем резать журнал до его курсора. Никогда не режем журнал ниже точки, которую регистр ещё не учёл.

Практический дефолт для red_planet: **регистры инкрементальные + транзиентные документы чистятся по фазе + снапшот регистров для сейва.** Водяной знак — точечно, где реально нужен многопоточный разбор долгоживущих записей.

---

## 6. Связывание сущностей

- У каждой сущности **стабильный `EntityId`** (счётчик в `GameManager.NewId()`).
- **Документы несут `EntityId` и значения, а не ссылки на ноды.** Причины: нода может быть удалена (снаряд, погибший юнит), а документ должен пережить это; документы сериализуются для сейва; когда-нибудь — реплицируются.
- **`EntityRegistry`** — словарь `EntityId → сущность/нода` — даёт обратный поиск, когда система реагирует на документ и ей нужна живая нода.
- Внутри одного кадра ноды могут держать прямые ссылки для «горячих» связей. Но **всё, что пересекает слои** (документы, регистры, сейв), — только через id.
- **`Owner` (playerId)** на сущности — под будущие co-op-права, даже если сейчас игрок один.

```csharp
public sealed class EntityRegistry
{
    private readonly Dictionary<int, Node> _byId = new();
    public void Add(int id, Node node) => _byId[id] = node;
    public void Remove(int id) => _byId.Remove(id);
    public Node? Get(int id) => _byId.GetValueOrDefault(id);
}
```

---

## 7. Размещение в проекте

- `scripts/core/` — `GameManager`, `Scheduler`, `Phase`, `EntityRegistry`, базовые контракты (`GameSystem`).
- `scripts/data/` — инфраструктура данных: `IRecord`/`ITransientRecord`, `RecordStream`, `Journal`, `Register`, `RegisterRegistry`, `Catalog`.
- `scripts/data/Configs/` — подклассы `Resource` (`BuildingDef`, `UnitDef`). Экземпляры `.tres` — в `resources/`.
- `scripts/data/Records/` — конкретные документы.
- `scripts/data/Registers/` — конкретные регистры.
- `scripts/units/`, `scripts/buildings/`, `scripts/world/` — ноды-сущности и их системы (либо, если удобнее, отдельная папка `scripts/systems/`).
- `scripts/ui/` — UI, слушает сигналы регистров.

Data-слой (`scripts/data`, `scripts/core`) почти не зависит от Godot — его можно покрывать обычными юнит-тестами без движка.

---

## 8. Отложено / открытые вопросы

- Сеть и мультиплеер — отложены (документы = естественные кандидаты на репликацию, когда вернёмся).
- Точный формат сейва (снапшот регистров + справочники + сущности).
- Пафайндинг: `AStarGrid2D` по сетке застройки + движение в непрерывном пространстве.
- Нужна ли отдельная папка `scripts/systems/` или системы живут по доменам — решим по мере роста.
