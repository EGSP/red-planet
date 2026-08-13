using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

/// <summary>
/// Форма свойств активной вкладки.
///
/// ПОЛЯ СТРОЯТСЯ ИЗ ContentSchema, а не вручную: новый ключ в схеме появляется здесь сам.
/// Значения читаются из Catalog после CompileWithOverrides (итог с учётом черновика).
/// Provenance считает Store по наличию ключа в DraftText и в цепочке base.
///
/// ПРАВКА. Любое изменение виджета сразу пишет ключ в DraftText через SetField.
/// Если новое значение совпало с унаследованным, локальный ключ удаляется — файл
/// не раздувается копиями base. Кнопка «Сбросить» делает то же явно.
///
/// _rebuildGuard защищает от рекурсии: Refresh пересоздаёт виджеты, а их сигналы
/// при выставлении ButtonPressed/Selected не должны снова вызывать SetField.
/// </summary>
[Tool]
public partial class ContentEditorForm : ScrollContainer
{
    private ContentEditorStore _store;
    private VBoxContainer _root;
    private bool _rebuildGuard;
    private readonly HashSet<string> _expandedSections = new()
    {
        "",
        "body",
        "movement",
    };

    public event Action<string> VarsPanelRequested;
    public event Action GraphPanelRequested;

    public void Bind(ContentEditorStore store)
    {
        _store = store;
        EnsureRoot();
        Refresh();
    }

