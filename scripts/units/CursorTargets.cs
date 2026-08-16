using System.Collections.Generic;
using Godot;

/// <summary>
/// Что лежит под указателем и во что это превращается.
///
/// ЗАЧЕМ ОТДЕЛЬНОЙ СУЩНОСТЬЮ. Разбор цели — это шесть обходов мира (враги, каркасы, планы,
/// повреждённые свои, выделяемые, места работы) с общими правилами припуска и отсева,
/// и потребителей у него три: выдача приказа, вид курсора и указание области. Пока обходы
/// лежали в <see cref="CommandSystem"/>, разбор был перемешан с разбором нажатий, хотя
/// ни от состояния ввода, ни от выделения он не зависит — только от точки.
///
/// РЕШЕНИЙ ЗДЕСЬ НЕ ПРИНИМАЕТСЯ, кроме одного: какой вид приказа следует из найденного.
/// Оно живёт тут потому, что выводится ровно из разобранной цели, и разойтись с ней
/// не должно — курсор, обещающий не то, что случится, хуже отсутствия курсора.
/// </summary>
public sealed class CursorTargets
{
    /// <summary>Насколько промахивается мышь: припуск к радиусу цели, в пикселях.</summary>
    public const float PickSlack = Const.Unit * 0.25f;

    private readonly GameManager _gm;

    public CursorTargets(GameManager gm) => _gm = gm;

    /// <summary>
    /// Всё, что лежит в точке и может стать целью приказа. Разбор один на всех потребителей:
    /// расхождение между тем, что обещает курсор, и тем, что сделает нажатие, было бы враньём.
    /// </summary>
    public (Node2D Victim, IWorkSite Site, Node2D Damaged, Node2D Leader) Resolve(
        Vector2 point, List<IOrderable> recipients)
    {
        var occupant = _gm.Obstacles.At(point) as Node;

        return (
            EnemyAt(point),
            SiteAt(point),
            Repairable(occupant as Node2D) ?? DamagedUnitAt(point),
            Leader(point, recipients));
    }

    /// <summary>
    /// Какой приказ уйдёт по нажатию в этой точке, если вид его не задан игроком.
    ///
    /// ПОРЯДОК ТОТ ЖЕ, ЧТО У ВЫДАЧИ, и повторён он здесь не по недосмотру: выдача перебирает
    /// получателей, у каждого спрашивая набор, а здесь нужен один ответ на весь отряд.
    /// Общим у них остаётся разбор цели, то есть та часть, расхождение в которой было бы
    /// враньём; выбор же вида различается ровно тем, что здесь довольно одного согласного
    /// получателя.
    /// </summary>
    public OrderKind? KindAt(Vector2 point, List<IOrderable> recipients)
    {
        if (recipients.Count == 0)
            return null;

        var (victim, site, damaged, leader) = Resolve(point, recipients);

        if (victim != null && AnyTakes(recipients, OrderKind.Attack, victim))
            return OrderKind.Attack;

        if (site != null && AnyTakes(recipients, OrderKind.Build, site))
            return OrderKind.Build;

        if (damaged != null && AnyTakes(recipients, OrderKind.Repair, damaged))
            return OrderKind.Repair;

        if (leader != null && AnyTakes(recipients, OrderKind.Follow, leader))
            return OrderKind.Follow;

        return AnyTakes(recipients, OrderKind.Move, null) ? OrderKind.Move : null;
    }

    /// <summary>Возьмётся ли за такой приказ хоть кто-то из получателей.</summary>
    public static bool AnyTakes(List<IOrderable> recipients, OrderKind kind, object target)
    {
        foreach (var actor in recipients)
            if (actor.Orders.Allows(kind) && (target == null || Other(target, actor)))
                return true;

        return false;
    }

    /// <summary>
    /// Годится ли цель этому получателю: она не он сам.
    ///
    /// Одна проверка на все виды приказов, хотя сейчас сработать она может не у каждого:
    /// врагом получатель не бывает никогда, а вот местом работы и повреждённой целью бывает —
    /// каркас выделяется и принимает приказы наравне с постройкой, а подбитый ремонтник
    /// стоит в том же отряде, которому игрок отдаёт приказ. Проверка стоит у всех потому,
    /// что правило одно, а не три, и держать его в стольких местах, сколько видов приказов,
    /// значило бы забыть о нём при появлении следующего.
    /// </summary>
    public static bool Other(object target, IOrderable actor) => !ReferenceEquals(target, actor);

    /// <summary>
    /// Выделяем своё с непустым видимым набором приказов: принятые плюс мягкие
    /// (умеет, но в определении не разрешено). Мягкие нужны, чтобы сущность с забытым
    /// <c>[orders]</c> оставалась доступной для проверки в панели. Юнит, ещё выезжающий
    /// из корпуса завода, некликабелен.
    /// </summary>
    public static bool Commandable(IOrderable actor) =>
        actor.Faction == Faction.Player
        && (actor.AllowedOrders.Any || actor.SoftOrders.Any)
        && !Targeting.Leaving(actor);

