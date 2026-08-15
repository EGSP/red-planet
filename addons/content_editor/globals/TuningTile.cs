using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Плитка одного настроечного ресурса: заголовок с действиями, пояснение и перечень полей.
///
/// ПОЧЕМУ ПЛИТКА, А НЕ ВКЛАДКА НА КАЖДЫЙ РЕСУРС. Числа появления противника подбираются
/// вместе: очки террора переводятся в мощь одним коэффициентом, а мощь превращается
/// в размер волны другим. Разложенные по вкладкам, они требуют переключения ради каждого
/// сравнения; плитки рядом позволяют видеть всю цепочку разом.
///
/// ЧТО ЗДЕСЬ НЕ ПРАВИТСЯ. Кривые, вложенные ресурсы и массивы показываются сводкой
/// с переходом в инспектор Godot: редактор кривой в движке уже есть, и повторять его
/// значило бы писать то, что и так работает.
/// </summary>
[Tool]
public partial class TuningTile : EditorPanel
{
    /// <summary>Ширина плитки. Постоянная: две колонки должны стоять ровно.</summary>
    public const int TileWidth = 460;

    private readonly TuningResourceDraft _draft;
    private readonly VBoxContainer _body = new();
    private readonly ConfirmationDialog _saveDialog = new();
    private readonly LineEdit _saveName = new();

    private Button _applyButton;
    private Button _revertButton;
    private Button _saveAsButton;
    private Button _defaultButton;
    private Button _inspectButton;
    private Button _collapseButton;

    private bool _collapsed;

    /// <summary>Перестроение содержимого выполняется прямо сейчас.</summary>
    private bool _refreshing;

    /// <summary>Во время перестроения поступил запрос на ещё одно перестроение.</summary>
    private bool _refreshPending;

    /// <summary>Плитка изменилась и снимок рабочего места подлежит записи.</summary>
    public event Action StateChanged;

    /// <summary>Сообщение для общей строки состояния главного экрана.</summary>
    public event Action<string> StatusReported;

    public TuningTile(TuningResourceDraft draft) : base(draft.Spec.Title)
    {
        _draft = draft;

        CustomMinimumSize = new Vector2(TileWidth, 0);
        SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        SizeFlagsVertical = SizeFlags.ShrinkBegin;

        BuildActions();
        _body.AddThemeConstantOverride("separation", 4);
        _body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddContent(_body);

        BuildSaveDialog();
        Refresh();
    }

    /// <summary>Ключ ресурса: им плитка опознаётся в снимке рабочего места.</summary>
    public string Id => _draft.Spec.Id;

    /// <summary>Есть ли несохранённые правки плитки. От них зависит, можно ли перечитать диск.</summary>
    public bool Dirty => _draft.Dirty;

    /// <summary>Свёрнута ли плитка. Свёрнутая показывает только заголовок и действия.</summary>
    public bool Collapsed
    {
        get => _collapsed;
        set
        {
            _collapsed = value;
            Refresh();
        }
    }

    /// <summary>Несохранённые правки плитки для снимка рабочего места.</summary>
    public string[] CaptureEdits() => _draft.CaptureEdits();

    /// <summary>Восстановить правки из снимка.</summary>
    public void RestoreEdits(string[] edits)
    {
        _draft.RestoreEdits(edits);
        Refresh();
    }

    /// <summary>Перечитать ресурс с диска, отбросив правки.</summary>
    public void Reload()
    {
        _draft.Reload();
        Refresh();
    }

    // ── Раскладка ─────────────────────────────────────────────────────────────────

