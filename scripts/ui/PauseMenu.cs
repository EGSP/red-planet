using System;
using Godot;

/// <summary>
/// Меню паузы — отдельное дерево интерфейса, соседнее с основным HUD и лежащее выше него
/// по слою. Разделены они намеренно: HUD показывает состояние идущей игры и живёт вместе
/// с нею, меню паузы управляет самой игрой и обязано работать тогда, когда симуляция стоит.
/// Сведи их в одно дерево — и видимость меню пришлось бы согласовывать с каждым элементом
/// HUD по отдельности.
///
/// Состояние паузы хранит <see cref="Session"/>, а меню лишь отражает его и просит
/// изменить. Поэтому пауза остаётся согласованной, каким бы способом её ни включили —
/// клавишей, кнопкой на HUD или из кода.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    private static readonly Color Shade = new(0f, 0f, 0f, 0.55f);

    private Control _frame;
    private Session _session;

    public override void _Ready()
    {
        Build();

        _session = this.Ancestor<Session>();

        if (_session == null)
        {
            GD.PushError("[PauseMenu] меню паузы вне сессии");
            return;
        }

        _session.PauseChanged += OnPauseChanged;
        _frame.Visible = _session.Paused;
    }

    public override void _ExitTree()
    {
        if (_session != null && Alive.Is(_session))
            _session.PauseChanged -= OnPauseChanged;
    }

    private void OnPauseChanged(bool paused)
    {
        // После исхода партии пауза тоже поднимается, но показывает OutcomeMenu, не это
        _frame.Visible = paused && _session.Outcome == SessionOutcome.None;
    }

    /// <summary>
    /// Escape означает «отменить текущее» и лишь затем «открыть меню». Порядок разбора
    /// решается здесь, а не порядком обхода дерева при рассылке ввода: сначала спрашиваем
    /// систему приказов, было ли что отменять, и открываем паузу только если не было.
    /// Так привычное поведение сохраняется — первый Escape снимает режим постройки,
    /// второй сбрасывает выделение, третий открывает меню.
    ///
    /// На самой паузе ветка Systems отключена, ввод до неё не доходит, и Escape просто
    /// закрывает меню. После исхода партии Escape ничего не делает: снять паузу нельзя.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
            return;

        if (_session == null || _session.Outcome != SessionOutcome.None)
            return;

        GetViewport().SetInputAsHandled();

        if (_session.Paused)
        {
            _session.SetPaused(false);
            return;
        }

        if (_session.Systems?.System<CommandSystem>() is { } command && command.CancelContext())
            return;

        _session.SetPaused(true);
    }

    private void Build()
    {
        // Слой выше HUD: меню паузы перекрывает интерфейс игры, а не соседствует с ним
        Layer = 10;

        _frame = new UiFrame { Visible = false };
        AddChild(_frame);

        // Затемнение заодно перехватывает мышь — на паузе по HUD не щёлкнуть
        var shade = new ColorRect { Color = Shade };
        _frame.AddChild(shade);
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var center = new CenterContainer();
        _frame.AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        center.AddChild(box);

        var title = new Label
        {
            Text = "Пауза",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 34);
        box.AddChild(title);

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

        AddButton(box, "Продолжить", () => _session.SetPaused(false));
        AddButton(box, "Перезапустить", Restart);
        AddButton(box, "Выйти в главное меню", ExitToMainMenu);
    }

    /// <summary>
    /// Перезапуск: нынешняя сессия удаляется целиком и создаётся новая. Снимать паузу
    /// перед этим не нужно — вместе с сессией уходит и её состояние, а новая рождается
    /// идущей.
    /// </summary>
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
