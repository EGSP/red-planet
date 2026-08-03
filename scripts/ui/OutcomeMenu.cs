using System;
using Godot;

/// <summary>
/// Меню исхода партии: победа или поражение. Лежит выше HUD и паузы; появляется только
/// когда <see cref="Session"/> зафиксировал исход. Кнопки те же, что у паузы без «Продолжить»:
/// перезапуск и выход в главное меню.
/// </summary>
public partial class OutcomeMenu : CanvasLayer
{
    private static readonly Color Shade = new(0f, 0f, 0f, 0.6f);
    private static readonly Color VictoryColor = new(0.45f, 0.9f, 0.65f);
    private static readonly Color DefeatColor = new(1f, 0.45f, 0.4f);

    private Control _frame;
    private Label _title;
    private Label _subtitle;
    private Session _session;

    public override void _Ready()
    {
        Build();

        _session = this.Ancestor<Session>();

        if (_session == null)
        {
            GD.PushError("[OutcomeMenu] меню исхода вне сессии");
            return;
        }

        _session.OutcomeChanged += OnOutcomeChanged;
        Apply(_session.Outcome);
    }

    public override void _ExitTree()
    {
        if (_session != null && Alive.Is(_session))
            _session.OutcomeChanged -= OnOutcomeChanged;
    }

    private void OnOutcomeChanged(int outcome) => Apply((SessionOutcome)outcome);

    private void Apply(SessionOutcome outcome)
    {
        if (outcome == SessionOutcome.None)
        {
            _frame.Visible = false;
            return;
        }

        bool victory = outcome == SessionOutcome.Victory;
        _title.Text = victory ? "Победа" : "Поражение";
        _title.AddThemeColorOverride("font_color", victory ? VictoryColor : DefeatColor);
        _subtitle.Text = victory
            ? "Портал удержан. Основные силы получили проход."
            : "Крепость уничтожена.";
        _frame.Visible = true;
    }

    private void Build()
    {
        // Выше паузы: исход перекрывает и её
        Layer = 20;

        _frame = new UiFrame { Visible = false };
        AddChild(_frame);

        var shade = new ColorRect { Color = Shade };
        _frame.AddChild(shade);
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var center = new CenterContainer();
        _frame.AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        center.AddChild(box);

        _title = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _title.AddThemeFontSizeOverride("font_size", 40);
        box.AddChild(_title);

        _subtitle = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _subtitle.AddThemeFontSizeOverride("font_size", 16);
        _subtitle.AddThemeColorOverride("font_color", new Color(0.75f, 0.78f, 0.82f));
        box.AddChild(_subtitle);

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

        AddButton(box, "Перезапустить", Restart);
        AddButton(box, "Выйти в главное меню", ExitToMainMenu);
    }

    private void Restart() => this.Ancestor<Root>()?.NewGame();

    private void ExitToMainMenu() => this.Ancestor<Root>()?.OpenMainMenu();

    private static void AddButton(Node parent, string text, Action pressed)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(280, 42),
        };

        button.AddThemeFontSizeOverride("font_size", 17);
        button.Pressed += () => pressed();

        parent.AddChild(button);
    }
}
