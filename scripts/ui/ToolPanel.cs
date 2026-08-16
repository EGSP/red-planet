using System.Collections.Generic;
using Godot;

/// <summary>
/// Общее основание служебных панелей: отладочной (<see cref="DebugPanel"/>) и песочницы
/// (<see cref="SandboxPanel"/>).
///
/// ЗАЧЕМ ОБЩЕЕ ОСНОВАНИЕ. Панели занимают одно и то же место у левого края экрана, поэтому
/// показывать их одновременно нельзя: содержимое накладывалось бы. Правило «открыта не более
/// одной» принадлежит не отдельной панели, а их набору, и держать его в каждой из них значило
/// бы, что при появлении третьей панели правило придётся дописывать во все.
///
/// Набор ведётся статическим списком, поскольку панели живут в разных ветках дерева сцены
/// и общего родителя, у которого можно было бы спросить соседей, не имеют. Выбывшая из дерева
/// панель список покидает: сессия пересобирается по F5, и ссылка на освобождённый узел
/// пережила бы пересборку.
///
/// Клавиша переключения ловится в <see cref="Node._UnhandledKeyInput"/>, то есть раньше систем:
/// служебная панель обязана открываться и на паузе, когда ветка систем обработку не получает.
/// </summary>
public abstract partial class ToolPanel : CanvasLayer
{
    private static readonly List<ToolPanel> Panels = new();

    /// <summary>Каркас содержимого. Видимостью каркаса, а не слоя, и задан показ панели.</summary>
    protected Control Frame;

    /// <summary>Имя действия из <see cref="InputActions"/>, которым панель переключается.</summary>
    protected abstract string ToggleAction { get; }

    /// <summary>Открыта ли панель прямо сейчас.</summary>
    public bool Shown => Frame is { Visible: true };

    public override void _Ready()
    {
        // Выше паузы и экрана исхода: служебное читается поверх любого другого интерфейса
        Layer = 30;

        Frame = new UiFrame();
        AddChild(Frame);

        Build(Frame);

        Frame.Visible = false;
        Panels.Add(this);
    }

    public override void _ExitTree()
    {
        Panels.Remove(this);
        base._ExitTree();
    }

    /// <summary>Собрать содержимое внутри готового каркаса. Зовётся один раз при появлении.</summary>
    protected abstract void Build(Control frame);

    /// <summary>
    /// Панель закрылась — своим нажатием либо открытием соседней. Нужно там, где панель
    /// оставляет за собой состояние вне себя самой: невидимая панель управлять им уже
    /// не может, а игрок о нём не помнит.
    /// </summary>
    protected virtual void OnHidden()
    {
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!@event.IsActionPressed(ToggleAction))
            return;

        Toggle();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// Показать эту панель и закрыть остальные. Повторное нажатие закрывает её саму,
    /// поэтому состояние снимается до показа, а не вместо него.
    /// </summary>
    private void Toggle()
    {
        bool show = !Shown;

        foreach (var panel in Panels)
        {
            if (!Alive.Is(panel) || panel.Frame is not { Visible: true })
                continue;

            panel.Frame.Visible = false;
            panel.OnHidden();
        }

        Frame.Visible = show;
    }
}
