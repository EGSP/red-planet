using System.Collections.Generic;
using Godot;

/// <summary>
/// Эффекты мира: узлы частиц, объявленные в сценах моделей, связываются здесь с сущностями
/// и ведутся отсюда каждый кадр.
///
/// ЗАЧЕМ СИСТЕМА, А НЕ КОД В САМОМ УЗЛЕ. Узел изображения не знает, чей он и что вокруг
/// происходит: пыль не должна разыскивать носителя, а вспышка выстрела — угадывать, какому
/// из двух стволов она принадлежит. Связывание есть работа того, кто видит обе стороны —
/// справочник инструментов и дерево модели, — и живёт в одном месте по той же причине,
/// по которой в одном месте живёт <see cref="Spawner"/>.
///
/// ГРАНИЦА С СИМУЛЯЦИЕЙ. Система только читает. Она не дописывает документов, не трогает
/// индекс и ни на что в мире не влияет, поэтому снятие всего слоя эффектов не меняет хода
/// партии. Отсюда же её место: фаза <see cref="Phase.React"/> графического цикла — эффект
/// показывает уже случившееся и обязан совпадать с отрисовкой, а не с шагом физики.
///
/// ЧТО ДОБАВЛЯЕТСЯ ДАЛЬШЕ. Поводов для эффекта будет несколько, и каждый читает своё:
/// ход — скорость, выстрел — документ кадра, повреждение — <see cref="DamageDealt"/>.
/// Общего у них только связывание, поэтому новый повод добавляется своим родом узла
/// и своим разделом здесь, а не наследником <see cref="MovementParticles"/>.
///
/// СЛОЙ МЕНЯЕТСЯ ПРИ СВЯЗЫВАНИИ. Узел лежит в сцене модели, то есть внутри дерева юнита,
/// и рисовался бы поверх корпуса, а пыль поднимается от грунта. Поэтому при связывании
/// узел переносится на <see cref="WorldLayer.GroundEffects"/>, а положение ему выставляется
/// каждый кадр по владельцу и запомненному смещению.
/// </summary>
public partial class EffectSystem : GameSystem
{
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

    /// <summary>
    /// Узлы, потерявшие носителя. Освобождаются не сразу: выпущенная пыль обязана дожить
    /// свой срок, иначе облако исчезает вместе с погибшей машиной.
    /// </summary>
    private readonly List<(Node2D Node, float Left)> _fading = new();

    private readonly Dictionary<object, List<Trail>> _trails = new(ByReference.Instance);

    protected override void OnLink() =>
        GM.Index.Watch<IMobile>(OnMobileAdded, OnMobileRetired, this);

    public override void Step(double dt)
    {
        foreach (var trails in _trails.Values)
            foreach (var trail in trails)
                Advance(trail, dt);

        Fade(dt);
    }

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

    private static void Collect(Node node, List<MovementParticles> found)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is MovementParticles particles)
                found.Add(particles);

            Collect(child, found);
        }
    }
}