    private void BuildActions()
    {
        _collapseButton = EditorControls.Add(Actions, "", "GuiTreeArrowDown", () =>
        {
            Collapsed = !Collapsed;
            StateChanged?.Invoke();
        });

        _applyButton = EditorControls.Add(Actions, "Apply", "Save", () =>
        {
            string error = _draft.Apply();
            StatusReported?.Invoke(error ?? $"saved: {TomlText.FileName(_draft.Path)}");
            Refresh();
            StateChanged?.Invoke();
        });

        _revertButton = EditorControls.Add(Actions, "Revert", "Undo", () =>
        {
            _draft.Revert();
            Refresh();
            StateChanged?.Invoke();
        });

        _saveAsButton = EditorControls.Add(Actions, "Save as…", "Duplicate", () =>
        {
            _saveName.Text = SuggestName();
            _saveDialog.PopupCentered();
        });

        _defaultButton = EditorControls.Add(Actions, "", "Reload", () =>
        {
            string error = _draft.UseDefault();
            StatusReported?.Invoke(error ?? $"session uses {TomlText.FileName(_draft.Spec.DefaultPath)} again");
            Refresh();
            StateChanged?.Invoke();
        });
        if (_defaultButton.Icon == null)
            _defaultButton.Text = "Use default";

        _inspectButton = EditorControls.Add(Actions, "", "Edit", () => Inspect(_draft.Resource));
        if (_inspectButton.Icon == null)
            _inspectButton.Text = "Inspector";
    }

    private void BuildSaveDialog()
    {
        _saveDialog.Title = "Save as a variant";
        _saveDialog.OkButtonText = "Save and use";
        _saveDialog.DialogText =
            "The variant is written next to the original and assigned to the session scene. "
            + "The original file stays as it is.";

        _saveName.PlaceholderText = "variant name";
        _saveDialog.AddChild(_saveName);
        _saveDialog.Confirmed += () => EditorControls.Run("save tuning variant", () =>
        {
            string error = _draft.SaveAsVariant(_saveName.Text);
            StatusReported?.Invoke(error ?? $"session uses {TomlText.FileName(_draft.Path)}");
            Refresh();
            StateChanged?.Invoke();
        });
        AddChild(_saveDialog);
    }

