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

    private BurstParticles[] _impacts;
    private int _nextImpact;

    protected override void OnLink()
    {
        GM.Index.Watch<IMobile>(OnMobileAdded, OnMobileRetired, this);
        GM.Index.Watch<IArmed>(OnArmedAdded, OnArmedRetired, this);
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
        var burst = NextImpact();

        if (burst == null)
            return;

        burst.GlobalPosition = hit.Pos;
        burst.GlobalRotation = hit.Facing + Mathf.Pi;
        burst.Play();
    }

    /// <summary>
    /// Очередная вспышка из кольцевого набора. Набор поднимается при первом попадании:
    /// партия может пройти вовсе без стрельбы, и платить за него заранее незачем.
    /// </summary>
    private BurstParticles NextImpact()
    {
        if (Impact == null || ImpactPool <= 0)
            return null;

        if (_impacts == null)
        {
            _impacts = new BurstParticles[ImpactPool];

            for (int i = 0; i < _impacts.Length; i++)
            {
                if (Impact.Instantiate() is not BurstParticles burst)
                {
                    GD.PushWarning("[EffectSystem] корень сцены попадания не BurstParticles");
                    _impacts = null;
                    return null;
                }

                _impacts[i] = GM.Playground.Add(WorldLayer.AirEffects, burst);
            }
        }

        var next = _impacts[_nextImpact];
        _nextImpact = (_nextImpact + 1) % _impacts.Length;

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
