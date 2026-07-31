using Godot;

/// <summary>
/// Система — нода: её добавляют в сцену и настраивают в инспекторе.
/// Сама регистрируется в планировщике, порядок исполнения задаётся полем Order.
///
/// Менеджера система берёт не из статической ссылки, а из собственных предков: она
/// принадлежит тому корню, под которым висит. Разница видна на сносе сессии — выход
/// из дерева идёт сверху вниз, и к тому мигу, когда очередь доходит до системы,
/// статической ссылки уже нет, а своя — есть.
/// </summary>
public partial class GameSystem : Node
{
    [Export] public Phase Phase = Phase.Simulate;

    /// <summary>Порядок внутри фазы — чем меньше, тем раньше.</summary>
    [Export] public int StepOrder;

    protected GameManager GM { get; private set; }

    public override void _EnterTree()
    {
        GM = Ancestor<GameManager>() ?? GameManager.I;

        if (GM == null)
            GD.PushError($"[{Name}] система вне сессии: композиционный корень не найден");
    }

    public override void _Ready()
    {
        GM?.Scheduler.Add(this);
        OnRegister();
    }

    public override void _ExitTree() => GM?.Scheduler.Remove(this);

    /// <summary>Ближайший предок нужного рода: системы обычно дети корня, но не обязаны.</summary>
    private T Ancestor<T>() where T : Node
    {
        for (var node = GetParent(); node != null; node = node.GetParent())
            if (node is T found)
                return found;

        return null;
    }

    /// <summary>Подписки и начальная настройка.</summary>
    protected virtual void OnRegister() { }

    /// <summary>Шаг системы в своей фазе кадра.</summary>
    public virtual void Step(double dt) { }
}
