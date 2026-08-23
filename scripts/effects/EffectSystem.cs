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
    /// Вид вспышки попадания. Общий на все виды оружия: показывать разные попадания
    /// пока нечем, а как только понадобится, ссылка переедет к части-инструменту модели,
    /// где уже лежит вспышка выстрела.
    /// </summary>
    [Export] public PackedScene Impact { get; set; }

    /// <summary>
    /// Сколько вспышек попадания держать наготове. Набор кольцевой: попадания идут часто
    /// и коротко, поэтому поднимать сцену на каждое и освобождать её следом дороже, чем
    /// перезапускать давно отыгравшую.
    /// </summary>
    [Export] public int ImpactPool { get; set; } = 24;

    /// <summary>
    /// Взрыв при гибели. Общий на все виды — и на юниты, и на постройки: разные взрывы
    /// показывать пока нечем. Когда понадобятся, ссылка переедет в сцену модели, где
    /// у вида уже лежит всё прочее его изображение, а это поле останется общим запасным.
    /// </summary>
    [Export] public PackedScene Explosion { get; set; }

    /// <summary>
    /// Сколько взрывов держать наготове. Меньше, чем попаданий: гибнет реже, чем попадает,
    /// зато волна гибнет разом, поэтому единицей набор быть не может.
    /// </summary>
    [Export] public int ExplosionPool { get; set; } = 8;

    /// <summary>
    /// Сколько отметин копоти держать на карте — см. <see cref="ScorchField.Capacity"/>.
    /// Задаётся здесь потому, что слой отпечатков поднимается этой системой: собственного
    /// узла в сцене сессии у него нет, как нет его и у вспышек попадания.
    /// </summary>
    [Export] public int ScorchCapacity { get; set; } = 96;

    /// <summary>
    /// Связка «узел частиц — его носитель». Смещение и угол запомнены в системе координат
    /// носителя в тот миг, когда узел ещё лежал в модели: после переноса на слой мира
    /// спросить их уже не у кого.
    /// </summary>
    private sealed class Trail
    {
        public MovementParticles Node;
        public IMobile Owner;
        public Vector2 Offset;
        public float Angle;

        /// <summary>Сколько секунд носитель стоит. Гасит выдачу по <c>StopDelay</c>.</summary>
        public float Idle;
    }

    /// <summary>Вспышка выстрела, найденная у части-инструмента, с ключом её инструмента.</summary>
    private readonly struct Flash
    {
        public readonly string ToolId;
        public readonly BurstParticles Node;

        public Flash(string toolId, BurstParticles node)
        {
            ToolId = toolId;
            Node = node;
        }
    }

    /// <summary>
    /// Узлы, потерявшие носителя. Освобождаются не сразу: выпущенная пыль обязана дожить
    /// свой срок, иначе облако исчезает вместе с погибшей машиной.
    /// </summary>
    private readonly List<(Node2D Node, float Left)> _fading = new();

    private readonly Dictionary<object, List<Trail>> _trails = new(ByReference.Instance);

    private readonly Dictionary<object, Flash[]> _flashes = new(ByReference.Instance);

    /// <summary>Один объявленный в модели взрыв, снятый в системе координат носителя.</summary>
    private readonly struct Wreck
    {
        public readonly PackedScene Scene;
        public readonly Vector2 Offset;
        public readonly float Angle;
        public readonly float Size;
        public readonly float Delay;

        public Wreck(PackedScene scene, Vector2 offset, float angle, float size, float delay)
        {
            Scene = scene;
            Offset = offset;
            Angle = angle;
            Size = size;
            Delay = delay;
        }
    }

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
        public Wreck[] Items;
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

    /// <summary>Готовые экземпляры одноразовых эффектов, по набору на сцену.</summary>
    private sealed class Pool
    {
        public BurstParticles[] Items;
        public int Next;
    }

    private readonly Dictionary<PackedScene, Pool> _pools = new();

    private ScorchField _scorch;

    protected override void OnLink()
    {
        GM.Index.Watch<IMobile>(OnMobileAdded, OnMobileRetired, this);
        GM.Index.Watch<IArmed>(OnArmedAdded, OnArmedRetired, this);
        GM.Index.Watch<IDamageable>(OnDeadlyAdded, OnDeadlyRetired, this);
    }

    public override void Step(double dt)
    {
        foreach (var trails in _trails.Values)
            foreach (var trail in trails)
                Advance(trail, dt);

        Fade(dt);

        foreach (var shot in GM.Events.Stream<WeaponFired>().Records)
            Flare(shot);

        foreach (var hit in GM.Events.Stream<DamageDealt>().Records)
            Strike(hit);

        Track();
        Await(dt);

        foreach (var death in GM.Events.Stream<EntityDestroyed>().Records)
            Blast(death);

        Close();
    }

    // ── ход: пыль из-под корпуса ──────────────────────────────────────────────────

    /// <summary>
    /// Подвижная сущность вошла в мир: собрать в её модели узлы частиц хода и перенести
    /// их на слой наземных эффектов.
    /// </summary>
    private void OnMobileAdded(IMobile mobile)
    {
        if (mobile is not Node2D carrier || !Alive.Is(carrier))
            return;

        var found = new List<MovementParticles>();
        Collect(carrier, found);

        if (found.Count == 0)
            return;

        var trails = new List<Trail>(found.Count);

        foreach (var node in found)
        {
            // Смещение снимается ДО переноса: после него узел уже не потомок носителя
            var local = carrier.GlobalTransform.AffineInverse() * node.GlobalTransform;

            trails.Add(new Trail
            {
                Node = node,
                Owner = mobile,
                Offset = local.Origin,
                Angle = local.Rotation,
            });

            Detach(node);
        }

        _trails[mobile] = trails;
    }

    /// <summary>
    /// Носитель выбыл. Поля его ноды здесь читать нельзя — движок обычно уже освободил
    /// обёртку, — поэтому опознание идёт только по ссылке, а всё нужное лежит в связке.
    /// </summary>
    private void OnMobileRetired(IMobile mobile)
    {
        if (!_trails.Remove(mobile, out var trails))
            return;

        foreach (var trail in trails)
        {
            if (!Alive.Is(trail.Node))
                continue;

            trail.Node.Emitting = false;
            _fading.Add((trail.Node, (float)trail.Node.Lifetime));
        }
    }

    /// <summary>
    /// Шаг одной связки: положение по носителю, плотность по скорости.
    ///
    /// ПЛОТНОСТЬ, А НЕ ВКЛЮЧЕНИЕ. <see cref="GpuParticles2D.Emitting"/> при обратном
    /// включении начинает выдачу заново, поэтому мигать им на каждом шаге нельзя:
    /// разгон и остановка выражаются долей <c>AmountRatio</c>, а само включение снимается
    /// только после того, как носитель простоял <c>StopDelay</c>.
    /// </summary>
    private void Advance(Trail trail, double dt)
    {
        var node = trail.Node;

        if (!Alive.Is(node) || trail.Owner is not Node2D carrier || !Alive.Is(carrier))
            return;

        node.GlobalPosition = carrier.ToGlobal(trail.Offset);
        node.GlobalRotation = carrier.GlobalRotation + trail.Angle;

        float speed = trail.Owner.Movement?.Velocity.Length() ?? 0f;
        float span = Mathf.Max(node.FullSpeed - node.MinSpeed, 1f);
        float ratio = Mathf.Clamp((speed - node.MinSpeed) / span, 0f, 1f);

        node.AmountRatio = ratio;

        if (ratio > 0f)
        {
            trail.Idle = 0f;

            if (!node.Emitting)
                node.Emitting = true;

            return;
        }

        trail.Idle += (float)dt;

        if (trail.Idle >= node.StopDelay && node.Emitting)
            node.Emitting = false;
    }

    /// <summary>Досчитать срок осиротевшим узлам и освободить тех, чья пыль осела.</summary>
    private void Fade(double dt)
    {
        for (int i = _fading.Count - 1; i >= 0; i--)
        {
            var (node, left) = _fading[i];

            if (!Alive.Is(node))
            {
                _fading.RemoveAt(i);
                continue;
            }

            left -= (float)dt;

            if (left > 0f)
            {
                _fading[i] = (node, left);
                continue;
            }

            _fading.RemoveAt(i);
            node.QueueFree();
        }
    }

    /// <summary>
    /// Перенести узел на слой наземных эффектов. Частицы обязаны считаться в мировых
    /// координатах: узел ведётся по носителю вручную, и при местных координатах уже
    /// выпущенное облако ехало бы вслед за машиной вместо того, чтобы оставаться позади.
    /// </summary>
    private void Detach(MovementParticles node)
    {
        node.LocalCoords = false;
        node.GetParent()?.RemoveChild(node);
        GM.Playground.Add(WorldLayer.GroundEffects, node);
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

        foreach (var mount in armed.Aim.Mounts)
        {
            if (mount.Weapon == null || mount.Part == null || !Alive.Is(mount.Part))
                continue;

            var bursts = new List<BurstParticles>();
            Collect(mount.Part, bursts);

            foreach (var burst in bursts)
                (found ??= new List<Flash>()).Add(new Flash(mount.Weapon.Id, burst));
        }

        if (found != null)
            _flashes[armed] = found.ToArray();
    }

    private void OnArmedRetired(IArmed armed) => _flashes.Remove(armed);

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
            if (flash.ToolId == shot.ToolId && Alive.Is(flash.Node))
                flash.Node.Play();
    }

    // ── попадание: вспышка на цели ────────────────────────────────────────────────

    /// <summary>
    /// Показать попадание. Вспышка разворачивается против хода снаряда, поэтому разлёт
    /// идёт навстречу выстрелу, а не в случайную сторону.
    /// </summary>
    private void Strike(DamageDealt hit)
    {
        var burst = Take(Impact, ImpactPool);

        if (burst == null)
            return;

        burst.GlobalPosition = hit.Pos;
        burst.GlobalRotation = hit.Facing + Mathf.Pi;
        burst.Scale = Vector2.One;

        Play(burst);
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

        var found = new List<DeathEffect>();
        Collect(carrier, found);

        if (found.Count == 0)
            return;

        var items = new Wreck[found.Count];
        var inverse = carrier.GlobalTransform.AffineInverse();

        for (int i = 0; i < found.Count; i++)
        {
            var local = inverse * found[i].GlobalTransform;

            items[i] = new Wreck(found[i].Effect, local.Origin, local.Rotation,
                found[i].Size, found[i].Delay);
        }

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
                var scene = item.Scene ?? Explosion;
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
    private void Fire(PackedScene scene, Vector2 pos, float angle, float size)
    {
        var burst = Take(scene, ExplosionPool);

        if (burst == null)
            return;

        burst.GlobalPosition = pos;
        burst.GlobalRotation = angle;

        // Экземпляры набора общие, поэтому размер выставляется на каждое проигрывание,
        // а не один раз при поднятии: прошлый взрыв мог оставить чужой множитель
        burst.Scale = Vector2.One * Mathf.Max(size, 0.01f);

        Play(burst);
    }

    /// <summary>
    /// Проиграть одноразовый эффект и оставить след, если он объявлен.
    ///
    /// ПОЧЕМУ ОТМЕТИНУ СТАВИТ СИСТЕМА, А НЕ САМ ЭФФЕКТ. Узел
    /// <see cref="ScorchStamp"/> — объявление, а не действие: про слой отпечатков и про
    /// мир вокруг он не знает ничего, как и вспышка выстрела не знает, из какого ствола
    /// вышла. Отсюда общее следствие: отметину оставляет любой эффект, в котором штамп
    /// положен, — и гибель, и попадание, — а различать поводы здесь не нужно вовсе.
    /// </summary>
    private void Play(BurstParticles burst)
    {
        burst.Play();

        foreach (var stamp in burst.Stamps)
        {
            if (!Alive.Is(stamp) || stamp.Decal == null)
                continue;

            // Положение снимается у самого штампа: художник кладёт его со смещением,
            // когда пятно не совпадает с началом эффекта. Размер берётся с учётом
            // мирового масштаба узла, поэтому множитель размера взрыва, заданный
            // в модели, растит и пятно под ним
            float size = Mathf.Abs(stamp.GlobalScale.X);

            Scorch()?.Stamp(stamp.Decal, stamp.GlobalPosition,
                stamp.MinSize * size, stamp.MaxSize * size);
        }
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

    // ── наборы готовых эффектов ───────────────────────────────────────────────────

    /// <summary>
    /// Очередной экземпляр эффекта из набора. Набор поднимается при первом обращении
    /// к сцене: партия может пройти вовсе без стрельбы либо без потерь, и платить
    /// за готовые экземпляры заранее незачем.
    ///
    /// НАБОР СВОЙ У КАЖДОЙ СЦЕНЫ. Общий набор пришлось бы поднимать по самому длинному
    /// эффекту и по самому частому поводу разом, то есть держать десятки взрывов ради
    /// частоты попаданий.
    /// </summary>
    private BurstParticles Take(PackedScene scene, int size)
    {
        if (scene == null || size <= 0)
            return null;

        if (!_pools.TryGetValue(scene, out var pool))
        {
            var items = new BurstParticles[size];

            for (int i = 0; i < items.Length; i++)
            {
                if (scene.Instantiate() is not BurstParticles burst)
                {
                    GD.PushWarning($"[EffectSystem] корень сцены {scene.ResourcePath} " +
                        "не BurstParticles");
                    return null;
                }

                items[i] = GM.Playground.Add(WorldLayer.AirEffects, burst);
            }

            pool = new Pool { Items = items };
            _pools[scene] = pool;
        }

        var next = pool.Items[pool.Next];
        pool.Next = (pool.Next + 1) % pool.Items.Length;

        return Alive.Is(next) ? next : null;
    }

    private static void Collect<T>(Node node, List<T> found) where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T match)
                found.Add(match);

            Collect(child, found);
        }
    }
}
