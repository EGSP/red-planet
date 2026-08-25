using Godot;

/// <summary>
/// Снаряд. Летит по прямой, живёт отведённое время и, задев чужую сущность,
/// публикует документ о попадании.
///
/// Урон снаряд НЕ применяет: он публикует DamageDealt, а прочность правит DamageSystem
/// в фазе реакции. Так у попадания есть след в журнале, и никто не убивает цель
/// посреди чужого обхода — от этого при удалении нод и рождаются висячие ссылки.
///
/// Попадание ищется по отрезку за кадр, а не по конечной точке: на скорости
/// пятнадцати пикселей за кадр проскочить цель насквозь несложно.
/// </summary>
public partial class Projectile : Entity
{
    public Vector2 Velocity;

    public float Damage;

    /// <summary>Радиус самого снаряда в пикселях — складывается с радиусом цели.</summary>
    public float Radius = 4f;

    /// <summary>Сколько ещё лететь, секунд.</summary>
    public float Life = 1f;

    /// <summary>Кто выстрелил — id уходит в документ о попадании.</summary>
    public int SourceId;

    /// <summary>
    /// Из какого ствола выстрелен. Уходит в документ попадания и нужен показу: вид вспышки
    /// объявлен у самого оружия (<see cref="ImpactEffect"/>), а стволов у носителя бывает
    /// несколько.
    /// </summary>
    public string ToolId = "";

    /// <summary>По какой стороне бьёт.</summary>
    public Faction TargetSide;

    /// <summary>
    /// Радиус взрыва в пикселях. Ноль — снаряд бьёт только ту цель, которую задел.
    /// </summary>
    public float SplashRadius;

    /// <summary>Урон в середине взрыва. К краю убывает — раздаёт его <see cref="SplashSystem"/>.</summary>
    public float SplashDamage;

    /// <summary>Задевает ли взрыв своих.</summary>
    public bool SplashFriendlyFire;

    public Color Tint = new(1f, 0.9f, 0.4f);

    /// <summary>
    /// Место под кандидатов на попадание. Одно на все снаряды: полёт считается в один поток,
    /// и заводить список на каждый снаряд каждый кадр значило бы мусорить впустую.
    /// </summary>
    private static readonly System.Collections.Generic.List<IDamageable> _candidates = new();

    public override void _Process(double delta) => QueueRedraw();

    public void Step(double dt)
    {
        Life -= (float)dt;
        if (Life <= 0f)
        {
            Retire();
            return;
        }

        var from = GlobalPosition;
        var to = from + Velocity * (float)dt;

        var hit = FindHit(from, to);
        GlobalPosition = to;

        if (hit == null)
            return;

        GameManager.I.Events.Append(new DamageDealt
        {
            TargetId = hit.EntityId,
            SourceId = SourceId,
            ToolId = ToolId,
            Amount = Damage,
            Pos = GlobalPosition,
            Facing = Velocity.Angle(),
        });

        // Взрыв публикуется отдельным требованием: кого он задел, решает SplashSystem —
        // снаряд об окружении цели не знает и знать не должен
        if (SplashRadius > 0f && SplashDamage > 0f)
            GameManager.I.Events.Append(new SplashRequested
            {
                SourceId = SourceId,
                ToolId = ToolId,
                Side = TargetSide.Opposite(),
                FriendlyFire = SplashFriendlyFire,
                Pos = GlobalPosition,
                Radius = SplashRadius,
                Amount = SplashDamage,
                DirectId = hit.EntityId,
            });

        Retire();
    }

    /// <summary>
    /// Кого задел отрезок полёта за этот кадр.
    ///
    /// Кандидаты берутся из раскладки по месту, а не обходом всех целей стороны: снарядов
    /// в бою десятки, целей сотни, и полный обход у каждого снаряда каждый кадр давал
    /// произведение численностей. Окрестность отсчитывается от середины отрезка: за кадр
    /// снаряд пролетает малую долю клетки, поэтому клеток выходит немного.
    ///
    /// Запас к радиусу — на габарит цели. Крупная постройка лежит во всех клетках своего
    /// прямоугольника и потому находится и без запаса; запас нужен юнитам, чья середина
    /// может оказаться в соседней клетке.
    /// </summary>
    private IDamageable FindHit(Vector2 from, Vector2 to)
    {
        IDamageable best = null;
        float bestDistance = float.MaxValue;

        var nearby = GameManager.I.Space.ReadyTargets();
        nearby.Collect((from + to) * 0.5f,
            from.DistanceTo(to) * 0.5f + Radius + Const.Unit, _candidates);

        foreach (var target in _candidates)
        {
            // Раскладка собрана в начале шага, и кто-то из неё мог погибнуть за этот же шаг:
            // проверка живости здесь обязательна, тогда как разрез индекса делал её сам
            if (target.Faction != TargetSide || target.Health.IsDead
                || target is ILive { Live: false })
                continue;

            var center = target.GlobalPosition;
            float reach = target.HitRadius + Radius;

            // Ближайшая точка отрезка за кадр — так снаряд не проскакивает цель насквозь
            var closest = Geometry2D.GetClosestPointToSegment(center, from, to);
            if (closest.DistanceSquaredTo(center) > reach * reach)
                continue;

            float distance = from.DistanceSquaredTo(center);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = target;
        }

        return best;
    }



    public override void _Draw()
    {
        ShapeDraw.Circle(this, Vector2.Zero, Radius, ShapeStyle.Solid(Tint));

        // Короткий хвост назад по движению — очередь читается как очередь.
        // Ноду не поворачиваем, поэтому локальные координаты совпадают с мировым смещением.
        // Толщина хвоста в мировых единицах равна радиусу снаряда.
        var tail = -Velocity.Normalized() * Radius * 4f;
        ShapeDraw.Line(this, Vector2.Zero, tail,
            ShapeStyle.Outline(new Color(Tint, 0.45f), Radius, WidthMode.World));
    }
}
