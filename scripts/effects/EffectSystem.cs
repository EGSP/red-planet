using System.Collections.Generic;
using Godot;

/// <summary>
/// Эффекты мира: узлы частиц, объявленные в сценах моделей, связываются здесь с сущностями
/// и проигрываются отсюда.
///
/// ЗАЧЕМ СИСТЕМА, А НЕ КОД В САМОМ УЗЛЕ. Узел изображения не знает, чей он и что вокруг
/// происходит: пыль не должна разыскивать носителя, а вспышка выстрела — угадывать, какому
/// из двух стволов она принадлежит. Связывание есть работа того, кто видит обе стороны —
/// справочник инструментов и дерево модели, — и живёт в одном месте по той же причине,
/// по которой в одном месте живёт <see cref="Spawner"/>.
///
/// ДВА РОДА ПОВОДОВ, И РАЗЛИЧАЮТСЯ ОНИ ИСТОЧНИКОМ. Ход есть непрерывное состояние: фактов
/// он не порождает, и пыль считается по скорости, читаемой каждый кадр. Выстрел и попадание
/// суть факты, они уже лежат в журнале документами <see cref="WeaponFired"/>
/// и <see cref="DamageDealt"/>, и разбираются пачкой за кадр. Заводить документ на движение
/// было бы ошибкой: он писался бы каждый кадр на каждого юнита, то есть журнал стал бы
/// копией состояния мира.
///
/// ГРАНИЦА С СИМУЛЯЦИЕЙ. Система только читает. Она не дописывает документов, не трогает
/// индекс и ни на что в мире не влияет, поэтому снятие всего слоя эффектов не меняет хода
/// партии. Отсюда её место: фаза <see cref="Phase.React"/> физического цикла — документы
/// кадра чистятся в конце того же цикла, и разбирать их из графического значило бы
/// прочитать одну пачку дважды либо не прочитать вовсе.
///
/// СЛОЙ ВЫБИРАЕТСЯ ПО РОДУ ЭФФЕКТА. Пыль переносится на <see cref="WorldLayer.GroundEffects"/>:
/// узел лежит в сцене модели, то есть внутри дерева юнита, и рисовался бы поверх корпуса,
/// а поднимается он от грунта. Вспышка выстрела, наоборот, остаётся при стволе — она ему
/// и принадлежит. Попадание рождается там, где ни модели, ни носителя нет вовсе, и потому
/// живёт на <see cref="WorldLayer.AirEffects"/> общим набором.
/// </summary>
public partial class EffectSystem : GameSystem
{
    /// <summary>
    /// Запасной вид вспышки попадания: им показывается урон, у которого своего вида нет, —
    /// от оружия, не объявившего <see cref="ImpactEffect"/>, и от сноса, у которого
    /// инструмента нет вовсе. Своя вспышка объявляется в модели, внутри части-инструмента,
    /// рядом со вспышкой выстрела.
    /// </summary>
    [Export] public PackedScene Impact { get; set; }

    /// <summary>
    /// Сколько частиц вмещает одно общее поле — см. <see cref="ParticleField"/>. Запас
    /// кольцевой: когда вбросов приходит больше, новые частицы вытесняют самые старые.
    ///
    /// ЧИСЛО ОБЩЕЕ НА ВСЕ ПОЛЯ, а не своё у каждого, поскольку поле заводится по признаку
    /// материала, а не по роду события: разделять запас искр попадания и запас дыма
    /// значило бы подписывать одно и то же число дважды. Полей немного — по одному
    /// на различный материал частиц, — поэтому общий запас обходится дёшево.
    /// </summary>
    [Export] public int FieldCapacity { get; set; } = 1024;

    /// <summary>
    /// Взрыв при гибели. Общий на все виды — и на юниты, и на постройки: разные взрывы
    /// показывать пока нечем. Когда понадобятся, ссылка переедет в сцену модели, где
    /// у вида уже лежит всё прочее его изображение, а это поле останется общим запасным.
    /// </summary>
    [Export] public PackedScene Explosion { get; set; }

