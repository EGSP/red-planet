using System;
using Godot;

/// <summary>
/// Главное меню — ветка приложения, соседняя с сессией и взаимоисключающая с нею.
/// Пока меню открыто, игрового мира не существует, поэтому обращаться отсюда
/// к GameManager нельзя и незачем: единственный собеседник меню — <see cref="Root"/>.
///
/// Разметка собирается кодом, как и в остальном интерфейсе проекта: сцена держит
/// один узел со скриптом, и правка меню не требует возни в редакторе.
/// </summary>
public partial class MainMenu : CanvasLayer
{
    private static readonly Color Background = new(0.07f, 0.06f, 0.08f);

    public override void _Ready()
    {
        var frame = new UiFrame();
        AddChild(frame);

        // Фон непрозрачный: за меню нет ни камеры, ни мира, и без него виден пустой буфер
        var background = new ColorRect { Color = Background };
        frame.AddChild(background);
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var center = new CenterContainer();
        frame.AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);
        center.AddChild(box);

        var title = new Label
        {
            Text = "RED PLANET",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 46);
        title.AddThemeColorOverride("font_color", new Color(1f, 0.55f, 0.4f));
        box.AddChild(title);

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 24) });

        AddButton(box, "Новая игра", () => this.Ancestor<Root>()?.NewGame());
        AddButton(box, "Выйти из игры", () => this.Ancestor<Root>()?.QuitGame());
    }

    /// <summary>
    /// Корень ищем в момент нажатия, а не при сборке меню: к первому клику дерево заведомо
    /// собрано, тогда как на _Ready порядок готовности узлов зависел бы от сцены.
    /// </summary>
    private static void AddButton(Node parent, string text, Action pressed)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(280, 46),
        };

        button.AddThemeFontSizeOverride("font_size", 18);
        button.Pressed += () => pressed();

        parent.AddChild(button);
    }
}
