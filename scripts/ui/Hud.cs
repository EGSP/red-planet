using Godot;

/// <summary>
/// Общие сведения о ходе игры: счёт боя, подсказки управления и пауза.
///
/// Всё, что зависит от выделения или от состояния базы, вынесено в свои деревья:
/// <see cref="ResourceBar"/> сверху, <see cref="SelectionPanel"/> слева,
/// <see cref="CommandPanel"/> справа, <see cref="Buildbar"/> внизу. Здесь остаётся то,
/// что не привязано ни к тому, ни к другому.
/// </summary>
public partial class Hud : CanvasLayer
{
    private Label _combat;

    public override void _Ready()
    {
        var panel = new PanelContainer
        {
            Position = new Vector2(12, 56),
            CustomMinimumSize = new Vector2(240, 0),
        };
        AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        panel.AddChild(box);

        _combat = new Label { Text = "врагов 0" };
        _combat.AddThemeFontSizeOverride("font_size", 13);
        _combat.AddThemeColorOverride("font_color", new Color(1f, 0.7f, 0.65f));
        box.AddChild(_combat);

        box.AddChild(new HSeparator());

        var hints = new Label
        {
            Text = "ЛКМ — выделить или рамка, ПКМ — приказ по цели\n" +
                   "Shift — дописать в очередь, WASD и колесо — камера",
        };
        hints.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(hints);

        AddPauseButton(box);
    }

    /// <summary>
    /// Кнопка паузы просит сессию переключить состояние, а не показывает меню сама:
    /// решение о паузе принимается в одном месте, поэтому клавиша и кнопка не разойдутся.
    /// </summary>
    private void AddPauseButton(Node parent)
    {
        var button = new Button { Text = "Пауза (Esc)" };
        button.Pressed += () => this.Ancestor<Session>()?.TogglePause();
        parent.AddChild(button);
    }

    public override void _Process(double delta)
    {
        var gm = GameManager.I;
        if (gm == null)
            return;

        var combat = gm.Combat;
        float commanderDamage = gm.Commander?.Health.TotalTaken ?? 0f;

        _combat.Text = $"врагов на карте {combat.EnemiesAlive}   " +
                       $"уничтожено {combat.EnemiesDestroyed}   потеряно {combat.LossesTaken}\n" +
                       $"урон коммандеру {commanderDamage:0}";
    }
}