    /// <summary>
    /// Сколько отметин копоти держать на карте — см. <see cref="ScorchField.Capacity"/>.
    /// Задаётся здесь потому, что слой отпечатков поднимается этой системой: собственного
    /// узла в сцене сессии у него нет, как нет его и у вспышек попадания.
    /// </summary>
    [Export] public int ScorchCapacity { get; set; } = 96;

    /// <summary>
    /// Связка «поток пыли — его носитель». Смещение и угол сняты запеканием в системе
    /// координат корпуса; узла частиц здесь нет вовсе, поскольку выпускает их общее поле.
    /// </summary>
    private sealed class Trail
    {
        public EffectBake Effect;
        public IMobile Owner;
        public Vector2 Offset;
        public float Angle;

        /// <summary>
        /// Дробный остаток потока по каждой части эффекта. За кадр частиц выходит меньше
        /// одной, и без накопления остатка поток либо не пошёл бы вовсе, либо шёл бы
        /// вдесятеро гуще — см. <see cref="ParticleYard.Stream"/>.
        /// </summary>
        public float[] Debts;

    }

    /// <summary>
    /// Вспышка выстрела при стволе: ключ инструмента, запечённый эффект и место вспышки
    /// в осях самой части.
    ///
    /// ССЫЛКА НА ЧАСТЬ НУЖНА ПОТОМУ, ЧТО СТВОЛ ПОВОРАЧИВАЕТСЯ ОТДЕЛЬНО ОТ КОРПУСА.
    /// Узла вспышки, который прежде ездил вместе со стволом сам собой, больше нет,
    /// и преобразование приходится спрашивать у части в миг выстрела.
    /// </summary>
    private readonly struct Flash
    {
        public readonly string ToolId;
        public readonly Node2D Part;
        public readonly EffectBake Effect;
        public readonly Vector2 Offset;
        public readonly float Angle;

        public Flash(string toolId, Node2D part, ModelBake.Emitter emitter)
        {
            ToolId = toolId;
            Part = part;
            Effect = emitter.Effect;
            Offset = emitter.Offset;
            Angle = emitter.Angle;
        }
    }

    private readonly Dictionary<object, List<Trail>> _trails = new(ByReference.Instance);

    private readonly Dictionary<object, Flash[]> _flashes = new(ByReference.Instance);

    /// <summary>
    /// Вспышка попадания, объявленная у ствола: ключ его инструмента, сцена и размер.
    /// Хранится ссылкой на сцену, а не узлом: разрыв случается там, где ни модели,
    /// ни носителя нет, и частицы для него выпустит общее поле.
    /// </summary>
    private readonly struct Blow
    {
        public readonly string ToolId;
        public readonly PackedScene Scene;
        public readonly float Size;

        public Blow(string toolId, PackedScene scene, float size)
        {
            ToolId = toolId;
            Scene = scene;
            Size = size;
        }
    }

    private readonly Dictionary<object, Blow[]> _impacts = new(ByReference.Instance);

    /// <summary>
    /// Всё, что нужно знать о гибели сущности, снятое заранее.
    ///
    /// ЗАЧЕМ СНИМОК ПОЛОЖЕНИЯ. Документ хранит точку гибели, но не поворот, а смещения
    /// взрывов заданы в системе координат корпуса: у повёрнутой машины они легли бы мимо.
    /// Спросить поворот в момент разбора документа не у кого — сущность к этому мигу уже
    /// сняла себя с игры, — поэтому преобразование носителя запоминается каждый кадр,
    /// пока он жив. Обходится это одним чтением на сущность и только для тех видов,
    /// которые взрыв в модели объявили.
    /// </summary>
    private sealed class Burial
    {
        public int Id;
        public Node2D Carrier;
        public Transform2D Snapshot;
        public ModelBake.Wreck[] Items;
    }

