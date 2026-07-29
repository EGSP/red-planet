using Godot;

/// <summary>
/// Башня-сборщик: делает работу фабрикатора, но с места не сходит. Строит каркасы
/// и чинит постройки в радиусе своего обзора — что попало в круг, то и обслуживает.
///
/// Логика выбора работы общая с ботами (см. Jobs): сначала стройка, потом ремонт.
/// Разница ровно одна — бот к работе идёт, а башня ждёт, пока работа окажется рядом.
///
/// В стройке башня участвует как обычный исполнитель: подключается к каркасу своей
/// мощностью, и дальше каркас сам заявляет спрос. А ремонт она делает сама, поэтому
/// в экономике участвует ещё и напрямую.
/// </summary>
public partial class Assembler : Building
{
    /// <summary>Мощность инструмента — она же строит, она же чинит, отдельной ремонтной нет.</summary>
    [Export] public float BuildPower = 8f;

    /// <summary>Сколько энергии в секунду съедает единица мощности инструмента.</summary>
    [Export] public float EnergyPerPower = 4f;

    /// <summary>Берётся ли башня чинить юнитов, а не только постройки.</summary>
    [Export] public bool RepairUnits = true;

    private WorkNode _attached;
    private Node2D _repairTarget;

    public bool Working => Alive.Is(_attached) || Alive.Is(_repairTarget);

    public override void _Process(double delta) => QueueRedraw();

    /// <summary>Башня стоит на месте, поэтому ходить ей некуда — только работать.</summary>
    public override OrderSet AllowedOrders =>
        OrderSet.None.With(OrderKind.Build).With(OrderKind.Repair);

    /// <summary>
    /// Чем заняться, если приказа нет. Решение принимает AssemblerSystem — она же и ставит
    /// приказ; башня его только исполняет. Порядок общий с ботами: сначала стройка,
    /// потом ремонт, и всё в пределах своего круга.
    /// </summary>
    public Order ChooseJob()
    {
        float reach = VisionRadius;

        var blueprint = Jobs.NearestBlueprint(GlobalPosition, reach);
        if (blueprint != null)
            return Order.Work(OrderKind.Build, blueprint);

        var damaged = Jobs.NearestDamaged(GlobalPosition, reach, RepairUnits);
        return damaged != null ? Order.Repair(damaged) : null;
    }

    public override void RunOrder(Order order, double dt)
    {
        switch (order.Kind)
        {
            case OrderKind.Build:
                _repairTarget = null;
                Attach(order.Target);
                return;

            case OrderKind.Repair:
                Detach();
                _repairTarget = order.Entity;
                return;
        }
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

        if (Def == null)
            return;

        float half = Const.Unit * 0.5f;

        // Луч к тому, с чем работаем: сразу видно, чем башня занята
        var target = Alive.Is(_attached) ? (Node2D)_attached : _repairTarget;

        if (Alive.Is(target))
            DrawLine(Vector2.Zero, ToLocal(target.GlobalPosition),
                new Color(0.6f, 1f, 0.7f, 0.55f), 2f);

        // Манипулятор: три коротких луча из центра — знак того, что башня рабочая
        var arm = Working ? new Color(0.6f, 1f, 0.7f) : new Color(0.55f, 0.6f, 0.6f);

        for (int i = 0; i < 3; i++)
        {
            float angle = Mathf.Tau * i / 3f - Mathf.Pi * 0.5f;
            DrawLine(Vector2.Zero, Heading.Forward(angle) * half * 0.7f, arm, 3f);
        }

        DrawCircle(Vector2.Zero, half * 0.22f, arm);
    }
}