    public void Refresh()
    {
        if (_store == null)
            return;

        EnsureRoot();
        ClearChildren(_root);

        var session = _store?.ActiveSession;
        if (session == null)
        {
            _root.AddChild(new Label { Text = "Откройте сущность из каталога" });
            return;
        }

        var title = new Label
        {
            Text = $"{session.FileName}  ({session.DisplayName})",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        title.AddThemeFontSizeOverride("font_size", 16);
        _root.AddChild(title);

        var path = new Label
        {
            Text = session.Path,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(1f, 1f, 1f, 0.55f),
        };
        _root.AddChild(path);

        var contextActions = new HBoxContainer();
        var showToggle = new CheckBox
        {
            Text = "Показывать на поле",
            ButtonPressed = session.ShowOnField,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        showToggle.Toggled += on =>
        {
            session.ShowOnField = on;
            _store.NotifyChanged();
        };
        contextActions.AddChild(showToggle);

        if (session.Kind is ContentEntityKind.Unit or ContentEntityKind.Building)
        {
            var graph = new Button
            {
                Text = "Связи",
                TooltipText = "Показать наследование, файлы переменных и инструменты",
            };
            graph.Pressed += () => GraphPanelRequested?.Invoke();
            contextActions.AddChild(graph);
        }
        _root.AddChild(contextActions);

        var fields = ContentSchema.FieldsFor(session.Kind);

        UnitDefinition unit = session.Kind is ContentEntityKind.Unit or ContentEntityKind.Building
            ? _store.PreviewUnit(session.Id)
            : null;

        var visibleFields = fields
            .Where(field => field.Key is not ("id" or "kind"))
            .Where(field => unit == null || field.VisibleForUnit == null || field.VisibleForUnit(unit))
            .GroupBy(field => field.RootOnly ? "" : field.Section ?? "");

        foreach (var section in visibleFields)
        {
            string sectionKey = section.Key;
            bool expanded = _expandedSections.Contains(sectionKey);
            var heading = new Button
            {
                Text = $"{(expanded ? "▼" : "▶")}  " +
                       (string.IsNullOrEmpty(sectionKey) ? "Основные" : SectionTitle(sectionKey)),
                Flat = true,
                Alignment = HorizontalAlignment.Left,
                TooltipText = expanded ? "Свернуть раздел" : "Развернуть раздел",
            };
            heading.AddThemeColorOverride("font_color", new Color(0.72f, 0.84f, 1f));
            heading.Pressed += () =>
            {
                if (!_expandedSections.Add(sectionKey))
                    _expandedSections.Remove(sectionKey);
                Refresh();
            };
            _root.AddChild(heading);

            if (!expanded)
                continue;

            var sectionBody = new VBoxContainer();
            sectionBody.AddThemeConstantOverride("separation", 8);
            foreach (var field in section)
                sectionBody.AddChild(BuildRow(session, field));
            _root.AddChild(sectionBody);
        }

        if (session.Kind is ContentEntityKind.Unit or ContentEntityKind.Building)
            AddLinkedTools(session, unit);
    }

    private void AddLinkedTools(OpenEntitySession session, UnitDefinition unit)
    {
        if (unit?.ToolIds == null || unit.ToolIds.Length == 0)
            return;

        _root.AddChild(new Label { Text = "Связанные инструменты" });
        foreach (string toolId in unit.ToolIds)
        {
            var button = new Button { Text = $"Открыть {toolId}" };
            string id = toolId;
            button.Pressed += () => _store.Open(id);
            _root.AddChild(button);
        }
    }

    private Control BuildRow(OpenEntitySession session, ContentFieldSpec field)
    {
        var sourceInfo = _store.FieldSource(session, field);
        var provenance = sourceInfo.Provenance;
        bool local = _store.HasLocalValue(session, field);
        var sourceAndActions = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(176, 0),
        };

        var source = new Label
        {
            Text = ProvenanceLabel(sourceInfo),
            TooltipText = ProvenanceTooltip(sourceInfo),
            Modulate = ProvenanceColor(provenance),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        source.AddThemeFontSizeOverride("font_size", 11);
        sourceAndActions.AddChild(source);

        // Поле всегда редактируемо: первое изменение унаследованного значения создаёт
        // локальный override. Отдельная широкая кнопка «Переопределить» занимала почти
        // половину узкой формы и не добавляла действия.
        if (local)
        {
            var reset = new Button
            {
                Text = "↶",
                Flat = true,
                TooltipText = "Удалить локальное значение и вернуть наследование",
            };
            reset.Pressed += () =>
            {
                _store.SetField(session, field, null, clear: true);
            };
            sourceAndActions.AddChild(reset);
        }
        if ((provenance is FieldProvenance.Base or FieldProvenance.Vars)
            && !string.IsNullOrEmpty(sourceInfo.Path))
        {
            string sourcePath = sourceInfo.Path;
            var openSource = new Button
            {
                Text = "↗",
                Flat = true,
                CustomMinimumSize = new Vector2(24, 24),
                TooltipText = $"Открыть исходный файл\n{sourcePath}",
            };
            openSource.Pressed += () => OpenSource(sourcePath);
            sourceAndActions.AddChild(openSource);
        }

        object value = _store.EffectiveValue(session, field);
        Control editor = BuildEditor(session, field, value, editable: true);
        var row = new EditorPropertyRow();
        row.Configure(
            field.Label,
            $"{field.Section ?? "корень"}.{field.Key}",
            editor,
            sourceAndActions,
            EditorBelowTitle(field));
        return row;
    }

    private void OpenSource(string path)
    {
        // Определения с id открываются второй вкладкой редактора контента. Vars-файлы
        // не являются сущностями каталога, поэтому для них используется системный
        // редактор, связанный с расширением TOML.
        if (TomlResolver.IsVars(path)
            && (_store.ActiveSession?.Kind is ContentEntityKind.Unit or ContentEntityKind.Building))
        {
            VarsPanelRequested?.Invoke(path);
            return;
        }

        if (_store.OpenPath(path))
            return;

        OS.ShellOpen(ProjectSettings.GlobalizePath(path));
    }

    private static bool EditorBelowTitle(ContentFieldSpec field) =>
        field.Type is ContentFieldType.StringList or ContentFieldType.Vector2List;

    private Control BuildEditor(
        OpenEntitySession session, ContentFieldSpec field, object value, bool editable)
    {
        switch (field.Type)
        {
            case ContentFieldType.Bool:
            {
                var box = new CheckBox
                {
                    ButtonPressed = value is true,
                    Disabled = !editable,
                    Text = "",
                };
                box.Toggled += on => Commit(session, field, on);
                return box;
            }

            case ContentFieldType.Enum when field.EnumType != null:
            {
                var option = new OptionButton { Disabled = !editable };
                string current = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                int selected = 0;
                int i = 0;
                foreach (string name in Enum.GetNames(field.EnumType))
                {
                    option.AddItem(EnumLabel(field.EnumType, name));
                    option.SetItemMetadata(i, name);
                    if (string.Equals(name, current, StringComparison.OrdinalIgnoreCase))
                        selected = i;
                    i++;
                }

                option.Selected = selected;
                option.ItemSelected += index =>
                {
                    string name = option.GetItemMetadata((int)index).AsString();
                    Commit(session, field, name);
                };
                return option;
            }

            case ContentFieldType.Color:
            {
                var picker = new ColorPickerButton
                {
                    Color = value is Color color ? color : Colors.White,
                    Disabled = !editable,
                    CustomMinimumSize = new Vector2(80, 28),
                };
                Color pending = picker.Color;
                picker.ColorChanged += color => pending = color;
                // Во время перетаскивания маркера ColorChanged приходит каждый пиксель.
                // Сборка каталога и пересоздание формы выполняются один раз при закрытии.
                picker.PopupClosed += () => Commit(session, field, pending);
                return picker;
            }

            case ContentFieldType.StringList:
            {
                var edit = new LineEdit
                {
                    Text = value is IEnumerable<string> list
                        ? string.Join(", ", list)
                        : "",
                    Editable = editable,
                };
                edit.TextSubmitted += text =>
                {
                    var items = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    Commit(session, field, items.ToList());
                };
                edit.FocusExited += () =>
                {
                    if (!editable)
                        return;
                    var items = edit.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    Commit(session, field, items.ToList());
                };
                return edit;
            }

            case ContentFieldType.Vector2List:
            {
                var edit = new LineEdit
                {
                    Text = FormatValue(value),
                    PlaceholderText = "1,0; 0,1; -1,0",
                    TooltipText = "Пары X,Y разделяются точкой с запятой",
                    Editable = editable,
                };
                edit.TextSubmitted += text => CommitVectors(session, field, text);
                edit.FocusExited += () =>
                {
                    if (editable)
                        CommitVectors(session, field, edit.Text);
                };
                return edit;
            }

            default:
            {
                var edit = new LineEdit
                {
                    Text = FormatValue(value),
                    Editable = editable,
                };
                edit.TextSubmitted += text => CommitText(session, field, text);
                edit.FocusExited += () =>
                {
                    if (editable)
                        CommitText(session, field, edit.Text);
                };
                return edit;
            }
        }
    }

    private void CommitText(OpenEntitySession session, ContentFieldSpec field, string text)
    {
        object parsed = field.Type switch
        {
            ContentFieldType.Float or ContentFieldType.NullableFloat or ContentFieldType.Scale =>
                float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f :
                float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out f) ? f : null,
            ContentFieldType.Int =>
                int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : null,
            _ => text,
        };

        if (parsed != null)
            Commit(session, field, parsed);
    }

    private void CommitVectors(OpenEntitySession session, ContentFieldSpec field, string text)
    {
        var vectors = new List<Vector2>();
        foreach (string pair in text.Split(
                     ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] coordinates = pair.Split(
                ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (coordinates.Length != 2
                || !float.TryParse(
                    coordinates[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(
                    coordinates[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                return;
            }

            vectors.Add(new Vector2(x, y));
        }

        Commit(session, field, vectors);
    }

    private void Commit(OpenEntitySession session, ContentFieldSpec field, object value)
    {
        if (_rebuildGuard)
            return;

        _rebuildGuard = true;
        _store.SetField(session, field, value, clear: false);
        _rebuildGuard = false;
    }

    private void EnsureRoot()
    {
        if (_root != null)
            return;

        _root = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _root.AddThemeConstantOverride("separation", 6);
        AddChild(_root);
    }

    private static void ClearChildren(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            // RemoveChild исключает старый Control из раскладки немедленно. Один QueueFree
            // оставлял старую и новую формы одновременно до конца кадра.
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static string FormatValue(object value) => value switch
    {
        null => "",
        float f => f.ToString("0.###", CultureInfo.InvariantCulture),
        double d => d.ToString("0.###", CultureInfo.InvariantCulture),
        Color c => $"{c.R:0.##}, {c.G:0.##}, {c.B:0.##}",
        IEnumerable<string> list => string.Join(", ", list),
        IEnumerable<float> floats => string.Join(", ", floats.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture))),
        Vector2[] vectors => string.Join("; ", vectors.Select(v => $"{v.X:0.##},{v.Y:0.##}")),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    private static string ProvenanceLabel(FieldSourceInfo source) => source.Provenance switch
    {
        FieldProvenance.Local => "в текущем файле",
        FieldProvenance.Base => $"унаследовано · {SourceFileName(source.Path)}",
        FieldProvenance.Vars => $"из переменных · {SourceFileName(source.Path)}",
        FieldProvenance.Default => "по умолчанию",
        _ => "нет",
    };

    private static string SourceFileName(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "неизвестный файл";
        int slash = path.LastIndexOf('/');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    private static string ProvenanceTooltip(FieldSourceInfo source) => source.Provenance switch
    {
        FieldProvenance.Local => "Значение записано в текущем файле",
        FieldProvenance.Base =>
            $"Значение задано в родительском определении\n{source.Path}",
        FieldProvenance.Vars =>
            $"Значение задано в файле общих переменных\n{source.Path}",
        FieldProvenance.Default => "Значение задано в коде как умолчание",
        _ => "Значение отсутствует",
    };

    private static Color ProvenanceColor(FieldProvenance provenance) => provenance switch
    {
        FieldProvenance.Local => new Color(0.55f, 0.9f, 0.65f),
        FieldProvenance.Base => new Color(0.65f, 0.75f, 0.95f),
        FieldProvenance.Vars => new Color(0.85f, 0.7f, 1f),
        _ => new Color(1f, 1f, 1f, 0.5f),
    };

    private static string EnumLabel(Type type, string name)
    {
        if (type == typeof(UnitClass))
            return name switch
            {
                nameof(UnitClass.Bot) => "Бот",
                nameof(UnitClass.Vehicle) => "Машина",
                nameof(UnitClass.Commander) => "Коммандер",
                nameof(UnitClass.Structure) => "Постройка",
                nameof(UnitClass.Factory) => "Переработчик",
                nameof(UnitClass.Turret) => "Турель",
                nameof(UnitClass.Assembler) => "Сборщик",
                nameof(UnitClass.Enemy) => "Противник",
                nameof(UnitClass.Plant) => "Завод",
                _ => name,
            };

        if (type == typeof(HullShape))
            return name switch
            {
                nameof(HullShape.Circle) => "Круг",
                nameof(HullShape.Rect) => "Прямоугольник",
                nameof(HullShape.Hex) => "Шестиугольник",
                nameof(HullShape.Arrow) => "Стрела",
                nameof(HullShape.Crescent) => "Подкова",
                nameof(HullShape.Star) => "Звезда",
                nameof(HullShape.Fortress) => "Крепость",
                nameof(HullShape.Sickle) => "Серп",
                nameof(HullShape.Crown) => "Венец",
                _ => name,
            };

        if (type == typeof(HullTrim))
            return name == nameof(HullTrim.None) ? "Нет" : "Плечи";

        if (type == typeof(BuildPattern))
            return name switch
            {
                nameof(BuildPattern.None) => "Нет",
                nameof(BuildPattern.Line) => "Линия",
                nameof(BuildPattern.Square) => "Квадрат",
                nameof(BuildPattern.Diamond) => "Ромб",
                nameof(BuildPattern.MetalArea) => "Точки металла",
                _ => name,
            };

        return name;
    }

    private static string SectionTitle(string section) => section switch
    {
        "body" => "Корпус",
        "movement" => "Движение",
        "footprint" => "Занимаемые клетки",
        "assembly" => "Сборка",
        "conversion" => "Переработка",
        "production" => "Производство",
        "storage" => "Хранение",
        "terror" => "Террор",
        "battle" => "Бой",
        "plant" => "Завод",
        "orders" => "Приказы",
        _ => section,
    };
}