    /// <summary>Взрыв, ждущий своей задержки. Место и поворот уже сосчитаны.</summary>
    private struct Delayed
    {
        public PackedScene Scene;
        public Vector2 Pos;
        public float Angle;
        public float Size;
        public float Left;
    }

    private readonly Dictionary<object, Burial> _burials = new(ByReference.Instance);
    private readonly Dictionary<int, Burial> _byId = new();
    private readonly List<Burial> _closed = new();
    private readonly List<Delayed> _delayed = new();

    /// <summary>Общие поля частиц — см. <see cref="ParticleYard"/>.</summary>
    private ParticleYard _yard;

    private ScorchField _scorch;

    protected override void OnLink()
    {
        _yard = new ParticleYard(GM, FieldCapacity);

        GM.Index.Watch<IMobile>(OnMobileAdded, OnMobileRetired, this);
        GM.Index.Watch<IArmed>(OnArmedAdded, OnArmedRetired, this);
        GM.Index.Watch<IDamageable>(OnDeadlyAdded, OnDeadlyRetired, this);
    }

    /// <summary>
    /// Разбор документов шага. Остаётся в физическом цикле по единственной причине:
    /// транзиентные документы живут один шаг — <c>Events.ClearTransient</c> опустошает
    /// потоки в конце каждого, — и разбирать их раз в кадр значило бы терять вспышки
    /// и взрывы всех шагов кадра, кроме последнего.
    ///
    /// <see cref="Track"/> тоже принадлежит шагу: снимок преобразования носителя нужен
    /// <see cref="Blast"/> того же шага, а к следующему кадру носитель уже выметен из мира.
    /// </summary>
    public override void Step(double dt)
    {
        foreach (var shot in GM.Events.Stream<WeaponFired>().Records)
            Flare(shot);

        foreach (var hit in GM.Events.Stream<DamageDealt>().Records)
            Strike(hit);

        foreach (var blast in GM.Events.Stream<SplashRequested>().Records)
            Burst(blast);

        Track();

        foreach (var death in GM.Events.Stream<EntityDestroyed>().Records)
            Blast(death);

        Close();
    }

    /// <summary>
    /// Ведение уже созданных эффектов: пыль под корпусом, догорание осиротевших узлов,
    /// отложенные взрывы. Всё это влияет на одну лишь картинку, поэтому идёт раз в кадр,
    /// а не раз в шаг, — см. <see cref="GameSystem.Present"/>. Отсчёты ведутся по <c>dt</c>
    /// кадра и потому остаются верны по времени, а не по числу вызовов.
    /// </summary>
    public override void Present(double dt)
    {
        foreach (var trails in _trails.Values)
            foreach (var trail in trails)
                Advance(trail, dt);

        Await(dt);
    }

    // ── ход: пыль из-под корпуса ──────────────────────────────────────────────────

    /// <summary>
    /// Подвижная сущность вошла в мир: завести потоки пыли по тому, что снято с её модели.
    ///
    /// УЗЛОВ ЧАСТИЦ ЗДЕСЬ БОЛЬШЕ НЕ ИЩУТ. Запекание сняло их со сцены модели и оставило
    /// от них настройки рождения (<see cref="ModelBake.Trails"/>), поэтому вошедшая машина
    /// не приносит с собой ни одного узла выдачи — пыль всех машин выпускает одно общее
    /// поле.
    /// </summary>
    private void OnMobileAdded(IMobile mobile)
    {
        if (mobile is not Node2D carrier || !Alive.Is(carrier))
            return;

        var found = Model(carrier)?.Bake?.Trails;

        if (found == null || found.Length == 0)
            return;

        var trails = new List<Trail>(found.Length);

        foreach (var item in found)
        {
            if (item.Effect == null || item.Effect.Parts.Length == 0)
                continue;

            trails.Add(new Trail
            {
                Effect = item.Effect,
                Owner = mobile,
                Offset = item.Offset,
                Angle = item.Angle,
                Debts = new float[item.Effect.Parts.Length],
            });
        }

        if (trails.Count > 0)
            _trails[mobile] = trails;
    }

