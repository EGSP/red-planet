using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Состояние главного экрана. Публичные входы сначала переводят объект в Ready;
/// прямой доступ к виджетам в Constructed и Disposed запрещён.
/// </summary>
public enum ContentEditorLifecycle
{
    Constructed,
    UiReady,
    Loading,
    Ready,
    Faulted,
    Disposed,
}

/// <summary>
/// Главный экран редактора контента.
///
/// ЧТО ЗДЕСЬ ОСТАЁТСЯ. Жизненный цикл (создание интерфейса, завершение), верхняя полоса
/// действий над всем проектом, переключение режимов, сверка с диском и снимок рабочего
/// пространства. Содержимое режимов вынесено в отдельные классы, реализующие
/// <see cref="IContentEditorMode"/>: главный экран одинаково связывает их со Store,
/// обновляет и сохраняет их настройку.
///
/// СОСТОЯНИЕ. Единственный источник правды — <see cref="ContentEditorStore"/>. Интерфейс
/// подписан на его <c>Changed</c> и перерисовывается целиком отложенным вызовом.
///
/// ВНЕШНИЕ ПРАВКИ. Сверка вызывается при показе вкладки, при возврате фокуса приложения
/// и по сигналу EditorFileSystem (с паузой). Чистые вкладки подтягиваются молча; вкладки
/// с черновиком открывают диалог конфликта.
/// </summary>
[Tool]
public partial class ContentEditorMain : Control
{
    public const string EditorNodeName = "RedPlanetContentEditor";

    /// <summary>Пауза перед сверкой после FilesystemChanged: импорт шлёт несколько событий подряд.</summary>
    private const double ExternalCheckDelaySec = 0.35;

    /// <summary>
    /// Пауза перед записью снимка. Правка в форме порождает событие на каждый набранный
    /// символ, и запись файла на каждое из них означала бы десятки обращений к диску
    /// в секунду. Отсрочка объединяет их в одну запись.
    /// </summary>
    private const double WorkspaceSaveDelaySec = 0.6;

    private ContentEditorStore _store = new();

    private TabContainer _modeTabs;
    private ContentEditorEntitiesMode _entities;
    private ContentEditorBalance _balance;
    private ContentEditorWaves _waves;
    private ContentEditorGlobals _globals;
    private readonly List<IContentEditorMode> _modes = new();
    private ContentEditorIconCache _icons;

    private Label _status;
    private Button _reloadAllButton;
    private Button _floatingModeButton;
    private ConfirmationDialog _conflictDialog;

    /// <summary>Файлы, изменённые снаружи при несохранённом черновике, ждут решения по очереди.</summary>
    private readonly Queue<string> _pendingConflicts = new();

    private readonly HashSet<string> _pendingConflictIds = new(StringComparer.Ordinal);

    private bool _uiBuilt;
    private bool _loaded;
    private bool _storeRefreshQueued;
    private int _uiGeneration;
    private double _externalCheckIn;
    private bool _externalCheckQueued;
    private double _workspaceSaveIn;
    private bool _workspaceSaveQueued;
    private ContentEditorLifecycle _lifecycle = ContentEditorLifecycle.Constructed;
    private bool _reparenting;

    public ContentEditorLifecycle Lifecycle => _lifecycle;

    /// <summary>Готов ли экран к обращению к виджетам.</summary>
    public bool IsOperational => _lifecycle == ContentEditorLifecycle.Ready && UiReferencesAreValid();

    /// <summary>Плагин переносит этот Control; сам экран не должен владеть Window.</summary>
    public event Action FloatingModeToggleRequested;

    /// <summary>Согласовать надпись и иконку переключателя отдельного окна с его состоянием.</summary>
    public void SetFloatingMode(bool floating)
    {
        if (!IsInstanceValid(_floatingModeButton))
            return;

        _floatingModeButton.Text = floating ? "Dock in editor" : "Open fullscreen";
        _floatingModeButton.TooltipText = floating
            ? "Close the separate window and return the editor to the Content tab"
            : "Move the content editor into a separate fullscreen window";
        _floatingModeButton.Icon = floating
            ? ContentEditorTheme.IconAny("Panels", "GuiTabMenuHl", "Window")
            : ContentEditorTheme.IconAny("MakeFloating", "DistractionFree", "Window");
    }

    public override void _Ready() => EnsureOperational();

