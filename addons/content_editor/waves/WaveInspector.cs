using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Правка выбранной волны: корневые ключи, секция <c>[spawn]</c> и блоки
/// <c>[[unit_list]]</c>.
///
/// ПОЧЕМУ ЭТО НЕ ФОРМА СВОЙСТВ. Форма режима «Entities» показывает поля активной вкладки
/// каталога, а волна вкладкой каталога не является: у неё нет силуэта и наследования,
/// зато есть массив таблиц, который правится по номерам блоков. Общими остаются строка
/// поля (<see cref="EditorFieldRow"/>) и разбор ввода (<see cref="EditorFieldEditor"/>),
/// поэтому расхождения в поведении полей между режимами возникнуть не может.
/// </summary>
[Tool]
public partial class WaveInspector : VBoxContainer
{
    private ContentEditorStore _store;
    private ContentEditorIconCache _icons;

    /// <summary>Раскрытые секции. Умолчание раскрывает обе: их всего две.</summary>
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal) { "", "spawn" };

    /// <summary>Открыть вид противника во вкладках режима «Entities».</summary>
    public event Action<string> EntityOpenRequested;

    /// <summary>Состав раскрытых секций изменился и подлежит записи в снимок.</summary>
    public event Action UiStateChanged;

    /// <summary>Раскрытые секции для снимка рабочего пространства.</summary>
    public string[] ExpandedSections
    {
        get => _expanded.ToArray();
        set
        {
            _expanded.Clear();
            foreach (string section in value ?? Array.Empty<string>())
                _expanded.Add(section);
        }
    }

    public void Bind(ContentEditorStore store, ContentEditorIconCache icons)
    {
        _store = store;
        _icons = icons;
        AddThemeConstantOverride("separation", 5);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
    }

    /// <summary>Перестроить содержимое под выбранную волну либо показать пояснение.</summary>
    public void Show(OpenEntitySession session, WaveOverview overview)
    {
        EditorControls.ClearChildren(this);

        if (_store == null || session == null || overview == null)
        {
            AddChild(new Label
            {
                Text = "Select a wave on the chart to edit it",
                Modulate = new Color(1f, 1f, 1f, 0.6f),
            });
            return;
        }

        if (!overview.Compiled)
        {
            AddChild(new Label
            {
                Text = "This draft does not pass content checks; it cannot be applied yet. "
                       + "See the Godot output for the reason.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                Modulate = new Color(1f, 0.75f, 0.5f),
            });
        }

        var sections = ContentSchema.WaveFields
            .Where(field => field.Key != "id")
            .GroupBy(field => field.RootOnly ? "" : field.Section ?? "");

        foreach (var section in sections)
        {
            string key = section.Key;
            bool expanded = _expanded.Contains(key);
            AddChild(EditorControls.SectionHeading(
                string.IsNullOrEmpty(key) ? "Wave" : "Spawn shape",
                expanded,
                () => ToggleSection(key, session, overview)));

            if (!expanded)
                continue;

            var body = new VBoxContainer();
            body.AddThemeConstantOverride("separation", 4);
            foreach (var field in section)
                body.AddChild(BuildFieldRow(session, field));
            AddChild(body);
        }

        BuildUnitLists(session);
    }

    private void ToggleSection(string key, OpenEntitySession session, WaveOverview overview)
    {
        if (!_expanded.Add(key))
            _expanded.Remove(key);

        Show(session, overview);
        UiStateChanged?.Invoke();
    }

    /// <summary>Строка корневого ключа либо ключа секции <c>[spawn]</c>.</summary>
    private Control BuildFieldRow(OpenEntitySession session, ContentFieldSpec field)
    {
        bool local = _store.HasLocalValue(session, field);
        return EditorFieldRow.Build(new EditorFieldRowSpec
        {
            Field = field,
            Value = _store.EffectiveValue(session, field),
            SourceLabel = local ? "in this file" : "default",
            SourceTooltip = local
                ? "The key is written in this wave file"
                : "The key is absent; the wave subsystem default applies",
            SourceColor = local
                ? new Color(0.55f, 0.9f, 0.65f)
                : new Color(1f, 1f, 1f, 0.5f),
            CanReset = local,
            ResetTooltip = $"Remove {field.Key} from the file and fall back to the default",
            ActionsWidth = 96f,
            Commit = value => _store.SetField(session, field, value, clear: false),
            Reset = () => _store.SetField(session, field, null, clear: true),
        });
    }

    // ── Списки видов ──────────────────────────────────────────────────────────────

    private void BuildUnitLists(OpenEntitySession session)
    {
        var heading = new Label { Text = "Unit lists" };
        heading.AddThemeColorOverride("font_color", new Color(0.72f, 0.84f, 1f));
        AddChild(heading);
        AddChild(EditorControls.Hint(
            "allow narrows what is admissible, deny subtracts from it, "
            + "limit only assigns a share of the budget."));

        var available = _store.EnemyUnitIds();
        foreach (var list in _store.WaveLists(session))
            AddChild(BuildUnitList(session, list, available));

        var add = new Button
        {
            Text = "Add unit list",
            Icon = ContentEditorTheme.IconAny("Add", "Plus"),
            Alignment = HorizontalAlignment.Left,
            TooltipText = "Append a new [[unit_list]] block to the file",
        };
        add.Pressed += () => EditorControls.Run("add unit list", () =>
            _store.AddArrayItem(session, WaveDraftReader.UnitListArray, new[]
            {
                ("mode", TomlPatchWriter.FormatEnum(nameof(UnitListMode.Allow))),
                ("units", "[]"),
            }));
        AddChild(add);
    }

    private Control BuildUnitList(
        OpenEntitySession session, WaveUnitListView list, IReadOnlyList<string> available)
    {
        var panel = new EditorPanel($"[[unit_list]] {list.Index + 1} · {list.Mode}")
        {
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
        };

        int index = list.Index;
        var remove = new Button
        {
            Icon = ContentEditorTheme.IconAny("Remove", "Close"),
            Flat = true,
            TooltipText = "Delete this list from the file",
            CustomMinimumSize = new Vector2(24, 24),
        };
        if (remove.Icon == null)
            remove.Text = "×";
        remove.Pressed += () => EditorControls.Run("remove unit list", () =>
            _store.RemoveArrayItem(session, WaveDraftReader.UnitListArray, index));
        panel.Actions.AddChild(remove);

        foreach (var field in ContentSchema.WaveUnitListFields)
        {
            panel.AddContent(field.Key == "units"
                ? BuildUnitPicker(session, list, available)
                : BuildArrayFieldRow(session, index, field));
        }

        return panel;
    }

    /// <summary>
    /// Состав списка правится выбором из перечня видов противника, а не набором текста:
    /// опечатка в идентификаторе при этом невозможна.
    /// </summary>
    private Control BuildUnitPicker(
        OpenEntitySession session, WaveUnitListView list, IReadOnlyList<string> available)
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 2);

        var spec = ContentSchema.WaveUnitListFields.First(field => field.Key == "units");
        int index = list.Index;

        var caption = new HBoxContainer();
        caption.AddChild(new Label { Text = "Units", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        caption.AddChild(EditorControls.Reset(
            _store.HasArrayItemValue(session, WaveDraftReader.UnitListArray, index, spec),
            "Remove the units key from this list",
            () => _store.SetArrayItemField(
                session, WaveDraftReader.UnitListArray, index, spec, null, clear: true)));
        box.AddChild(caption);

        var picker = new EditorIdListField();
        picker.Configure(
            list.UnitIds,
            available,
            id => _store.UnitDisplayName(id),
            id => _icons?.GetUnit(_store.PreviewUnit(id)),
            "Add unit…",
            "no units: the list does nothing");
        picker.Changed += values => EditorControls.Run("edit unit list", () =>
            _store.SetArrayItemField(
                session, WaveDraftReader.UnitListArray, index, spec, values, clear: false));
        box.AddChild(picker);

        var open = new Button
        {
            Text = "Open selected units in Entities",
            Flat = true,
            Alignment = HorizontalAlignment.Left,
            Icon = ContentEditorTheme.IconAny("ExternalLink", "Forward"),
        };
        ContentEditorTheme.SetAction(open, list.UnitIds.Length > 0,
            "Open every unit of this list in the entities tab",
            "The list is empty");
        open.Pressed += () => EditorControls.Run("open units", () =>
        {
            foreach (string id in list.UnitIds)
                EntityOpenRequested?.Invoke(id);
        });
        box.AddChild(open);
        return box;
    }

    private Control BuildArrayFieldRow(
        OpenEntitySession session, int index, ContentFieldSpec field)
    {
        bool local = _store.HasArrayItemValue(session, WaveDraftReader.UnitListArray, index, field);
        return EditorFieldRow.Build(new EditorFieldRowSpec
        {
            Field = field,
            Value = _store.ArrayItemValue(session, WaveDraftReader.UnitListArray, index, field),
            CanReset = local,
            ResetTooltip = $"Remove {field.Key} from this list",
            ActionsWidth = 32f,
            Commit = value => _store.SetArrayItemField(
                session, WaveDraftReader.UnitListArray, index, field, value, clear: false),
            Reset = () => _store.SetArrayItemField(
                session, WaveDraftReader.UnitListArray, index, field, null, clear: true),
        });
    }
}