    /// <summary>
    /// Носитель выбыл. Досчитывать здесь нечего: выпущенная пыль живёт в общем поле и своё
    /// доживает сама, а связка держала лишь настройки потока.
    /// </summary>
    private void OnMobileRetired(IMobile mobile) => _trails.Remove(mobile);

    /// <summary>
    /// Шаг одного потока: место по носителю, густота по скорости.
    ///
    /// ГУСТОТА, А НЕ ВКЛЮЧЕНИЕ. Прежде поток включался и выключался признаком выдачи узла,
    /// и переступающая в толпе машина мигала бы пылью на каждом шаге; ради этого держалась
    /// задержка после остановки. Теперь частота вброса прямо пропорциональна доле разгона:
    /// стоящая машина не даёт частиц вовсе, разгон и торможение видны сами собой,
    /// а мигать нечему, поскольку включать и выключать больше нечего.
    /// </summary>
    private void Advance(Trail trail, double dt)
    {
        if (trail.Owner is not Node2D carrier || !Alive.Is(carrier))
            return;

        // Преобразование считается по кэшированным полям сущности, а не узлом: чтение поля
        // не переходит границу C#↔Godot — см. Entity
        Vector2 spot;
        float facing;

        if (carrier is Entity placed)
        {
            spot = placed.ToWorld(trail.Offset);
            facing = placed.Rotation + trail.Angle;
        }
        else
        {
            spot = carrier.ToGlobal(trail.Offset);
            facing = carrier.GlobalRotation + trail.Angle;
        }

        var bake = trail.Effect;
        float speed = trail.Owner.Movement?.Velocity.Length() ?? 0f;
        float span = Mathf.Max(bake.FullSpeed - bake.MinSpeed, 1f);
        float ratio = Mathf.Clamp((speed - bake.MinSpeed) / span, 0f, 1f);

        if (ratio <= 0f)
            return;

        for (int i = 0; i < bake.Parts.Length; i++)
            _yard.Stream(bake.Parts[i], spot, facing, 1f, ratio, dt, ref trail.Debts[i]);
    }

    // ── выстрел: вспышка у среза ствола ───────────────────────────────────────────

    /// <summary>
    /// Вооружённая сущность вошла в мир: разобрать её стенд наведения и запомнить вспышки,
    /// лежащие в частях-инструментах.
    ///
    /// ОПОЗНАНИЕ ОТДАНО СБОРКЕ, А НЕ ХУДОЖНИКУ. Часть-инструмент уже связана с определением
    /// оружия по <see cref="ModelTool.ToolId"/>, и вспышка, лежащая внутри неё, наследует
    /// эту связь целиком. Поэтому подписывать вспышку не нужно вовсе: есть она у ствола —
    /// значит, принадлежит ему, нет — значит, у этого ствола вспышки не нарисовали.
    /// </summary>
    private void OnArmedAdded(IArmed armed)
    {
        if (armed is not Node2D carrier || !Alive.Is(carrier))
            return;

        List<Flash> found = null;
        List<Blow> blows = null;

        foreach (var mount in armed.Aim.Mounts)
        {
            if (mount.Weapon == null || mount.Part == null || !Alive.Is(mount.Part))
                continue;

            foreach (var emitter in mount.Part.Flashes)
                if (emitter.Effect != null)
                    (found ??= new List<Flash>())
                        .Add(new Flash(mount.Weapon.Id, mount.Part, emitter));

            // Вспышка попадания объявлена внутри той же части и снята запеканием —
            // см. ImpactEffect. Берётся ссылкой на сцену: играть эффект придётся на цели,
            // до которой снаряду ещё лететь
            foreach (var item in mount.Part.Impacts)
                (blows ??= new List<Blow>())
                    .Add(new Blow(mount.Weapon.Id, item.Effect, item.Size));
        }

        if (found != null)
            _flashes[armed] = found.ToArray();

        if (blows != null)
            _impacts[armed] = blows.ToArray();
    }

