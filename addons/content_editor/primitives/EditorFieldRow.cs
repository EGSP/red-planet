using System;
using Godot;

/// <summary>
/// Описание одной строки правки поля: что показать, чем править и что делать с правкой.
///
/// ЗАЧЕМ ОПИСАНИЕ, А НЕ НАБОР ПАРАМЕТРОВ. Строку строят форма свойств, инспектор волн
/// и блоки <c>[[unit_list]]</c>; у них совпадает всё, кроме источника значения и набора
/// доступных действий. Отдельное описание позволяет вызывающей стороне заполнять только
/// то, что для неё осмысленно, а строке — оставаться одной на весь редактор.
/// </summary>
public sealed class EditorFieldRowSpec
{
    /// <summary>Описание ключа из схемы: подпись, тип значения и пояснение.</summary>
    public ContentFieldSpec Field { get; init; }

    /// <summary>Действующее значение, показанное виджетом.</summary>
    public object Value { get; init; }

    /// <summary>Короткая метка происхождения: «in this file», «default» и подобные.</summary>
    public string SourceLabel { get; init; } = "";

    /// <summary>Развёрнутое пояснение происхождения с путём файла.</summary>
    public string SourceTooltip { get; init; } = "";

    /// <summary>Цвет метки происхождения: им различаются локальное, base, vars и умолчание.</summary>
    public Color SourceColor { get; init; } = new(1f, 1f, 1f, 0.5f);

    /// <summary>Показывать ли кнопку удаления локального ключа.</summary>
    public bool CanReset { get; init; }

    public string ResetTooltip { get; init; } = "";

    /// <summary>Записать новое значение. Вызывается при подтверждении ввода.</summary>
    public Action<object> Commit { get; init; }

    /// <summary>Удалить локальный ключ и вернуть наследование.</summary>
    public Action Reset { get; init; }

    /// <summary>Открыть файл-источник. Пусто — кнопки перехода нет.</summary>
    public Action OpenSource { get; init; }

    public string OpenSourceTooltip { get; init; } = "";

    /// <summary>Наименьшая ширина колонки происхождения и действий.</summary>
    public float ActionsWidth { get; init; } = 176f;
}

/// <summary>
/// Сборка строки правки поля по описанию <see cref="EditorFieldRowSpec"/>.
///
/// Раскладку колонок берёт на себя <see cref="EditorPropertyRow"/>, разбор ввода —
/// <see cref="EditorFieldEditor"/>; здесь остаётся только состав действий и метка
/// происхождения.
/// </summary>
public static class EditorFieldRow
{
    public static Control Build(EditorFieldRowSpec spec)
    {
        var field = spec.Field;
        Control editor = EditorFieldEditor.Build(field, spec.Value, editable: true,
            committed => EditorControls.Run($"edit {field.Key}", () => spec.Commit?.Invoke(committed)));

        var actions = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(spec.ActionsWidth, 0),
        };

        if (!string.IsNullOrEmpty(spec.SourceLabel))
        {
            var source = new Label
            {
                Text = spec.SourceLabel,
                TooltipText = spec.SourceTooltip,
                Modulate = spec.SourceColor,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            source.AddThemeFontSizeOverride("font_size", 11);
            actions.AddChild(source);
        }

        if (spec.Reset != null)
            actions.AddChild(EditorControls.Reset(spec.CanReset, spec.ResetTooltip, spec.Reset));

        if (spec.OpenSource != null)
            actions.AddChild(OpenSourceButton(spec));

        var row = new EditorPropertyRow();
        row.Configure(field.Label, Tooltip(field), editor, actions, EditorFieldEditor.BelowTitle(field));
        return row;
    }

    /// <summary>Подсказка заголовка: полное имя ключа и, если задано, пояснение схемы.</summary>
    private static string Tooltip(ContentFieldSpec field)
    {
        string tooltip = $"{field.Section ?? "root"}.{field.Key}";
        return string.IsNullOrEmpty(field.Hint) ? tooltip : tooltip + "\n" + field.Hint;
    }

    private static Button OpenSourceButton(EditorFieldRowSpec spec)
    {
        Texture2D icon = ContentEditorTheme.IconAny("ExternalLink", "ActionCopy", "Forward");
        var button = new Button
        {
            Text = icon == null ? "↗" : "",
            Icon = icon,
            Flat = true,
            CustomMinimumSize = new Vector2(24, 24),
            TooltipText = spec.OpenSourceTooltip,
        };
        button.Pressed += () => EditorControls.Run("open source file", spec.OpenSource);
        return button;
    }
}
