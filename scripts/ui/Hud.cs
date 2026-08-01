using Godot;

/// <summary>
/// Кнопка паузы в правом нижнем углу — всё, что осталось за HUD.
///
/// Прежде здесь висела панель со счётом боя и подсказками управления. Она занимала левый
/// верхний угол постоянно, хотя нужна редко, и потому переехала в <see cref="DebugPanel"/>.
/// Следствие принято сознательно: постоянно видимого счёта боя больше нет. Понадобится
/// он как игровая величина, а не как отладочная, — вернётся строкой в <see cref="ResourceBar"/>,
/// а не восстановлением прежней панели.
///
/// Угол выбран свободный: <see cref="Buildbar"/> стоит внизу по центру,
/// <see cref="CommandPanel"/> — справа по центру.
/// </summary>
public partial class Hud : CanvasLayer
{
    public override void _Ready()
    {
        var frame = new UiFrame();
        AddChild(frame);

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        frame.AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var row = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        margin.AddChild(row);

        var column = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        row.AddChild(column);

        column.AddChild(PauseButton());
    }

    /// <summary>
    /// Кнопка просит сессию переключить состояние, а не показывает меню сама:
    /// решение о паузе принимается в одном месте, поэтому клавиша и кнопка не разойдутся.
    /// </summary>
    private Button PauseButton()
    {
        var button = new Button { Text = "Пауза (Esc)" };
        button.Pressed += () => this.Ancestor<Session>()?.TogglePause();
        return button;
    }
}
