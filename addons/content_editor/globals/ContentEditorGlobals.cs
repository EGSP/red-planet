using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Режим «Globals»: ключевые параметры, действующие на партию целиком.
///
/// ЗАЧЕМ ОТДЕЛЬНАЯ СТРАНИЦА. Таблица баланса отвечает на вопрос, чем сущности отличаются
/// друг от друга. Здесь собраны числа, у которых нет владельца среди сущностей: подсчёт
/// террора, перевод очков в мощь противника и темп волн. Они лежат ресурсами <c>.tres</c>
/// и правились поодиночке через дерево файлов, отчего цепочка «террор — давление — волна»
/// не читалась целиком.
///
/// ЧТО СЮДА ВХОДИТ. Только определяющее появление противника; состав объявлен
/// в <see cref="TuningCatalog"/>. Прочие настройки проекта — камера, туман, графика —
/// к балансу отношения не имеют и остаются в инспекторе.
///
/// РАСКЛАДКА. Плитки постоянной ширины в две колонки; на узком окне колонка остаётся одна.
/// Плитку можно свернуть до заголовка, и это состояние запоминается.
/// </summary>
[Tool]
public partial class ContentEditorGlobals : ScrollContainer, IContentEditorMode
{
    /// <summary>Ширина, ниже которой две колонки перестают помещаться.</summary>
    private const float TwoColumnWidth = TuningTile.TileWidth * 2 + 48;

    private readonly List<TuningTile> _tiles = new();
    private GridContainer _grid;
    private bool _built;

    /// <summary>Восстановление снимка не должно порождать запрос на его запись.</summary>
    private bool _restoring;

    public string ModeTitle => "Globals";

    public event Action UiStateChanged;
    public event Action<string> StatusReported;

    // Ресурсы настройки сущностями каталога не являются, и открывать здесь нечего;
    // событие договора остаётся неиспользованным намеренно.
#pragma warning disable CS0067
    public event Action<string> EntityOpenRequested;
#pragma warning restore CS0067

    /// <summary>
    /// Страница не зависит от Store: настроечные ресурсы читаются движком, а не сборкой
    /// содержимого. Связывание нужно лишь ради общего договора режимов.
    /// </summary>
    public void Bind(ContentEditorStore store, ContentEditorIconCache icons) => EnsureUi();

    public void Refresh()
    {
        EnsureUi();

        // Ресурс мог быть изменён извне — инспектором Godot либо правкой файла. Плитка
        // без несохранённых правок перечитывается молча; плитка с правками их сохраняет,
        // поскольку иначе обновление соседнего режима уничтожало бы работу.
        foreach (var tile in _tiles)
            EditorControls.Run($"refresh {tile.Id}", tile.Refresh);
    }

    public void CaptureInto(ContentEditorWorkspace workspace)
    {
        if (!_built || workspace == null)
            return;

        workspace.Globals.Collapsed = _tiles
            .Where(tile => tile.Collapsed)
            .Select(tile => tile.Id)
            .ToArray();

        workspace.Globals.Drafts = _tiles
            .Select(tile => new ContentEditorTuningDraftState
            {
                Id = tile.Id,
                Edits = tile.CaptureEdits(),
            })
            .Where(draft => draft.Edits.Length > 0)
            .ToList();
    }

    public void RestoreFrom(ContentEditorWorkspace workspace)
    {
        if (workspace?.Globals == null)
            return;

        EnsureUi();
        _restoring = true;
        try
        {
            var collapsed = (workspace.Globals.Collapsed ?? Array.Empty<string>())
                .ToHashSet(StringComparer.Ordinal);

            foreach (var tile in _tiles)
            {
                tile.Collapsed = collapsed.Contains(tile.Id);
                var draft = workspace.Globals.Drafts?
                    .FirstOrDefault(candidate => candidate.Id == tile.Id);
                if (draft != null)
                    tile.RestoreEdits(draft.Edits);
            }
        }
        finally
        {
            _restoring = false;
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            UpdateColumns();
    }

    private void EnsureUi()
    {
        if (_built)
            return;

        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        HorizontalScrollMode = ScrollMode.Disabled;

        var margin = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        AddChild(margin);

        _grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _grid.AddThemeConstantOverride("h_separation", 8);
        _grid.AddThemeConstantOverride("v_separation", 8);
        margin.AddChild(_grid);

        foreach (var spec in TuningCatalog.All)
        {
            var tile = new TuningTile(new TuningResourceDraft(spec));
            tile.StateChanged += () =>
            {
                if (!_restoring)
                    UiStateChanged?.Invoke();
            };
            tile.StatusReported += text => StatusReported?.Invoke(text);
            _grid.AddChild(tile);
            _tiles.Add(tile);
        }

        _built = true;
        UpdateColumns();
    }

    /// <summary>
    /// Число колонок по ширине окна. Две колонки — то, ради чего страница так и устроена;
    /// на узком окне плитки постоянной ширины иначе обрезались бы.
    /// </summary>
    private void UpdateColumns()
    {
        if (_grid == null)
            return;

        int columns = Size.X >= TwoColumnWidth ? 2 : 1;
        if (_grid.Columns != columns)
            _grid.Columns = columns;
    }
}
