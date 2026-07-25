using Godot;

/// <summary>
/// Система — нода: её добавляют в сцену и настраивают в инспекторе.
/// Сама регистрируется в планировщике, порядок исполнения задаётся полем Order.
/// </summary>
public partial class GameSystem : Node
{
    [Export] public Phase Phase = Phase.Simulate;

    /// <summary>Порядок внутри фазы — чем меньше, тем раньше.</summary>
    [Export] public int StepOrder;

    protected GameManager GM => GameManager.I;

    public override void _Ready()
    {
        GM.Scheduler.Add(this);
        OnRegister();
    }

    public override void _ExitTree() => GM?.Scheduler.Remove(this);

    /// <summary>Подписки и начальная настройка.</summary>
    protected virtual void OnRegister() { }

    /// <summary>Шаг системы в своей фазе кадра.</summary>
    public virtual void Step(double dt) { }
}