    /// <summary>
    /// Перестроить содержимое под текущее состояние черновика.
    ///
    /// Удаление прежних виджетов снимает фокус с того из них, который его удерживал,
    /// а обработчик потери фокуса способен записать значение и потребовать нового
    /// перестроения. Вложенный вызов движком не выполняется, поскольку узел занят
    /// удалением потомков; поэтому запрос откладывается и исполняется по выходе
    /// из текущего перестроения.
    /// </summary>
    public void Refresh()
    {
        if (_refreshing)
        {
            _refreshPending = true;
            return;
        }

        _refreshing = true;
        try
        {
            do
            {
                _refreshPending = false;
                Rebuild();
            }
            while (_refreshPending);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Rebuild()
    {
        UpdateHeader();
        EditorControls.ClearChildren(_body);

        if (_collapsed)
            return;

        if (_draft.LoadError != null)
        {
            _body.AddChild(new Label
            {
                Text = $"The resource was not read: {_draft.LoadError}",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                Modulate = new Color(1f, 0.75f, 0.5f),
            });
            return;
        }

        _body.AddChild(EditorControls.Hint(_draft.Spec.Subtitle));
        _body.AddChild(PathLabel());

        string group = null;
        bool groupOn = true;
        foreach (var field in _draft.Fields)
        {
            if (field.Group != group)
            {
                group = field.Group;
                var toggle = SectionToggle(_draft.Fields, group);
                groupOn = toggle == null || _draft.Value(toggle).AsBool();
                if (!string.IsNullOrEmpty(group))
                    _body.AddChild(GroupHeading(group, toggle));
            }

            if (IsSectionToggle(field))
                continue;

            Control row = field.Kind == TuningFieldKind.Complex
                ? ComplexRow(field)
                : FieldRow(field);
            if (!groupOn)
                row.Modulate = new Color(1f, 1f, 1f, 0.4f);
            _body.AddChild(row);
        }
    }

    private void UpdateHeader()
    {
        bool variant = _draft.IsVariant;
        Title = _draft.Spec.Title
                + (variant ? $" · {TomlText.FileName(_draft.Path)}" : "")
                + (_draft.Dirty ? "  •  unsaved" : "");
        TitleTooltip = $"{_draft.Path}\nassigned to {_draft.Spec.NodeName} in {_draft.Spec.ScenePath}";

        _collapseButton.Icon = ContentEditorTheme.Icon(
            _collapsed ? "GuiTreeArrowRight" : "GuiTreeArrowDown");
        if (_collapseButton.Icon == null)
            _collapseButton.Text = _collapsed ? "▶" : "▼";
        _collapseButton.TooltipText = _collapsed ? "Expand the tile" : "Collapse the tile";

        ContentEditorTheme.SetAction(_applyButton, _draft.Dirty,
            $"Write the changes to {TomlText.FileName(_draft.Path)}",
            "No unsaved changes");
        ContentEditorTheme.SetAction(_revertButton, _draft.Dirty,
            "Discard the changes and show the values from disk",
            "No unsaved changes");
        ContentEditorTheme.SetAction(_saveAsButton, _draft.LoadError == null,
            "Write the current values as a separate resource and assign it to the session",
            "The resource was not read");
        ContentEditorTheme.SetAction(_defaultButton, variant,
            $"Assign {TomlText.FileName(_draft.Spec.DefaultPath)} to the session again",
            "The session already uses the original resource");
        ContentEditorTheme.SetAction(_inspectButton, _draft.Resource != null,
            "Open the resource in the Godot inspector",
            "The resource was not read");
    }

    private Control PathLabel()
    {
        var label = new Label
        {
            Text = _draft.IsVariant
                ? $"{_draft.Path} · variant assigned to the session"
                : _draft.Path,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            Modulate = _draft.IsVariant
                ? new Color(0.85f, 0.75f, 1f)
                : new Color(1f, 1f, 1f, 0.45f),
        };
        label.AddThemeFontSizeOverride("font_size", 11);
        return label;
    }

    /// <summary>
    /// Заголовок группы полей. Если у группы есть логический признак с суффиксом
    /// <c>Enabled</c>, он выносится в заголовок тумблером и отдельной строкой не
    /// показывается: выключение относится к секции целиком, а не к ещё одному числу.
    /// </summary>
    private Control GroupHeading(string title, TuningField enabledField)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };

        var heading = new Label
        {
            Text = title,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        heading.AddThemeColorOverride("font_color", new Color(0.72f, 0.84f, 1f));
        heading.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(heading);

        if (enabledField == null)
            return row;

        bool on = _draft.Value(enabledField).AsBool();
        var toggle = new CheckButton
        {
            Text = "Enabled",
            ButtonPressed = on,
            TooltipText = string.IsNullOrEmpty(enabledField.Hint)
                ? enabledField.Name
                : $"{enabledField.Name}\n{enabledField.Hint}",
        };
        if (_draft.IsEdited(enabledField))
            toggle.Modulate = new Color(1f, 0.85f, 0.5f);
        toggle.Toggled += pressed => Commit(enabledField, pressed);
        row.AddChild(toggle);
        return row;
    }

    /// <summary>
    /// Логический признак участия секции в подсчёте: имя оканчивается на <c>Enabled</c>,
    /// поле стоит внутри группы. Такие поля рисуются в заголовке, а не отдельной строкой.
    /// </summary>
    private static bool IsSectionToggle(TuningField field) =>
        field != null
        && field.Kind == TuningFieldKind.Bool
        && !string.IsNullOrEmpty(field.Group)
        && field.Name.EndsWith("Enabled", StringComparison.Ordinal);

    private static TuningField SectionToggle(IReadOnlyList<TuningField> fields, string group) =>
        string.IsNullOrEmpty(group)
            ? null
            : fields.FirstOrDefault(field => field.Group == group && IsSectionToggle(field));

    // ── Строки полей ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Обычное поле. Строка та же, что в форме свойств сущности и в инспекторе волн:
    /// разбор ввода, перенос длинного значения и вид кнопки возврата не должны
    /// различаться между страницами редактора.
    /// </summary>
    private Control FieldRow(TuningField field)
    {
        bool edited = _draft.IsEdited(field);
        var spec = new ContentFieldSpec
        {
            Key = field.Name,
            Label = field.Label,
            Type = TypeOf(field.Kind),
            Hint = field.Hint,
            RootOnly = true,
        };

        return EditorFieldRow.Build(new EditorFieldRowSpec
        {
            Field = spec,
            Value = ValueOf(field),
            SourceLabel = edited ? "edited" : "saved",
            SourceTooltip = edited
                ? "The value differs from the file and is not written yet"
                : "The value is the one stored in the resource",
            SourceColor = edited
                ? new Color(1f, 0.85f, 0.5f)
                : new Color(1f, 1f, 1f, 0.45f),
            CanReset = edited,
            ResetTooltip = "Discard the change of this field",
            ActionsWidth = 84f,
            Commit = value => Commit(field, value),
            Reset = () =>
            {
                _draft.Reset(field);
                Refresh();
                StateChanged?.Invoke();
            },
        });
    }

    /// <summary>Поле, которое правится инспектором: подпись, сводка и переход.</summary>
    private Control ComplexRow(TuningField field)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };

        var label = new Label
        {
            Text = field.Label,
            CustomMinimumSize = new Vector2(112, 0),
            TooltipText = string.IsNullOrEmpty(field.Hint) ? field.Name : $"{field.Name}\n{field.Hint}",
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.AddChild(label);

        var summary = new Label
        {
            Text = field.Summary ?? "",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Modulate = new Color(1f, 1f, 1f, 0.6f),
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.AddChild(summary);

        var open = new Button
        {
            Text = ContentEditorTheme.IconAny("Edit", "Tools") == null ? "Inspector" : "",
            Icon = ContentEditorTheme.IconAny("Edit", "Tools"),
            Flat = true,
            CustomMinimumSize = new Vector2(24, 24),
            TooltipText = "Edit this value in the Godot inspector",
        };
        var value = _draft.Value(field);
        open.Pressed += () => EditorControls.Run("open in inspector", () =>
            Inspect(value.As<Resource>() ?? _draft.Resource));
        row.AddChild(open);
        return row;
    }

    private void Commit(TuningField field, object value)
    {
        _draft.Set(field, ToVariant(field.Kind, value));
        Refresh();
        StateChanged?.Invoke();
    }

    /// <summary>Значение поля в том виде, в каком его ждут виджеты формы.</summary>
    private object ValueOf(TuningField field)
    {
        Variant value = _draft.Value(field);
        return field.Kind switch
        {
            TuningFieldKind.Float => value.AsSingle(),
            TuningFieldKind.Int => (int)value.AsInt64(),
            TuningFieldKind.Bool => value.AsBool(),
            TuningFieldKind.Color => value.AsColor(),
            _ => value.AsString(),
        };
    }

    private static Variant ToVariant(TuningFieldKind kind, object value) => kind switch
    {
        TuningFieldKind.Float => Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture),
        TuningFieldKind.Int => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture),
        TuningFieldKind.Bool => value is true,
        TuningFieldKind.Color => value is Color color ? color : Colors.White,
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "",
    };

    private static ContentFieldType TypeOf(TuningFieldKind kind) => kind switch
    {
        TuningFieldKind.Float => ContentFieldType.Float,
        TuningFieldKind.Int => ContentFieldType.Int,
        TuningFieldKind.Bool => ContentFieldType.Bool,
        TuningFieldKind.Color => ContentFieldType.Color,
        _ => ContentFieldType.String,
    };

    /// <summary>Показать ресурс в инспекторе Godot.</summary>
    private static void Inspect(Resource resource)
    {
        if (resource != null)
            EditorInterface.Singleton?.EditResource(resource);
    }

    /// <summary>Имя нового варианта: имя действующего файла с порядковым номером.</summary>
    private string SuggestName()
    {
        string basis = _draft.Spec.BaseName;
        for (int i = 2; i < 100; i++)
        {
            string candidate = $"{basis}_{i}";
            if (!_draft.VariantExists(candidate))
                return candidate;
        }

        return basis + "_variant";
    }
}