    private void OnArmedRetired(IArmed armed)
    {
        _flashes.Remove(armed);
        _impacts.Remove(armed);
    }

    /// <summary>
    /// Показать выстрел. Носитель разыскивается по номеру сущности, а ствол — по ключу
    /// инструмента: у носителя стволов бывает несколько, и вспышка обязана выйти из того,
    /// который стрелял.
    ///
    /// Положение и угол из документа здесь не нужны: вспышка лежит при срезе ствола
    /// и место своё знает сама. Читает их тот, у кого своего места нет, — попадание.
    /// </summary>
    private void Flare(WeaponFired shot)
    {
        if (GM.Entities.Get(shot.EntityId) is not IArmed armed
            || !_flashes.TryGetValue(armed, out var flashes))
            return;

        foreach (var flash in flashes)
        {
            if (flash.ToolId != shot.ToolId || !Alive.Is(flash.Part))
                continue;

            // Место вспышки считается от преобразования ствола: узла, который прежде ездил
            // вместе с ним сам собой, больше нет
            var at = flash.Part.GlobalTransform;

            Play(flash.Effect, at * flash.Offset, at.Rotation + flash.Angle, 1f);
        }
    }

    // ── попадание: вспышка на цели ────────────────────────────────────────────────

    /// <summary>
    /// Показать попадание. Вспышка разворачивается против хода снаряда, поэтому разлёт
    /// идёт навстречу выстрелу, а не в случайную сторону.
    /// </summary>
    private void Strike(DamageDealt hit)
    {
        // Задетым взрывом вспышка не полагается: у самого взрыва есть свой эффект
        // в середине, и вспышка на каждой цели сверх него дала бы десяток вспышек
        // на один разрыв, вычерпав при этом запас общего поля
        if (hit.FromSplash)
            return;

        var (scene, size) = ImpactOf(hit);

        Play(ParticleBake.Of(scene), hit.Pos, hit.Facing + Mathf.Pi, size);
    }

    /// <summary>
    /// Чем показывать это попадание. Вид принадлежит ОРУЖИЮ, а не цели: разрыв говорит,
    /// чем ударили, и болванка малого калибра обязана отличаться от снаряда титана на одной
    /// и той же машине. Поэтому объявление ищется у стрелявшего, по ключу его ствола.
    ///
    /// ОБЩАЯ ВСПЫШКА ОСТАЁТСЯ ЗАПАСНОЙ и достаётся трём случаям: у оружия объявления нет,
    /// урон нанесён не оружием вовсе (снос), либо стрелявший погиб раньше, чем его снаряд
    /// долетел, — спросить объявление тогда не у кого.
    /// </summary>
    private (PackedScene Scene, float Size) ImpactOf(DamageDealt hit) =>
        Declared(hit.SourceId, hit.ToolId) is { } blow
            ? (blow.Scene, blow.Size)
            : (Impact, 1f);

    /// <summary>
    /// Объявление вспышки у стрелявшего по ключу его ствола. Пусто означает, что своей
    /// вспышки у оружия нет либо спросить её уже не у кого.
    /// </summary>
    private Blow? Declared(int sourceId, string toolId)
    {
        if (string.IsNullOrEmpty(toolId)
            || GM.Entities.Get(sourceId) is not IArmed armed
            || !_impacts.TryGetValue(armed, out var declared))
            return null;

        foreach (var blow in declared)
            if (blow.ToolId == toolId)
                return blow;

        return null;
    }

    // ── взрыв: эффект в середине области ──────────────────────────────────────────

