using System;
using Godot;

/// <summary>
/// Мелкие повторяемые элементы редактора: кнопки действий, заголовки секций и защита
/// обработчиков от исключений.
///
/// ЗАЧЕМ ЗАЩИТА. Обработчик сигнала Godot исполняется внутри обхода дерева. Исключение,
/// вылетевшее оттуда, прерывает обход: часть виджетов остаётся необновлённой, а редактор
/// выглядит зависшим, причём причина в журнале не связана видимым образом с нажатой
/// кнопкой. <see cref="Run"/> перехватывает исключение, называет действие, при котором оно
/// произошло, и оставляет редактор работоспособным.
///
/// ЗАЧЕМ ОБЩИЕ КНОПКИ. Кнопка «Применить» в режиме сущностей и в инспекторе волн должна
/// выглядеть и вести себя одинаково, включая подсказку выключенного состояния. Пока
/// каждый режим создавал кнопки сам, они расходились по иконкам и по тексту подсказок.
/// </summary>
public static class EditorControls
{
    /// <summary>
    /// Куда сообщать об отказе. Главный экран подставляет сюда строку состояния, поэтому
    /// пользователь видит отказ там же, где и остальные сообщения редактора.
    /// </summary>
    public static Action<string> FaultReporter { get; set; }

    /// <summary>
    /// Выполнить действие обработчика, перехватив исключение.
    /// <paramref name="what"/> называет действие в родительном падеже английским текстом
    /// интерфейса, например <c>"apply changes"</c>.
    /// </summary>
    public static void Run(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            GD.PushError($"[Content editor] {what} failed: {ex}");
            FaultReporter?.Invoke($"{what} failed: {ex.Message}");
        }
    }

    /// <summary>Кнопка с текстом и иконкой темы редактора, добавленная в полосу.</summary>
    public static Button Add(
        BoxContainer bar, string text, string icon, Action action, string tooltip = null)
    {
        var button = new Button
        {
            Text = text,
            Icon = ContentEditorTheme.Icon(icon),
            TooltipText = tooltip ?? "",
        };
        button.Pressed += () => Run(text.Length > 0 ? text.ToLowerInvariant() : icon, action);
        bar.AddChild(button);
        return button;
    }

    /// <summary>
    /// Кнопка возврата к унаследованному значению. Выключенная кнопка объясняет причину:
    /// иначе выключенное состояние читается как неисправность.
    /// </summary>
    public static Button Reset(bool enabled, string tooltip, Action action)
    {
        Texture2D icon = ContentEditorTheme.IconAny("Reload", "Undo");
        var reset = new Button
        {
            Text = icon == null ? "↶" : "",
            Icon = icon,
            Flat = true,
            CustomMinimumSize = new Vector2(24, 24),
        };
        ContentEditorTheme.SetAction(reset, enabled, tooltip, "The key is not written in this file");
        reset.Pressed += () => Run("reset field", action);
        return reset;
    }

    /// <summary>
    /// Заголовок сворачиваемой секции. Стрелка берётся из темы редактора, а при её
    /// отсутствии заменяется знаком: состав EditorIcons меняется между версиями Godot.
    /// </summary>
    public static Button SectionHeading(string title, bool expanded, Action toggle)
    {
        Texture2D arrow = expanded
            ? ContentEditorTheme.Icon("GuiTreeArrowDown")
            : ContentEditorTheme.Icon("GuiTreeArrowRight");
        var heading = new Button
        {
            Text = (arrow == null ? (expanded ? "▼  " : "▶  ") : "") + title,
            Icon = arrow,
            Flat = true,
            Alignment = HorizontalAlignment.Left,
            TooltipText = expanded ? "Collapse section" : "Expand section",
        };
        heading.AddThemeColorOverride("font_color", new Color(0.72f, 0.84f, 1f));
        heading.Pressed += () => Run("toggle section", toggle);
        return heading;
    }

    /// <summary>Подпись пояснения: приглушённая, меньшего кегля, с переносом по словам.</summary>
    public static Label Hint(string text)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(1f, 1f, 1f, 0.55f),
        };
        label.AddThemeFontSizeOverride("font_size", 11);
        return label;
    }

    /// <summary>
    /// Положение разделителя области. В Godot 4.7 у <see cref="SplitContainer"/> несколько
    /// разделителей, и одиночное свойство объявлено устаревшим; редактору достаточно
    /// первого, поскольку все его области содержат ровно две части.
    /// </summary>
    public static int SplitOffset(SplitContainer split) =>
        split.SplitOffsets is { Length: > 0 } offsets ? offsets[0] : 0;

    /// <summary>Назначить положение первого разделителя области.</summary>
    public static void SetSplitOffset(SplitContainer split, int value) =>
        split.SplitOffsets = new[] { value };

    /// <summary>Удалить и освободить всех потомков узла немедленно, а не к концу кадра.</summary>
    public static void ClearChildren(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            // RemoveChild исключает узел из раскладки сразу; один QueueFree оставлял
            // прежнее и новое содержимое рядом до конца кадра.
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }
}
