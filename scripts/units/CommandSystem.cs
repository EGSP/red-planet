using Godot;

/// <summary>
/// Приказы игрока и режим строительства.
/// Клиент отдаёт намерения, применяет их симуляция — привычка на будущее.
/// </summary>
public partial class CommandSystem : GameSystem
{
    [Export] public PackedScene BlueprintScene;

    private PlacementGhost _ghost;

    /// <summary>Курсор в мировых координатах — берём из событий, а не опросом.</summary>
    private Vector2 _cursor;

    public BuildableDef Pending { get; private set; }

    protected override void OnRegister() => GM.Command = this;

    public void BeginBuild(BuildableDef def)
    {
        Pending = def;
        EnsureGhost();
        _ghost.Def = def;
        _ghost.Visible = true;
    }

    public void CancelBuild()
    {
        Pending = null;
        if (_ghost != null)
            _ghost.Visible = false;
    }

    public override void Step(double dt)
    {
        if (Pending == null || GM.WorldRoot == null)
            return;

        EnsureGhost();

        var origin = OriginUnderCursor(Pending);
        _ghost.Origin = origin;
        _ghost.Valid = GM.Grid.IsFree(origin, Pending);
        _ghost.QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (GM.WorldRoot == null)
            return;

        if (@event is InputEventMouse mouseEvent)
            _cursor = GetViewport().GetCanvasTransform().AffineInverse() * mouseEvent.Position;

        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            CancelBuild();
            return;
        }

        if (@event is not InputEventMouseButton { Pressed: true } mouse)
            return;

        switch (mouse.ButtonIndex)
        {
            case MouseButton.Left when Pending != null:
                PlaceBlueprint();
                break;

            case MouseButton.Right when Pending != null:
                CancelBuild();
                break;

            case MouseButton.Right:
                IssueOrder();
                break;
        }
    }

    private void PlaceBlueprint()
    {
        var def = Pending;
        var origin = OriginUnderCursor(def);

        if (!GM.Grid.IsFree(origin, def) || BlueprintScene == null)
            return;

        var blueprint = GM.Spawn.SpawnBlueprint(BlueprintScene, def, origin);

        GM.Events.Append(new BlueprintPlaced
        {
            EntityId = blueprint.Id,
            DefId = def.Id,
            Cell = origin,
        });

        SendCommander(OrderKind.Build, blueprint);

        if (!Input.IsKeyPressed(Key.Shift))
            CancelBuild();
    }

    private void IssueOrder()
    {
        var commander = GM.Commander;
        if (commander == null)
            return;

        // Враг под курсором важнее клетки: щёлкнули по цели — значит приказ атаковать
        var victim = EnemyUnderCursor();
        if (victim != null)
        {
            commander.SetOrders(Order.Attack(victim));
            return;
        }

        var cell = Const.WorldToCell(_cursor);

        foreach (var node in GetTree().GetNodesInGroup("ore"))
        {
            if (node is OreDeposit deposit && deposit.Cell == cell && deposit.NeedsWork)
            {
                SendCommander(OrderKind.Mine, deposit);
                return;
            }
        }

        commander.SetOrders(Order.MoveTo(_cursor));
    }

    /// <summary>
    /// Враг, по которому щёлкнули. Корпус небольшой, поэтому даём небольшой припуск —
    /// попадать точно в кружок мышью неудобно, а промах уводит коммандера гулять.
    /// </summary>
    private Node2D EnemyUnderCursor()
    {
        Node2D best = null;
        float bestDistance = float.MaxValue;

        foreach (var node in GetTree().GetNodesInGroup("enemy"))
        {
            if (node is not Node2D enemy || !Targeting.IsValid(node))
                continue;

            float reach = (node as IDamageable)?.HitRadius ?? Const.Unit * 0.4f;
            reach += Const.Unit * 0.25f;

            float distance = enemy.GlobalPosition.DistanceTo(_cursor);
            if (distance > reach || distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = enemy;
        }

        return best;
    }

    /// <summary>Цепочка приказов: далеко — сначала дойти, потом работать.</summary>
    private void SendCommander(OrderKind kind, WorkNode target)
    {
        var commander = GM.Commander;
        if (commander == null || commander.Def == null)
            return;

        float range = commander.Def.ToolRange * Const.Unit;
        var center = target.GlobalPosition;

        if (commander.GlobalPosition.DistanceTo(center) <= range)
        {
            commander.SetOrders(Order.Work(kind, target));
            return;
        }

        var direction = (commander.GlobalPosition - center).Normalized();
        if (direction == Vector2.Zero)
            direction = Vector2.Right;

        commander.SetOrders(
            Order.MoveTo(center + direction * range * 0.8f),
            Order.Work(kind, target));
    }

    private Vector2I OriginUnderCursor(BuildableDef def)
    {
        var cell = Const.WorldToCell(_cursor);
        return cell - new Vector2I(def.Width / 2, def.Height / 2);
    }

    private void EnsureGhost()
    {
        if (_ghost != null && IsInstanceValid(_ghost))
            return;

        _ghost = new PlacementGhost { ZIndex = 10 };
        GM.WorldRoot.AddChild(_ghost);
    }
}