    /// <summary>
    /// Показать взрыв. Повод — требование раздачи урона по области, то есть тот же
    /// документ, по которому урон и раздаётся: эффект и его последствия так не разойдутся
    /// ни при каком стечении обстоятельств.
    ///
    /// РАЗМЕР ВЫВЕДЕН ИЗ РАДИУСА, а не задан отдельно, поэтому увеличение радиуса
    /// в справочнике само растит картинку, и подписывать одно число дважды не приходится.
    /// Перевод считает <see cref="CombatSettings.SplashSize"/>, общий с полем редактора.
    ///
    /// Сцена берётся из свода боя; не назначена — играется общий взрыв гибели. Поворот
    /// случайный: рисунок частиц один на все разрывы, и без разворота два соседних
    /// выглядели бы одинаково.
    /// </summary>
    private void Burst(SplashRequested blast)
    {
        // Оружие со своей вспышкой попадания показало разрыв уже ею: два эффекта в одной
        // точке читаются как сбой, а не как мощный взрыв. Общий взрыв области поэтому
        // остаётся тем, у кого своей вспышки нет, — и тем событиям, у которых прямого
        // попадания не было вовсе
        if (Declared(blast.SourceId, blast.ToolId) != null)
            return;

        var scene = CombatSettings.Active.SplashEffect ?? Explosion;

        Fire(scene, blast.Pos, (float)GD.RandRange(0d, Mathf.Tau),
            CombatSettings.SplashSize(blast.Radius));
    }

    // ── гибель: взрыв и копоть на земле ───────────────────────────────────────────

    /// <summary>
    /// Сущность, способная погибнуть, вошла в мир: разобрать её модель и запомнить
    /// объявленные взрывы. Как и у пыли, смещение снимается заранее, в системе координат
    /// носителя: в момент гибели спрашивать его будет уже не у кого.
    ///
    /// Ничего не объявлено — связка не заводится вовсе. Такой вид взрывается общим
    /// взрывом по точке из документа, и хранить о нём нечего.
    /// </summary>
    private void OnDeadlyAdded(IDamageable target)
    {
        if (target is not Node2D carrier || !Alive.Is(carrier))
            return;

        var found = Model(carrier)?.Bake?.Deaths;

        if (found == null || found.Length == 0)
            return;

        // Взрывы берутся запечёнными как есть: место и поворот заданы в осях корпуса самим
        // запеканием, и своего описания того же самого системе эффектов заводить незачем
        var items = found;

        var burial = new Burial
        {
            Id = target.EntityId,
            Carrier = carrier,
            Snapshot = carrier.GlobalTransform,
            Items = items,
        };

        _burials[target] = burial;
        _byId[burial.Id] = burial;
    }

    /// <summary>
    /// Носитель выбыл. Из связки по номеру он снимается НЕ СРАЗУ, и это существенно:
    /// уведомление приходит в конце кадра, а документ о гибели разбирается в том же кадре
    /// раньше. Снятие поэтому откладывается до конца ближайшего шага —
    /// см. <see cref="Close"/>.
    /// </summary>
    private void OnDeadlyRetired(IDamageable target)
    {
        if (_burials.Remove(target, out var burial))
            _closed.Add(burial);
    }

    /// <summary>Запомнить положение живых носителей на случай их гибели.</summary>
    private void Track()
    {
        foreach (var burial in _burials.Values)
            if (Alive.Is(burial.Carrier))
                burial.Snapshot = burial.Carrier.GlobalTransform;
    }

    /// <summary>Досчитать задержки и проиграть то, чей срок вышел.</summary>
    private void Await(double dt)
    {
        for (int i = _delayed.Count - 1; i >= 0; i--)
        {
            var item = _delayed[i];
            item.Left -= (float)dt;

            if (item.Left > 0f)
            {
                _delayed[i] = item;
                continue;
            }

            _delayed.RemoveAt(i);
            Fire(item.Scene, item.Pos, item.Angle, item.Size);
        }
    }

    /// <summary>Снять связки выбывших. Документы кадра к этому мигу уже разобраны.</summary>
    private void Close()
    {
        if (_closed.Count == 0)
            return;

        foreach (var burial in _closed)
            if (_byId.TryGetValue(burial.Id, out var stored) && stored == burial)
                _byId.Remove(burial.Id);

        _closed.Clear();
    }