    /// <summary>
    /// Перевести экран в полноценное состояние. Метод можно вызывать из _Ready,
    /// _MakeVisible и уведомления фокуса: повторный вызов безопасен.
    /// </summary>
    public bool EnsureOperational()
    {
        if (_lifecycle == ContentEditorLifecycle.Disposed || !IsInstanceValid(this))
            return false;

        if (!EnsureUiBuilt())
            return false;

        if (_lifecycle == ContentEditorLifecycle.Loading)
            return false;

        if (_lifecycle == ContentEditorLifecycle.Ready)
            return true;

        _lifecycle = ContentEditorLifecycle.Loading;

        try
        {
            // Снимок читается до ReloadFromDisk: его Changed может сразу записать текущее
            // состояние поверх сохранённого.
            ContentEditorWorkspace workspace = ContentEditorWorkspaceFile.Load();

            // Связывание до чтения файлов: Changed может прийти уже во время загрузки.
            foreach (var mode in _modes)
                mode.Bind(_store, _icons);

            _store.ReloadFromDisk();
            if (workspace != null)
            {
                _store.RestoreWorkspace(workspace);
                RestoreUi(workspace);
            }

            RefreshModes();
            _loaded = true;
            _lifecycle = ContentEditorLifecycle.Ready;
            SetStatus("catalog loaded");
            return true;
        }
        catch (Exception ex)
        {
            _loaded = false;
            _lifecycle = ContentEditorLifecycle.Faulted;
            GD.PushError($"[Content editor] initialization failed: {ex}");
            if (IsInstanceValid(_status))
                _status.Text = "initialization failed; reopen the tab";
            return false;
        }
    }

    public override void _ExitTree()
    {
        if (!_reparenting)
            Shutdown();
    }

    /// <summary>
    /// Перенести экран между EditorMainScreen и Window. Обычный _ExitTree означает выгрузку
    /// плагина, однако при Reparent кратковременный выход из дерева не должен переводить
    /// Store в Disposed.
    /// </summary>
    public void ReparentWithoutShutdown(Node newParent)
    {
        if (!IsInstanceValid(newParent) || !EnsureOperational())
            return;

        _reparenting = true;
        try
        {
            Reparent(newParent, keepGlobalTransform: false);
        }
        finally
        {
            _reparenting = false;
        }
    }

    /// <summary>
    /// Остановить отложенные вызовы и отсоединить Store. Может вызываться и плагином,
    /// и _ExitTree; повторный вызов ничего не делает.
    /// </summary>
    public void Shutdown()
    {
        if (_lifecycle == ContentEditorLifecycle.Disposed)
            return;

        if (_uiBuilt && _store != null)
            _store.Changed -= OnStoreChanged;

        if (IsInstanceValid(_icons))
            _icons.IconsReady -= OnIconsReady;

        // Снимок пишется только тогда, когда редактор успел загрузиться. Иначе выключение
        // плагина до первого показа вкладки записало бы поверх сохранённого состояния
        // пустой набор вкладок и умолчания режимов.
        if (_loaded)
            PersistWorkspace();

        EditorControls.FaultReporter = null;
        _externalCheckQueued = false;
        _storeRefreshQueued = false;
        _workspaceSaveQueued = false;
        SetProcess(false);
        _loaded = false;
        _lifecycle = ContentEditorLifecycle.Disposed;
    }

    public override void _Notification(int what)
    {
        // Возврат в окно Godot после правки файла во внешнем редакторе.
        if (what == NotificationApplicationFocusIn && Visible && EnsureOperational())
            CheckExternalChanges();
    }

    public override void _Process(double delta)
    {
        if (_lifecycle is ContentEditorLifecycle.Disposed or ContentEditorLifecycle.Faulted)
            return;

        if (_externalCheckQueued)
        {
            _externalCheckIn -= delta;
            if (_externalCheckIn <= 0)
            {
                _externalCheckQueued = false;
                if (Visible && EnsureOperational())
                    CheckExternalChanges();
            }
        }

        if (!_workspaceSaveQueued)
            return;

        _workspaceSaveIn -= delta;
        if (_workspaceSaveIn <= 0)
        {
            _workspaceSaveQueued = false;
            PersistWorkspace();
        }
    }

    // ── Создание интерфейса ───────────────────────────────────────────────────────

