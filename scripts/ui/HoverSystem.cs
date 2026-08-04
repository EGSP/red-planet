using Godot;

/// <summary>
/// Наведение: какая сущность мира находится под курсором прямо сейчас.
///
/// ЗАЧЕМ ОТДЕЛЬНОЙ СИСТЕМОЙ. Наведение нужно сразу двум читателям — обводке в мире
/// (<see cref="HoverOverlay"/>) и справке внизу экрана (<see cref="HoverPanel"/>), — и оба
/// должны видеть одну и ту же сущность. Если бы каждый искал её сам, разбор цели повторялся
/// бы дважды за кадр и мог разойтись в мелочах припуска. Сами сущности о наведении не знают
/// вовсе: ни один их метод здесь не зовётся, читается только положение и справочник.
///
/// ОТ ВЫДЕЛЕНИЯ НЕ ЗАВИСИТ. <see cref="CommandSystem"/> ищет под курсором того, кому можно
/// приказать, поэтому его разбор ограничен своей стороной и набором приказов. Здесь правило
/// шире: показать сведения полагается о чём угодно видимом — о чужом юните, о каркасе,
/// о постройке без единого доступного приказа.
///
/// Исполняется в графическом цикле (<see cref="UpdateCycle.Process"/>) после
/// <see cref="CursorSystem"/>: мировая точка курсора к этому мигу пересчитана по текущему
/// canvas transform.
/// </summary>
public partial class HoverSystem : GameSystem
{
    /// <summary>Насколько промахивается мышь: припуск к границе цели, в пикселях.</summary>
    [Export] public float PickSlack = Const.Unit * 0.2f;

    private CursorSystem _cursor;
    private HoverOverlay _overlay;

    /// <summary>
    /// Сущность под курсором либо null. Читают обводка и справка; больше её никто
    /// не спрашивает, и на симуляцию она не влияет.
    /// </summary>
    public IDamageable Hovered { get; private set; }

    protected override void OnLink()
    {
        _cursor = GM.System<CursorSystem>();

        if (_cursor == null)
            GD.PushError("[HoverSystem] CursorSystem не найдена: наведение недоступно");

        if (GM.Playground != null)
            EnsureNodes();
    }

    public override void Step(double dt)
    {
        if (GM.Playground == null || _cursor == null)
        {
            Hovered = null;
            return;
        }

        EnsureNodes();

        Hovered = Suppressed ? null : Pick(_cursor.ActualWorldPosition);
    }

    /// <summary>
    /// Когда наведение молчит.
    ///
    /// Рамка выделения и призрак застройки заняты той же точкой экрана, и обводка под ними
    /// означала бы, что игрок целится в сущность, тогда как он размечает область или ставит
    /// постройку.
    ///
    /// Курсор над элементом интерфейса тоже гасит наведение: под строительной панелью
    /// сущности мира не видно, и подсвечивать её незачем. Спрашивается именно тот элемент,
    /// который принимает мышь, — панели с <c>MouseFilter.Ignore</c> сюда не попадают,
    /// и наведение сквозь них работает по-прежнему.
    /// </summary>
    private bool Suppressed
    {
        get
        {
            var command = GM.Command;

            if (command != null && (command.Banding || command.Building))
                return true;

            return GetViewport()?.GuiGetHoveredControl() != null;
        }
    }

    /// <summary>
    /// Ближайшая подходящая цель в точке. Точная позиция берётся без прогноза:
    /// справка утверждает, что именно лежит под указателем, и упреждать здесь нечего.
    /// </summary>
    private IDamageable Pick(Vector2 point) =>
        GM.Index.All<IDamageable>()
            .Where(target => Pickable(target) && Hit(target, point))
            .Nearest(point, target => target.GlobalPosition);

    /// <summary>
    /// Годится ли сущность под наведение. Скрытая туманом войны не годится: её узел погашен
    /// <see cref="VisionSystem"/>, и справка о невидимом противнике выдавала бы разведку,
    /// которой у игрока нет. Выезжающий из корпуса завода тоже пропускается — по тому же
    /// правилу, по которому его нельзя выделить.
    /// </summary>
    private static bool Pickable(IDamageable target) =>
        target is Node2D node
        && Alive.Is(node)
        && node.IsVisibleInTree()
        && !Targeting.Leaving(target);

    /// <summary>
    /// Попадание в границу цели. У занимающей место сущности граница — её прямоугольник:
    /// у постройки два на четыре описанная окружность накрыла бы вдвое больше площади,
    /// чем сама постройка, и курсор ловил бы её далеко за углом.
    /// </summary>
    private bool Hit(IDamageable target, Vector2 point) =>
        target is IObstacle { Footprint.IsEmpty: false } obstacle
            ? obstacle.Footprint.Grow(PickSlack).HasPoint(point)
            : target.GlobalPosition.DistanceTo(point) <= target.HitRadius + PickSlack;

    private void EnsureNodes()
    {
        if (_overlay == null || !IsInstanceValid(_overlay))
            _overlay = GM.Playground.Add(WorldLayer.Effects, new HoverOverlay());
    }
}