    /// <summary>
    /// Показать гибель сущности.
    ///
    /// ОБЪЯВЛЕНО В МОДЕЛИ — играется объявленное, каждый узел <see cref="DeathEffect"/>
    /// в своём месте корпуса, со своим размером и своей задержкой. Место считается
    /// по снимку преобразования носителя, поскольку смещения заданы относительно корпуса.
    ///
    /// НЕ ОБЪЯВЛЕНО — играется общий взрыв в точке из документа со случайным поворотом:
    /// частицы у всех гибелей тогда одни и те же, и без разворота две соседние машины
    /// взрывались бы совершенно одинаково.
    /// </summary>
    private void Blast(EntityDestroyed death)
    {
        if (_byId.TryGetValue(death.EntityId, out var burial))
        {
            foreach (var item in burial.Items)
            {
                // Узел без своей сцены означает «взорваться здесь общим взрывом»:
                // место и размер объявлены, а вида взрыва у этого юнита своего нет
                var scene = item.Effect ?? Explosion;
                var pos = burial.Snapshot * item.Offset;
                float angle = burial.Snapshot.Rotation + item.Angle;

                if (item.Delay > 0f)
                {
                    _delayed.Add(new Delayed
                    {
                        Scene = scene,
                        Pos = pos,
                        Angle = angle,
                        Size = item.Size,
                        Left = item.Delay,
                    });

                    continue;
                }

                Fire(scene, pos, angle, item.Size);
            }

            return;
        }

        Fire(Explosion, death.Pos, (float)GD.RandRange(0d, Mathf.Tau), 1f);
    }

    /// <summary>Проиграть взрыв в готовом месте.</summary>
    private void Fire(PackedScene scene, Vector2 pos, float angle, float size) =>
        Play(ParticleBake.Of(scene), pos, angle, Mathf.Max(size, 0.01f));

    /// <summary>
    /// Проиграть одноразовый эффект и оставить след, если он объявлен.
    ///
    /// ПОЧЕМУ ОТМЕТИНУ СТАВИТ СИСТЕМА, А НЕ САМ ЭФФЕКТ. Объявление отметины
    /// (<see cref="ScorchStamp"/>) есть описание, а не действие: про слой отпечатков и про
    /// мир вокруг оно не знает ничего, как и вспышка выстрела не знает, из какого ствола
    /// вышла. Отсюда общее следствие: отметину оставляет любой эффект, в котором штамп
    /// положен, — и гибель, и попадание, — а различать события здесь не нужно вовсе.
    ///
    /// РАЗМЕР ОТМЕТИНЫ РАСТЁТ ВМЕСТЕ С ЭФФЕКТОМ: множитель, объявленный в модели, растит
    /// и частицы, и пятно под ними, поэтому взрыв титана оставляет след шире, чем взрыв
    /// бота, без второго числа в объявлении.
    /// </summary>
    private void Play(EffectBake bake, Vector2 spot, float facing, float size)
    {
        if (bake == null)
            return;

        _yard.Play(bake, spot, facing, size);

        foreach (var stamp in bake.Stamps)
            Scorch()?.Stamp(stamp.Decal, spot + stamp.Offset.Rotated(facing) * size,
                stamp.MinSize * size, stamp.MaxSize * size);
    }

    /// <summary>
    /// Слой отметин. Поднимается при первом отпечатке и живёт на слое наземных эффектов:
    /// копоть лежит на грунте, то есть под всем, что по нему ходит и стоит.
    /// </summary>
    private ScorchField Scorch()
    {
        if (Alive.Is(_scorch))
            return _scorch;

        _scorch = GM.Playground.Add(WorldLayer.GroundEffects, new ScorchField
        {
            Name = nameof(ScorchField),
            Capacity = ScorchCapacity,
        });

        return _scorch;
    }

    /// <summary>
    /// Модель сущности. Ищется однажды, при входе в мир: у носителя она одна и лежит
    /// прямо под ним — см. <see cref="UnitModel"/>.
    /// </summary>
    private static UnitModel Model(Node carrier)
    {
        foreach (var child in carrier.GetChildren())
            if (child is UnitModel model)
                return model;

        return null;
    }
}