    /// <summary>
    /// EditorPlugin может получить _MakeVisible сразу после AddChild, до _Ready.
    /// </summary>
    private bool EnsureUiBuilt()
    {
        if (_lifecycle == ContentEditorLifecycle.Disposed)
            return false;

        if (_uiBuilt && UiReferencesAreValid())
            return true;

        ResetUiAndStore();

        try
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            SizeFlagsVertical = SizeFlags.ExpandFill;
            SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            BuildUi();
            _store.Changed += OnStoreChanged;
            _uiBuilt = true;
            SetProcess(true);
            _lifecycle = ContentEditorLifecycle.UiReady;
            return true;
        }
        catch (Exception ex)
        {
            _lifecycle = ContentEditorLifecycle.Faulted;
            GD.PushError($"[Content editor] UI was not created: {ex}");
            return false;
        }
    }

    private void ResetUiAndStore()
    {
        if (_loaded)
            PersistWorkspace();

        if (_uiBuilt && _store != null)
            _store.Changed -= OnStoreChanged;

        if (IsInstanceValid(_icons))
            _icons.IconsReady -= OnIconsReady;

        foreach (Node child in GetChildren())
        {
            // Дочерние элементы принадлежат только этому экрану. Снимаем их из дерева
            // сразу, чтобы новая раскладка не сосуществовала со старой до конца кадра.
            RemoveChild(child);
            child.Free();
        }

        _store = new ContentEditorStore();
        _modes.Clear();
        _modeTabs = null;
        _entities = null;
        _balance = null;
        _waves = null;
        _globals = null;
        _icons = null;
        _status = null;
        _reloadAllButton = null;
        _floatingModeButton = null;
        _conflictDialog = null;
        _pendingConflicts.Clear();
        _pendingConflictIds.Clear();
        _externalCheckQueued = false;
        _storeRefreshQueued = false;
        _workspaceSaveQueued = false;
        _loaded = false;
        _uiBuilt = false;
        _uiGeneration++;
        _lifecycle = ContentEditorLifecycle.Constructed;
    }

    private bool UiReferencesAreValid() =>
        _uiBuilt
        && IsInstanceValid(_modeTabs)
        && IsInstanceValid(_entities)
        && IsInstanceValid(_balance)
        && IsInstanceValid(_waves)
        && IsInstanceValid(_globals)
        && IsInstanceValid(_icons)
        && IsInstanceValid(_status)
        && IsInstanceValid(_floatingModeButton)
        && IsInstanceValid(_conflictDialog);

    private void BuildUi()
    {
        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        AddChild(root);
        // Preset вычисляет offsets относительно родителя, поэтому применяется после AddChild.
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        root.AddChild(BuildToolbar());

        _modeTabs = new TabContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _modeTabs.TabChanged += _ => RequestWorkspaceSave();
        root.AddChild(_modeTabs);

        // Кэш иконок должен существовать до связывания режимов: они спрашивают силуэты
        // сразу при первом обновлении.
        _icons = new ContentEditorIconCache();
        AddChild(_icons);
        _icons.IconsReady += OnIconsReady;

        _entities = new ContentEditorEntitiesMode();
        _balance = new ContentEditorBalance();
        _waves = new ContentEditorWaves();
        _globals = new ContentEditorGlobals();
        AddMode(_entities);
        AddMode(_balance);
        AddMode(_waves);
        AddMode(_globals);

        BuildConflictDialog();
        EditorControls.FaultReporter = SetStatus;
    }

    /// <summary>Поместить режим во вкладку и подписаться на его события единым образом.</summary>
    private void AddMode(IContentEditorMode mode)
    {
        var control = (Control)mode;
        _modeTabs.AddChild(control);
        _modeTabs.SetTabTitle(_modeTabs.GetTabCount() - 1, mode.ModeTitle);

        mode.UiStateChanged += RequestWorkspaceSave;
        mode.StatusReported += SetStatus;
        mode.EntityOpenRequested += OpenEntity;
        _modes.Add(mode);
    }

    /// <summary>
    /// Верхняя полоса относится ко всему проекту целиком. Действия над отдельной сущностью
    /// стоят в заголовке её панели: иначе по расположению кнопки нельзя определить, к чему
    /// она применяется.
    /// </summary>
    private Control BuildToolbar()
    {
        var toolbar = new HBoxContainer();
        toolbar.AddThemeConstantOverride("separation", 6);

        var scope = new Label
        {
            Text = "Project",
            Modulate = new Color(1f, 1f, 1f, 0.55f),
        };
        scope.AddThemeFontSizeOverride("font_size", 12);
        toolbar.AddChild(scope);

        _reloadAllButton = EditorControls.Add(toolbar, "Reload all files", "Reload", ReloadEverything,
            "Re-read every .toml of the project. Drafts of unsaved tabs are kept.");

        _status = new Label
        {
            Text = "",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        toolbar.AddChild(_status);

        _floatingModeButton = new Button
        {
            Text = "Open fullscreen",
            TooltipText = "Move the content editor into a separate fullscreen window",
            Icon = ContentEditorTheme.IconAny("MakeFloating", "DistractionFree", "Window"),
        };
        _floatingModeButton.Pressed += () => EditorControls.Run("toggle window mode", () =>
        {
            if (EnsureOperational())
                FloatingModeToggleRequested?.Invoke();
        });
        toolbar.AddChild(_floatingModeButton);
        return toolbar;
    }

    /// <summary>Диалог конфликта относится к файлам, а не к режиму, и потому живёт здесь.</summary>
    private void BuildConflictDialog()
    {
        _conflictDialog = new ConfirmationDialog
        {
            Title = "File changed externally",
            OkButtonText = "Reload from disk",
        };
        _conflictDialog.Confirmed += () => EditorControls.Run("reload conflicting file", () =>
        {
            if (_pendingConflicts.Count == 0)
                return;

            string id = DequeueConflict();
            _conflictDialog.Hide();
            _store.AcceptExternalReload(id);
            SetStatus($"reloaded from disk: {id}");
            ShowNextConflict();
        });

        // Отмена диалога равносильна выбору «оставить черновик».
        _conflictDialog.Canceled += () => EditorControls.Run("keep draft", KeepDraftForCurrentConflict);
        AddChild(_conflictDialog);

        _conflictDialog.AddButton("Keep draft", true, "keep");
        _conflictDialog.CustomAction += action =>
        {
            if (action != "keep")
                return;

            EditorControls.Run("keep draft", () =>
            {
                _conflictDialog.Hide();
                KeepDraftForCurrentConflict();
            });
        };
    }

    // ── Обновление ────────────────────────────────────────────────────────────────

    private void OnStoreChanged()
    {
        RequestWorkspaceSave();

        if (IsInstanceValid(_entities))
            _entities.HideStaleContext();

        // Во время Loading событие допустимо; отложенный вызов выполнится уже после Ready.
        // После Shutdown никакие запланированные обновления исполняться не должны.
        if (_lifecycle == ContentEditorLifecycle.Disposed || !UiReferencesAreValid())
            return;

        // Store может измениться из сигнала OptionButton или ColorPicker. Немедленное
        // уничтожение формы внутри этого сигнала повреждало состояние активного виджета.
        // Один отложенный вызов также объединяет несколько событий в пределах кадра.
        if (_storeRefreshQueued)
            return;

        _storeRefreshQueued = true;
        CallDeferred(nameof(RefreshAfterStoreChanged), _uiGeneration);
    }

    private void RefreshAfterStoreChanged(int generation)
    {
        // Обратный вызов старого поколения не должен менять флаг нового интерфейса.
        if (generation != _uiGeneration)
            return;

        _storeRefreshQueued = false;
        if (_lifecycle == ContentEditorLifecycle.Disposed
            || !IsInstanceValid(this)
            || !IsInsideTree()
            || !IsOperational)
        {
            return;
        }

        RefreshModes();
    }

    private void RefreshModes()
    {
        foreach (var mode in _modes)
            EditorControls.Run($"refresh {mode.ModeTitle}", mode.Refresh);
    }

    private void OnIconsReady()
    {
        if (!IsOperational)
            return;

        RefreshModes();
    }

    /// <summary>Перечитать все файлы проекта и обновить глобальный каталог игры.</summary>
    private void ReloadEverything()
    {
        _store.ReloadFromDisk();
        // Предпросмотры сессии читают Content.Catalog, поэтому его тоже обновляем.
        Content.Reload();

        // Чистые вкладки ReloadFromDisk не сбрасывает — подтянуть их явно.
        foreach (var session in _store.Sessions.Where(session => !session.Dirty).ToList())
            _store.AcceptExternalReload(session.Id);

        RefreshModes();
        SetStatus("all files reloaded from disk");
    }

    private void OpenEntity(string id)
    {
        if (!IsOperational || string.IsNullOrEmpty(id))
            return;

        _modeTabs.CurrentTab = 0;
        _store.Open(id);
    }

    private void SetStatus(string text)
    {
        if (IsInstanceValid(_status))
            _status.Text = text ?? "";
    }

    // ── Сверка с диском ───────────────────────────────────────────────────────────

    /// <summary>
    /// Отложенная сверка: FilesystemChanged может прийти пачкой при сохранении одного файла.
    /// </summary>
    public void RequestExternalCheck()
    {
        if (_lifecycle == ContentEditorLifecycle.Disposed)
            return;

        _externalCheckQueued = true;
        _externalCheckIn = ExternalCheckDelaySec;
    }

    /// <summary>Немедленная сверка меток времени на диске с открытыми вкладками.</summary>
    public void CheckExternalChanges()
    {
        if (!EnsureOperational())
            return;

        var report = _store.CheckExternalChanges();
        if (!report.Any)
            return;

        RefreshModes();

        if (report.Reloaded.Count > 0 || report.CatalogChanged > 0)
        {
            string parts = report.Reloaded.Count > 0
                ? $"reloaded from disk: {string.Join(", ", report.Reloaded)}"
                : "catalog updated";
            if (report.CatalogChanged > 0 && report.Reloaded.Count > 0)
                parts += "; catalog updated";
            SetStatus(parts);
        }

        foreach (string id in report.Conflicts)
        {
            // Сигналы файловой системы и возврат фокуса могут сообщить об одном файле
            // несколько раз; повторная запись в очередь дала бы второй диалог о том же.
            if (_pendingConflictIds.Add(id))
                _pendingConflicts.Enqueue(id);
        }

        ShowNextConflict();
    }

    private void ShowNextConflict()
    {
        if (!IsOperational
            || _pendingConflicts.Count == 0
            || !IsInstanceValid(_conflictDialog)
            || _conflictDialog.Visible)
        {
            return;
        }

        string id = _pendingConflicts.Peek();
        _conflictDialog.DialogText =
            $"File “{id}” changed externally, and this tab has unsaved edits.\n"
            + "“Reload from disk” discards the draft. “Keep draft” keeps the editor changes "
            + "and will overwrite the external file on Apply.";
        _conflictDialog.PopupCentered();
    }

    private void KeepDraftForCurrentConflict()
    {
        if (!IsOperational || _pendingConflicts.Count == 0)
            return;

        string id = DequeueConflict();
        _store.KeepDraftDespiteExternal(id);
        SetStatus($"draft kept: {id}");
        ShowNextConflict();
    }

    private string DequeueConflict()
    {
        string id = _pendingConflicts.Dequeue();
        _pendingConflictIds.Remove(id);
        return id;
    }

    // ── Снимок рабочего пространства ──────────────────────────────────────────────

    /// <summary>Запланировать запись снимка: сама запись произойдёт после короткой паузы.</summary>
    private void RequestWorkspaceSave()
    {
        if (_lifecycle == ContentEditorLifecycle.Disposed)
            return;

        _workspaceSaveQueued = true;
        _workspaceSaveIn = WorkspaceSaveDelaySec;
    }

    private void PersistWorkspace()
    {
        // Пока каталог не загружен, снимать нечего: запись в этот момент затёрла бы
        // сохранённое состояние пустым.
        if (_store == null || !_loaded)
            return;

        var workspace = new ContentEditorWorkspace();
        _store.CaptureInto(workspace);
        if (IsInstanceValid(_modeTabs))
            workspace.ModeTab = _modeTabs.CurrentTab;

        foreach (var mode in _modes)
            EditorControls.Run($"capture {mode.ModeTitle} state", () => mode.CaptureInto(workspace));

        ContentEditorWorkspaceFile.Save(workspace);
    }

    private void RestoreUi(ContentEditorWorkspace workspace)
    {
        foreach (var mode in _modes)
            EditorControls.Run($"restore {mode.ModeTitle} state", () => mode.RestoreFrom(workspace));

        if (IsInstanceValid(_modeTabs) && _modeTabs.GetTabCount() > 0)
            _modeTabs.CurrentTab = Mathf.Clamp(workspace.ModeTab, 0, _modeTabs.GetTabCount() - 1);
    }
}
