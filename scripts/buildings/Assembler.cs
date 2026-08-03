using Godot;

/// <summary>
/// Башня-сборщик: тот же фабрикатор, только здание. Строит каркасы и чинит постройки
/// в пределах своего манипулятора — что попало в круг, то и обслуживает.
///
/// ЧЕМ ЗАНЯТЬСЯ, РЕШАЕТ НЕ ОНА. Задачи ей раздаёт <see cref="PlayerAiSystem"/> наравне
/// с подвижными исполнителями: порядок предпочтений у них общий, поиск работы общий
/// (см. <see cref="Jobs"/>), а различаются они только пределами поиска, и те выводятся
/// из определения. Раньше выбор занятия был написан здесь, а отдельная система лишь
/// спрашивала башню и ставила ей же выбранное, — то есть одно правило жило в двух слоях,
/// хотя везде в остальном решает система, а сущность исполняет.
///
/// В стройке башня участвует как обычный исполнитель: подключается к каркасу своей
/// мощностью, и дальше каркас сам заявляет спрос. А ремонт она делает сама, поэтому
/// в экономике участвует ещё и напрямую.
/// </summary>
public partial class Assembler : Building, IWorker
{
    /// <summary>
    /// Манипулятор башни. Такой же инструмент, как рука фабрикатора, и лежит в том же
    /// списке определения: раньше его мощность и прожорливость были полями сцены,
    /// то есть настраивались не там, где у всех остальных.
    /// </summary>
    private WorkToolDefinition Arm => Definition?.BuildTool;

    private float BuildPower => Arm?.Power ?? 0f;

    private float EnergyPerPower => Arm?.EnergyPerPower ?? 0f;

    private WorkNode _attached;
    private Node2D _repairTarget;

    public bool Working => Alive.Is(_attached) || Alive.Is(_repairTarget);

    public override void _Process(double delta) => QueueRedraw();

    /// <summary>Башня стоит на месте, поэтому ходить ей некуда — только работать.</summary>
    public override OrderSet AllowedOrders =>
        OrderSet.None.With(OrderKind.Build).With(OrderKind.Repair);

    /// <summary>Башня стоит на месте, поэтому её якорь внимания — она сама.</summary>
    public Vector2 Anchor => GlobalPosition;

    public override void RunOrder(Order order, double dt)
    {
        switch (order.Kind)
        {
            case OrderKind.Build:
                _repairTarget = null;
                Work(order);
                return;

            case OrderKind.Repair:
                Detach();
                _repairTarget = order.Entity;
                return;
        }
    }

    /// <summary>
    /// Стройка в пределах манипулятора. Дальнее башне недоступно вовсе: дойти она не может,
    /// и приказ на далёкое место просто снимается — иначе он висел бы в очереди вечно.
    ///
    /// План башня ставит так же, как это делает фабрикатор: превращает в каркас, а цель
    /// приказа переезжает с плана на каркас сама, поэтому подключиться к нему можно тем же
    /// кадром — цель приказа читается заново, а не берётся из прежней переменной.
    /// </summary>
    private void Work(Order order)
    {
        var site = order.Target;

        if (site == null
            || GlobalPosition.DistanceTo(site.GlobalPosition) > Definition.WorkRangePx)
        {
            Detach();
            Orders.DropCurrent();
            return;
        }

        if (site is BuildPlan plan)
        {
            Detach();
            plan.Realize();
        }

        if (order.Target is WorkNode node)
            Attach(node);
    }

    public override void OnIdle(double dt)
    {
        Detach();
        _repairTarget = null;
    }

    private void Attach(WorkNode node)
    {
        if (_attached == node)
            return;

        Detach();
        node.AttachWorker(Id, BuildPower, BuildPower * EnergyPerPower);
        _attached = node;
    }

    private void Detach()
    {
        if (Alive.Is(_attached))
            _attached.DetachWorker(Id);

        _attached = null;
    }

    public override void Declare(EconomyLedger ledger)
    {
        base.Declare(ledger);

        // Стройку заявляет каркас — мы для него обычный исполнитель.
        // А ремонт наш собственный, и просить ресурсы под него приходится самим
        if (Alive.Is(_repairTarget))
            Repair.Declare(ledger, BuildPower, EnergyPerPower);
    }

    public override void Run(double dt, EconomyRates rates)
    {
        base.Run(dt, rates);

        if (Alive.Is(_repairTarget) && _repairTarget is IRepairable repairable)
            Repair.Run(repairable, BuildPower, EnergyPerPower, dt, rates);
    }

    public override void OnDestroyed()
    {
        Detach();
        _repairTarget = null;
        base.OnDestroyed();
    }

    public override void _ExitTree() => Detach();

    public override void _Draw()
    {
        base._Draw();

        if (Definition == null)
            return;

        float half = Const.Unit * 0.5f;

        // Луч к тому, с чем работаем: сразу видно, чем башня занята
        var target = Alive.Is(_attached) ? (Node2D)_attached : _repairTarget;

        if (Alive.Is(target))
            ShapeDraw.Line(this, Vector2.Zero, ToLocal(target.GlobalPosition),
                DrawTheme.Line(VizKind.WorkBeamRepair));

        // Манипулятор: три коротких луча из центра — знак того, что башня рабочая
        var arm = Working
            ? DrawTheme.Hue(VizKind.WorkBeamRepair)
            : new Color(0.55f, 0.6f, 0.6f);
        var armLine = ShapeStyle.Outline(arm, 3f, WidthMode.Screen);

        for (int i = 0; i < 3; i++)
        {
            float angle = Mathf.Tau * i / 3f - Mathf.Pi * 0.5f;
            ShapeDraw.Line(this, Vector2.Zero, Heading.Forward(angle) * half * 0.7f, armLine);
        }

        ShapeDraw.Circle(this, Vector2.Zero, half * 0.22f, ShapeStyle.Solid(arm));
    }
}