    /// <summary>Своя управляемая сущность в точке — ближайшая, если их несколько.</summary>
    public IOrderable ActorAt(Vector2 point) =>
        _gm.Index.All<IOrderable>()
            .Where(actor => Commandable(actor) && Hit(actor, point))
            .Nearest(point, actor => actor.GlobalPosition);

    private static bool Hit(IOrderable actor, Vector2 point)
    {
        float reach = ((actor as IDamageable)?.HitRadius ?? Const.Unit * 0.5f) + PickSlack;
        return actor.GlobalPosition.DistanceTo(point) <= reach;
    }

    /// <summary>
    /// За кем идти: своя сущность под курсором, не входящая в само выделение.
    ///
    /// ВЫДЕЛЕННЫЙ ВЕДУЩИМ НЕ БЫВАЕТ. Щелчок по своему же отряду — обычное указание идти туда,
    /// где он стоит, и превращать его в сопровождение нельзя: отряд принялся бы ходить сам
    /// за собой, а половина его при этом получила бы приказ, которого игрок не отдавал.
    /// Поэтому проверка стоит здесь, у разбора цели, а не у раздачи: приказ сопровождения
    /// либо есть у всех получателей, либо его нет вовсе.
    /// </summary>
    public Node2D Leader(Vector2 point, List<IOrderable> recipients) =>
        ActorAt(point) is { } found && !recipients.Contains(found) ? found as Node2D : null;

    /// <summary>
    /// Место работы под указателем: каркас, которому нужна работа, либо размеченный план.
    /// Каркас берётся из карты препятствий, план — обходом, потому что места в карте
    /// он не занимает (см. <see cref="PlanAt"/>).
    /// </summary>
    public IWorkSite SiteAt(Vector2 point) =>
        _gm.Obstacles.At(point) is Blueprint { NeedsWork: true } frame ? frame : PlanAt(point);

    /// <summary>
    /// План под курсором. Спрашивается отдельно от карты препятствий, потому что план
    /// в ней не значится: место он держит только для правила постановки.
    /// </summary>
    public BuildPlan PlanAt(Vector2 point)
    {
        foreach (var plan in _gm.Index.All<BuildPlan>())
            if (plan.NeedsWork && plan.Footprint.HasPoint(point))
                return plan;

        return null;
    }

    /// <summary>
    /// Все места работы внутри круга — то, что помощь строительству развернёт в цепочку
    /// приказов. Порядок — от центра наружу: игрок целится в важное серединой жеста,
    /// а не его краем.
    /// </summary>
    public List<IWorkSite> SitesWithin(Vector2 center, float radius)
    {
        var sites = new List<IWorkSite>();

        foreach (var site in _gm.Index.All<IWorkSite>())
            if (site.NeedsWork && site.GlobalPosition.DistanceTo(center) <= radius)
                sites.Add(site);

        sites.Sort((a, b) => a.GlobalPosition.DistanceSquaredTo(center)
            .CompareTo(b.GlobalPosition.DistanceSquaredTo(center)));

        return sites;
    }

    /// <summary>
    /// Враг в указанной точке. Корпус небольшой, поэтому даём припуск —
    /// попадать точно в кружок мышью неудобно, а промах уводит юнита гулять.
    /// </summary>
    public Node2D EnemyAt(Vector2 point) =>
        _gm.Units[Faction.Hostile]
            .Where(enemy => !Targeting.Leaving(enemy)
                            && enemy.GlobalPosition.DistanceTo(point)
                            <= enemy.HitRadius + PickSlack)
            .Nearest(point, enemy => enemy.GlobalPosition);

    /// <summary>Повреждённое своё под указателем: постройка из карты либо юнит.</summary>
    public Node2D DamagedAt(Vector2 point) =>
        Repairable(_gm.Obstacles.At(point) as Node2D) ?? DamagedUnitAt(point);

    private Node2D DamagedUnitAt(Vector2 point) =>
        _gm.Units[Faction.Player]
            .Where(unit => !Targeting.Leaving(unit)
                           && unit.GlobalPosition.DistanceTo(point) <= unit.HitRadius + PickSlack)
            .Nearest(point, unit => unit.GlobalPosition) is { } found && Repairable(found) != null
            ? found
            : null;

    /// <summary>Годится ли под ремонт: своё, повреждённое и с курсом ремонта.</summary>
    public static Node2D Repairable(Node2D node) =>
        node is IRepairable { Health: { Ratio: < 0.999f } } repairable
        && repairable.HealthPerMetal > 0f
        && node is IDamageable { Faction: Faction.Player }
            ? node
            : null;
}
