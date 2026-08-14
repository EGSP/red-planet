using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Правка списка идентификаторов строками, а не текстом через запятую.
///
/// ЗАЧЕМ НЕ СТРОКА ЧЕРЕЗ ЗАПЯТУЮ. Список видов волны ссылается на существующие
/// определения, и опечатка в нём становится ошибкой сборки. Выбор из готового перечня
/// опечатку исключает, а уже добавленные значения из перечня убираются, отчего повтор
/// невозможен.
/// </summary>
[Tool]
public partial class EditorIdListField : VBoxContainer
{
    private readonly List<string> _values = new();
    private IReadOnlyList<string> _available = Array.Empty<string>();
    private Func<string, string> _label;
    private Func<string, Texture2D> _icon;
    private string _addText = "Add…";
    private string _emptyText = "list is empty";

    /// <summary>Список изменился: значения переданы в порядке отображения.</summary>
    public event Action<List<string>> Changed;

    public EditorIdListField()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 2);
    }

    public void Configure(
        IEnumerable<string> values,
        IReadOnlyList<string> available,
        Func<string, string> label = null,
        Func<string, Texture2D> icon = null,
        string addText = null,
        string emptyText = null)
    {
        _values.Clear();
        if (values != null)
            _values.AddRange(values);

        _available = available ?? Array.Empty<string>();
        _label = label;
        _icon = icon;
        _addText = addText ?? _addText;
        _emptyText = emptyText ?? _emptyText;
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        if (_values.Count == 0)
        {
            AddChild(new Label
            {
                Text = _emptyText,
                Modulate = new Color(1f, 1f, 1f, 0.5f),
            });
        }

        for (int i = 0; i < _values.Count; i++)
        {
            string id = _values[i];
            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 4);

            var name = new Button
            {
                Text = _label?.Invoke(id) ?? id,
                Icon = _icon?.Invoke(id),
                Alignment = HorizontalAlignment.Left,
                Flat = true,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                TooltipText = id,
                Disabled = true,
            };
            // Кнопка используется ради выравнивания с иконкой; действие ей не назначено,
            // поэтому она сразу выключена и не обещает нажатия.
            row.AddChild(name);

            var remove = new Button
            {
                Icon = ContentEditorTheme.IconAny("Remove", "Close"),
                Flat = true,
                TooltipText = $"Remove {id} from the list",
                CustomMinimumSize = new Vector2(24, 24),
            };
            if (remove.Icon == null)
                remove.Text = "×";

            int index = i;
            remove.Pressed += () =>
            {
                _values.RemoveAt(index);
                Rebuild();
                Changed?.Invoke(_values.ToList());
            };
            row.AddChild(remove);
            AddChild(row);
        }

        var missing = _available
            .Where(id => !_values.Contains(id, StringComparer.Ordinal))
            .ToList();

        var add = new MenuButton
        {
            Text = _addText,
            Icon = ContentEditorTheme.IconAny("Add", "Plus"),
            Flat = false,
            Alignment = HorizontalAlignment.Left,
        };
        ContentEditorTheme.SetAction(add, missing.Count > 0,
            "Add an identifier to the list",
            _available.Count == 0
                ? "There is nothing to choose from"
                : "Every available identifier is already in the list");

        var popup = add.GetPopup();
        foreach (string id in missing)
        {
            popup.AddIconItem(_icon?.Invoke(id), _label?.Invoke(id) ?? id);
            popup.SetItemMetadata(popup.ItemCount - 1, id);
        }

        popup.IndexPressed += index =>
        {
            string id = popup.GetItemMetadata((int)index).AsString();
            if (string.IsNullOrEmpty(id) || _values.Contains(id, StringComparer.Ordinal))
                return;

            _values.Add(id);
            Rebuild();
            Changed?.Invoke(_values.ToList());
        };

        AddChild(add);
    }
}
