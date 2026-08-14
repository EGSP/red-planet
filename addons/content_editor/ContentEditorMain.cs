using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

/// <summary>
/// Состояние главного экрана. Публичные входы сначала переводят объект в Ready;
/// прямой доступ к виджетам в Constructed/Stale/Disposed запрещён.
/// </summary>
public enum ContentEditorLifecycle
{
    Constructed,
    UiReady,
    Loading,
    Ready,
    Stale,
    Faulted,
    Disposed,
}

/// <summary>
/// Главный экран редактора контента.
///
/// РАСКЛАДКА. Два режима в TabContainer: «Сущности» и «Баланс». В режиме сущностей
/// слева каталог, сверху внутренние вкладки открытых файлов, по центру измерительное
/// поле, справа форма свойств. Все виджеты строятся кодом: отдельной .tscn нет, чтобы
/// плагин не зависел от ручной сборки сцены.
///
/// СОСТОЯНИЕ. Единственный источник правды — <see cref="ContentEditorStore"/>. UI только
/// подписан на Changed и перерисовывает каталог, вкладки, форму, поле и баланс.
///
/// ВНЕШНИЕ ПРАВКИ. CheckExternalChanges вызывается при показе вкладки, при возврате
/// фокуса приложения и по сигналу EditorFileSystem (через RequestExternalCheck с паузой).
/// Чистые вкладки подтягиваются молча; грязные открывают диалог конфликта.
/// </summary>
[Tool]
public partial class ContentEditorMain : Control
{
    public const string EditorNodeName = "RedPlanetContentEditor";

    /// <summary>Пауза перед сверкой после FilesystemChanged: импорт шлёт несколько событий подряд.</summary>
    private const double ExternalCheckDelaySec = 0.35;

    /// <summary>Период проверки DLL: EditorFileSystem не обязан сообщать о результате MSBuild.</summary>
    private const double BuildCheckIntervalSec = 1.0;

    private const string BuildTokenMeta = "_red_planet_content_editor_build";
    private const string WorkspaceStatePath = "res://.godot/content_editor_workspace.cfg";

    private ContentEditorStore _store = new();

    private TabContainer _modeTabs;
    private LineEdit _search;
    private TabBar _catalogTabs;
    private ItemList _catalog;
    private TabBar _entityTabs;
    private ContentEditorForm _form;
    private ContentEditorPreview _preview;
    private EditorContextTabs _contextTabs;
    private ContentEditorBalance _balance;
    private ContentEditorIconCache _icons;
    private ContentEditorWaves _waves;
    private Label _status;
    private HBoxContainer _entityActions;
    private EditorPanel _entityPanel;
    private Button _reloadAllButton;
    private Button _applyButton;
    private Button _revertButton;
    private Button _reloadFileButton;
    private Button _closeEntityButton;
    private Button _floatingModeButton;
    private ConfirmationDialog _closeDialog;
    private ConfirmationDialog _conflictDialog;
    private string _pendingCloseId;
    private readonly Queue<string> _pendingConflicts = new();
    private readonly HashSet<string> _pendingConflictIds = new(StringComparer.Ordinal);
    private bool _uiBuilt;
    private bool _loaded;
    private bool _refreshingEntityTabs;
    private bool _storeRefreshQueued;
    private int _uiGeneration;
    private double _externalCheckIn;
    private bool _externalCheckQueued;
    private double _buildCheckIn;
    private long _knownAssemblyStamp;
    private ContentEditorLifecycle _lifecycle = ContentEditorLifecycle.Constructed;
    private bool _reparenting;

    public ContentEditorLifecycle Lifecycle => _lifecycle;
    public bool IsOperational => _lifecycle == ContentEditorLifecycle.Ready && UiReferencesAreValid();

    /// <summary>Плагин переносит этот Control; сам экран не должен владеть Window.</summary>
    public event Action FloatingModeToggleRequested;

    public void SetFloatingMode(bool floating)
    {
        if (IsInstanceValid(_floatingModeButton))
        {
            _floatingModeButton.Text = floating
                ? "Dock in editor"
                : "Open fullscreen";
            _floatingModeButton.TooltipText = floating
                ? "Close the separate window and return the editor to the Content tab"
                : "Move the content editor into a separate fullscreen window";
            _floatingModeButton.Icon = floating
                ? ContentEditorTheme.IconAny("Panels", "GuiTabMenuHl", "Window")
                : ContentEditorTheme.IconAny("MakeFloating", "DistractionFree", "Window");
        }
    }

    public override void _Ready()
    {
        EnsureOperational();
    }

    /// <summary>
    /// Перевести экран в полноценное состояние. Метод можно вызывать из _Ready,
    /// _MakeVisible, уведомления фокуса и проверки сборки: повторный вызов безопасен.
    /// </summary>
    public bool EnsureOperational(bool forceReload = false)
    {
        if (_lifecycle == ContentEditorLifecycle.Disposed || !IsInstanceValid(this))
            return false;

        if (!EnsureUiBuilt())
            return false;

        if (_lifecycle == ContentEditorLifecycle.Loading)
            return false;

        if (!forceReload && _lifecycle == ContentEditorLifecycle.Ready)
            return true;

        _lifecycle = ContentEditorLifecycle.Loading;

        try
        {
            // Снимок читается до ReloadFromDisk: его Changed может сразу записать
            // текущее состояние. Локальная копия сохраняет вкладки между C#-сборками.
            ContentEditorWorkspace workspace = LoadWorkspace();

            // Bind до ReloadFromDisk: Changed может прийти уже во время загрузки.
            _form.Bind(_store);
            _preview.Bind(_store);
            _balance.Bind(_store, _icons);
            _waves.Bind(_store, _icons);

            _store.ReloadFromDisk();
            if (workspace != null)
            {
                _store.RestoreWorkspace(workspace);
                ApplyUiWorkspace(workspace);
            }
            RefreshCatalog();
            RefreshTabs();
            _loaded = true;
            _knownAssemblyStamp = CurrentAssemblyStamp();
            _buildCheckIn = BuildCheckIntervalSec;
            _lifecycle = ContentEditorLifecycle.Ready;
            SetStatus(forceReload ? "editor reloaded after build" : "catalog loaded");
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
    /// Перенести экран между EditorMainScreen и Window. Обычный _ExitTree означает
    /// выгрузку плагина, однако при Reparent кратковременный выход из дерева не должен
    /// переводить Store в Disposed.
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

        PersistWorkspace();
        _externalCheckQueued = false;
        _storeRefreshQueued = false;
        SetProcess(false);
        _loaded = false;
        _lifecycle = ContentEditorLifecycle.Disposed;
    }

    public override void _Notification(int what)
    {
        // Возврат в окно Godot после правки файла в Cursor / другом редакторе
        if (what == NotificationApplicationFocusIn && Visible)
        {
            if (EnsureOperational())
                CheckExternalChanges();
        }
    }

    public override void _Process(double delta)
    {
        if (_lifecycle is ContentEditorLifecycle.Disposed or ContentEditorLifecycle.Faulted)
            return;

        _buildCheckIn -= delta;
        if (_buildCheckIn <= 0)
        {
            _buildCheckIn = BuildCheckIntervalSec;
            CheckAssemblyBuild();
        }

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
    }

    /// <summary>
    /// EditorPlugin может получить _MakeVisible сразу после AddChild, до _Ready.
    /// Кроме того, после перезагрузки C# нативные дочерние Control могут пережить
    /// старый managed-экземпляр. Маркер MVID и проверка ссылок отличают целый UI
    /// от такого остаточного дерева.
    /// </summary>
    private bool EnsureUiBuilt()
    {
        if (_lifecycle == ContentEditorLifecycle.Disposed)
            return false;

        string buildToken = CurrentBuildToken();
        bool sameBuild = HasMeta(BuildTokenMeta)
                         && GetMeta(BuildTokenMeta).AsString() == buildToken;

        if (_uiBuilt && sameBuild && UiReferencesAreValid())
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
            SetMeta(BuildTokenMeta, buildToken);
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
        if (IsInstanceValid(_form))
            _form.FieldFilterChanged -= PersistWorkspace;
        if (IsInstanceValid(_balance))
            _balance.UiStateChanged -= PersistWorkspace;

        foreach (Node child in GetChildren())
        {
            // Дочерние элементы принадлежат только этому экрану. Снимаем их из дерева
            // сразу, чтобы новая раскладка не сосуществовала со старой до конца кадра.
            RemoveChild(child);
            child.Free();
        }

        _store = new ContentEditorStore();
        _modeTabs = null;
        _search = null;
        _catalogTabs = null;
        _catalog = null;
        _entityTabs = null;
        _form = null;
        _preview = null;
        _contextTabs = null;
        _balance = null;
        _icons = null;
        _waves = null;
        _status = null;
        _entityActions = null;
        _entityPanel = null;
        _reloadAllButton = null;
        _applyButton = null;
        _revertButton = null;
        _reloadFileButton = null;
        _closeEntityButton = null;
        _floatingModeButton = null;
        _closeDialog = null;
        _conflictDialog = null;
        _pendingCloseId = null;
        _pendingConflicts.Clear();
        _pendingConflictIds.Clear();
        _externalCheckQueued = false;
        _storeRefreshQueued = false;
        _refreshingEntityTabs = false;
        _loaded = false;
        _uiBuilt = false;
        _uiGeneration++;
        _lifecycle = ContentEditorLifecycle.Constructed;
    }

    private bool UiReferencesAreValid() =>
        _uiBuilt
        && IsInstanceValid(_modeTabs)
        && IsInstanceValid(_catalog)
        && IsInstanceValid(_catalogTabs)
        && IsInstanceValid(_entityTabs)
        && IsInstanceValid(_form)
        && IsInstanceValid(_preview)
        && IsInstanceValid(_contextTabs)
        && IsInstanceValid(_balance)
        && IsInstanceValid(_icons)
        && IsInstanceValid(_waves)
        && IsInstanceValid(_status)
        && IsInstanceValid(_entityActions)
        && IsInstanceValid(_entityPanel)
        && IsInstanceValid(_applyButton)
        && IsInstanceValid(_floatingModeButton)
        && IsInstanceValid(_closeDialog)
        && IsInstanceValid(_conflictDialog);

    private static string CurrentBuildToken() =>
        typeof(ContentEditorMain).Assembly.ManifestModule.ModuleVersionId.ToString("N");

    private static long CurrentAssemblyStamp()
    {
        try
        {
            string location = typeof(ContentEditorMain).Assembly.Location;
            return string.IsNullOrEmpty(location) || !File.Exists(location)
                ? 0L
                : File.GetLastWriteTimeUtc(location).Ticks;
        }
        catch
        {
            // Некоторые среды загружают сборку из памяти. MVID всё равно проверяется.
            return 0L;
        }
    }

    private void CheckAssemblyBuild()
    {
        if (_lifecycle != ContentEditorLifecycle.Ready)
            return;

        long current = CurrentAssemblyStamp();
        if (current == 0L || _knownAssemblyStamp == 0L)
        {
            _knownAssemblyStamp = current;
            return;
        }

        if (current == _knownAssemblyStamp)
            return;

        _knownAssemblyStamp = current;
        _lifecycle = ContentEditorLifecycle.Stale;
        if (IsInstanceValid(_status))
            _status.Text = "new C# assembly detected; reloading editor";

        // Не пересоздаём Controls из _Process: текущий кадр может обходить их дерево.
        CallDeferred(nameof(ReloadAfterDetectedBuild));
    }

    private void ReloadAfterDetectedBuild() => EnsureOperational(forceReload: true);

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

        RefreshCatalog();
        RefreshTabs();

        if (report.Reloaded.Count > 0 || report.CatalogChanged > 0)
        {
            string parts = report.Reloaded.Count > 0
                ? $"reloaded from disk: {string.Join(", ", report.Reloaded)}"
                : "catalog updated";
            if (report.CatalogChanged > 0 && report.Reloaded.Count > 0)
                parts += "; catalog updated";
            SetStatus(parts);
        }

        if (report.Conflicts.Count == 0)
            return;

        foreach (string id in report.Conflicts)
        {
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
            return;

        string id = _pendingConflicts.Peek();
        _conflictDialog.DialogText =
            $"File “{id}” changed externally, and this tab has unsaved edits.\n" +
            "“Reload from disk” discards the draft. “Keep draft” keeps the editor changes " +
            "and will overwrite the external file on Apply.";
        _conflictDialog.PopupCentered();
    }

    private void BuildUi()
    {
        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        AddChild(root);
        // Preset вычисляет offsets относительно родителя, поэтому применяется после AddChild.
        // До исправления корень оставался высотой своего minimum size и занимал верх экрана.
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Верхняя полоса относится ко всему проекту целиком. Действия над отдельной
        // сущностью вынесены в полосу под вкладками: прежде «Применить» и «Сбросить»
        // стояли здесь же и не показывали, к чему именно они применяются.
        var toolbar = new HBoxContainer();
        toolbar.AddThemeConstantOverride("separation", 6);
        root.AddChild(toolbar);

        var scope = new Label
        {
            Text = "Project",
            Modulate = new Color(1f, 1f, 1f, 0.55f),
        };
        scope.AddThemeFontSizeOverride("font_size", 12);
        toolbar.AddChild(scope);

        _reloadAllButton = AddToolButton(toolbar, "Reload all files", "Reload", () =>
        {
            _store.ReloadFromDisk();
            // Глобальный каталог игры тоже обновляем: предпросмотры сессии читают Content.Catalog
            Content.Reload();
            // Чистые вкладки уже в ReloadFromDisk не сбрасываются — подтянуть их явно
            foreach (var session in _store.Sessions.ToList())
            {
                if (!session.Dirty)
                    _store.AcceptExternalReload(session.Id);
            }

            RefreshCatalog();
            RefreshTabs();
            SetStatus("all files reloaded from disk");
        });
        _reloadAllButton.TooltipText =
            "Re-read every .toml of the project. Drafts of unsaved tabs are kept.";

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
        _floatingModeButton.Pressed += () =>
        {
            if (EnsureOperational())
                FloatingModeToggleRequested?.Invoke();
        };
        toolbar.AddChild(_floatingModeButton);

        _modeTabs = new TabContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        root.AddChild(_modeTabs);

        var entities = new HSplitContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _modeTabs.AddChild(entities);
        _modeTabs.SetTabTitle(0, "Entities");

        var left = new EditorPanel("Catalog") { CustomMinimumSize = new Vector2(240, 0) };
        left.TitleTooltip = "Definitions of the project grouped by kind";
        entities.AddChild(left);

        _search = new LineEdit
        {
            PlaceholderText = "Search catalog…",
            RightIcon = ContentEditorTheme.Icon("Search"),
        };
        _search.TextChanged += _ => RefreshCatalog();
        left.AddContent(_search);

        // Виды содержимого разделены вкладками: смешанный список заставлял различать
        // юнита и его ствол по двухбуквенному префиксу.
        _catalogTabs = new TabBar
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _catalogTabs.AddTab("Units");
        _catalogTabs.AddTab("Buildings");
        _catalogTabs.AddTab("Weapons");
        _catalogTabs.AddTab("Tools");
        _catalogTabs.TabChanged += _ => RefreshCatalog();
        left.AddContent(_catalogTabs);

        // Одинарный щелчок открывает вкладку: художнику не нужен отдельный жест «активировать»
        _catalog = new ItemList
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            FixedIconSize = new Vector2I(24, 24),
        };
        _catalog.ItemActivated += OnCatalogActivated;
        _catalog.ItemSelected += OnCatalogSelectedOpen;
        left.AddContent(_catalog);

        var right = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        entities.AddChild(right);

        // Нативный TabBar связывает крестик с самой вкладкой и поддерживает
        // горизонтальную прокрутку. Отдельные Button + «×» не давали понять,
        // к какому названию относится закрытие.
        _entityTabs = new TabBar
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TabCloseDisplayPolicy = TabBar.CloseButtonDisplayPolicy.ShowAlways,
            ScrollingEnabled = true,
        };
        _entityTabs.TabChanged += OnEntityTabChanged;
        _entityTabs.TabClosePressed += OnEntityTabClosePressed;
        StyleEntityTabs(_entityTabs);
        right.AddChild(_entityTabs);

        var workspace = new HSplitContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        right.AddChild(workspace);

        // Центральная область делится по вертикали только при наличии контекстных
        // вкладок. Скрытый EditorContextTabs не отнимает место у предпросмотра.
        var center = new VSplitContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        workspace.AddChild(center);

        var previewPanel = new EditorPanel("Measure field · all open entities");
        previewPanel.TitleTooltip =
            "Every open entity with “Show on field” drawn at the same scale";
        center.AddChild(previewPanel);

        _preview = new ContentEditorPreview
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(360, 240),
        };
        previewPanel.AddContent(_preview);

        _contextTabs = new EditorContextTabs();
        center.AddChild(_contextTabs);

        // Панель активной сущности охватывает форму её свойств, а не всю рабочую область:
        // измерительное поле общее для всех открытых вкладок и одной сущности
        // не принадлежит. Кнопки правки стоят в заголовке этой панели, отчего область
        // их действия видна по расположению.
        _entityPanel = new EditorPanel("no entity open") { CustomMinimumSize = new Vector2(450, 0) };
        workspace.AddChild(_entityPanel);
        BuildEntityActions(_entityPanel.Actions);

        _form = new ContentEditorForm
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _entityPanel.AddContent(_form);
        _form.VarsPanelRequested += OpenVarsPanel;
        _form.GraphPanelRequested += OpenGraphPanel;
        _form.FieldFilterChanged += PersistWorkspace;

        _icons = new ContentEditorIconCache();
        AddChild(_icons);
        _icons.IconsReady += OnIconsReady;

        _balance = new ContentEditorBalance();
        _modeTabs.AddChild(_balance);
        _modeTabs.SetTabTitle(1, "Balance");
        _balance.OpenRequested += id =>
        {
            if (!IsOperational)
                return;

            _modeTabs.CurrentTab = 0;
            _store.Open(id);
            RefreshTabs();
        };
        _balance.UiStateChanged += PersistWorkspace;

        // Волны не являются сущностями каталога и правятся в файле, поэтому третий режим
        // показывает их взаимное расположение по шкале террора, а не форму свойств.
        _waves = new ContentEditorWaves();
        _modeTabs.AddChild(_waves);
        _modeTabs.SetTabTitle(2, "Waves");
        _waves.OpenRequested += id =>
        {
            if (!IsOperational)
                return;

            _modeTabs.CurrentTab = 0;
            _store.Open(id);
            RefreshTabs();
        };

        _closeDialog = new ConfirmationDialog
        {
            Title = "Unsaved changes",
            DialogText = "Save changes before closing?",
            OkButtonText = "Save",
        };
        _closeDialog.Confirmed += () =>
        {
            if (!IsOperational)
                return;

            string id = _pendingCloseId;
            string error = _store.Apply(_store.Sessions.FirstOrDefault(s => s.Id == id));
            if (error != null)
            {
                SetStatus(error);
                return;
            }

            _pendingCloseId = null;
            _store.Close(id, discard: true);
            RefreshTabs();
        };
        AddChild(_closeDialog);

        _closeDialog.AddButton("Discard changes", true, "discard");
        _closeDialog.CustomAction += action =>
        {
            if (action != "discard" || !IsOperational)
                return;

            _closeDialog.Hide();
            string id = _pendingCloseId;
            _pendingCloseId = null;
            _store.Close(id, discard: true);
            RefreshTabs();
        };

        // Конфликт: внешний файл новее, а вкладка Dirty
        _conflictDialog = new ConfirmationDialog
        {
            Title = "File changed externally",
            OkButtonText = "Reload from disk",
        };
        _conflictDialog.Confirmed += () =>
        {
            if (!IsOperational || _pendingConflicts.Count == 0)
                return;

            string id = _pendingConflicts.Dequeue();
            _pendingConflictIds.Remove(id);
            _conflictDialog.Hide();
            _store.AcceptExternalReload(id);
            SetStatus($"reloaded from disk: {id}");
            RefreshTabs();
            ShowNextConflict();
        };
        _conflictDialog.Canceled += () =>
        {
            // Отмена диалога = оставить черновик (как вторая кнопка)
            _conflictDialog.Hide();
            KeepDraftForCurrentConflict();
        };
        AddChild(_conflictDialog);

        _conflictDialog.AddButton("Keep draft", true, "keep");
        _conflictDialog.CustomAction += action =>
        {
            if (action != "keep")
                return;

            _conflictDialog.Hide();
            KeepDraftForCurrentConflict();
        };
    }

    private void KeepDraftForCurrentConflict()
    {
        if (!IsOperational || _pendingConflicts.Count == 0)
            return;

        string id = _pendingConflicts.Dequeue();
        _pendingConflictIds.Remove(id);
        _store.KeepDraftDespiteExternal(id);
        SetStatus($"draft kept: {id}");
        ShowNextConflict();
    }

    /// <summary>
    /// Полоса действий над той сущностью, вкладка которой сейчас выбрана. Имя файла
    /// в начале полосы называет объект действия прямо, а не оставляет его угадывать
    /// по выбранной вкладке.
    /// </summary>
    private void BuildEntityActions(HBoxContainer bar)
    {

        _applyButton = AddToolButton(bar, "Apply", "Save", () =>
        {
            var session = _store.ActiveSession;
            if (session == null)
                return;

            string error = _store.Apply(session);
            SetStatus(error ?? $"saved: {session.FileName}");
            RefreshTabs();
        });

        _revertButton = AddToolButton(bar, "Revert", "Undo", () =>
        {
            var session = _store.ActiveSession;
            if (session == null)
                return;

            _store.Revert(session);
            SetStatus($"draft reverted: {session.FileName}");
            RefreshTabs();
        });

        _reloadFileButton = AddToolButton(bar, "Reload file", "Reload", () =>
        {
            var session = _store.ActiveSession;
            if (session == null)
                return;

            string fileName = session.FileName;
            _store.AcceptExternalReload(session.Id);
            SetStatus($"reloaded from disk: {fileName}");
            RefreshTabs();
        });

        _closeEntityButton = AddToolButton(bar, "Close", "Close", () =>
        {
            var session = _store.ActiveSession;
            if (session != null)
                RequestClose(session.Id);
        });

        _entityActions = bar;
    }

    /// <summary>
    /// Согласовать доступность кнопок с состоянием. Кнопка, нажатие на которую ничего
    /// не изменило бы, выключается, а её подсказка объясняет причину.
    /// </summary>
    private void UpdateActionStates()
    {
        if (!IsInstanceValid(_entityActions) || !IsInstanceValid(_entityPanel) || _store == null)
            return;

        var session = _store.ActiveSession;
        bool open = session != null;
        bool dirty = open && session.Dirty;

        _entityPanel.Title = open
            ? $"{session.FileName} ({session.DisplayName}){(dirty ? "  •  unsaved" : "")}"
            : "no entity open";
        _entityPanel.TitleTooltip = open ? session.Path : "";

        ContentEditorTheme.SetAction(_applyButton, dirty,
            open ? $"Write the draft of {session.FileName} to disk" : "",
            open ? "No unsaved changes in this entity" : "No entity is open");
        ContentEditorTheme.SetAction(_revertButton, dirty,
            open ? $"Discard the draft of {session.FileName}" : "",
            open ? "No unsaved changes in this entity" : "No entity is open");
        ContentEditorTheme.SetAction(_reloadFileButton, open,
            open ? $"Re-read {session.FileName} from disk and discard the draft" : "",
            "No entity is open");
        ContentEditorTheme.SetAction(_closeEntityButton, open,
            open ? $"Close the tab of {session.FileName}" : "",
            "No entity is open");
    }

    private Button AddToolButton(BoxContainer bar, string text, string icon, Action action)
    {
        var button = new Button
        {
            Text = text,
            Icon = ContentEditorTheme.Icon(icon),
        };
        button.Pressed += () =>
        {
            if (EnsureOperational())
                action();
        };
        bar.AddChild(button);
        return button;
    }

    private void OpenVarsPanel(string path)
    {
        var session = _store.ActiveSession;
        if (session?.Kind is not (ContentEntityKind.Unit or ContentEntityKind.Building))
            return;

        _contextTabs.SetContext(session.Id, preservePages: true);
        VarsPanelData data = _store.VarsPanel(path);
        if (data == null || data.ContextId != session.Id)
            return;

        string key = $"vars:{path}";
        if (_contextTabs.Page(key) is EditorKeyValueTable existing)
        {
            existing.SetRows(data.Rows);
            _contextTabs.Select(key);
            return;
        }

        var table = new EditorKeyValueTable();
        table.SetRows(data.Rows);
        _contextTabs.Open(key, data.FileName, table);
    }

    private void OpenGraphPanel()
    {
        var session = _store.ActiveSession;
        if (session?.Kind is not (ContentEntityKind.Unit or ContentEntityKind.Building))
            return;

        _contextTabs.SetContext(session.Id, preservePages: true);
        ContentGraphData data = _store.ActiveContextGraph();
        if (data == null || data.ContextId != session.Id)
            return;

        if (_contextTabs.Page("graph") is EditorNodeGraph existing)
        {
            existing.SetGraph(data);
            _contextTabs.Select("graph");
            return;
        }

        var graph = new EditorNodeGraph();
        graph.SetGraph(data);
        graph.NodeActivated += OpenGraphNode;
        _contextTabs.Open("graph", "Relations", graph);
    }

    private void OpenGraphNode(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        if (TomlResolver.IsVars(path))
        {
            OpenVarsPanel(path);
            return;
        }

        _store.OpenPath(path);
    }

    private void RefreshContextPanels()
    {
        var session = _store.ActiveSession;
        string contextId = session?.Kind is ContentEntityKind.Unit or ContentEntityKind.Building
            ? session.Id
            : null;
        if (contextId == null)
        {
            _contextTabs.SetContext(null);
            return;
        }

        ContentGraphData graphData = _store.ActiveContextGraph();
        _contextTabs.SetContext(contextId, preservePages: true);
        var referencedVars = graphData?.Nodes
            .Where(node => node.Kind == ContentGraphNodeKind.Vars)
            .Select(node => node.Path)
            .ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);

        foreach (var pair in _contextTabs.Pages.ToList())
        {
            if (pair.Key == "graph" && pair.Value is EditorNodeGraph graph)
            {
                graph.SetGraph(graphData);
                continue;
            }

            if (pair.Key.StartsWith("vars:", StringComparison.Ordinal)
                && pair.Value is EditorKeyValueTable table)
            {
                string path = pair.Key["vars:".Length..];
                if (!referencedVars.Contains(path))
                {
                    _contextTabs.Close(pair.Key);
                    continue;
                }

                VarsPanelData data = _store.VarsPanel(path);
                if (data != null && data.ContextId == contextId)
                    table.SetRows(data.Rows);
            }
        }

        _contextTabs.RevealPages();
    }

    private void OnStoreChanged()
    {
        PersistWorkspace();

        string activeContext = _store.ActiveSession?.Kind is ContentEntityKind.Unit or ContentEntityKind.Building
            ? _store.ActiveSession.Id
            : null;
        if (IsInstanceValid(_contextTabs)
            && !string.Equals(_contextTabs.ContextId, activeContext, StringComparison.Ordinal))
        {
            // Старые данные скрываются немедленно. В deferred-обновлении граф получает
            // новую сущность, а vars-вкладки сохраняются только для общих файлов.
            _contextTabs.Visible = false;
        }

        // Во время Loading событие допустимо; deferred-вызов выполнится уже после Ready.
        // После Shutdown никакие запланированные обновления исполняться не должны.
        if (_lifecycle == ContentEditorLifecycle.Disposed || !UiReferencesAreValid())
            return;

        // Store может измениться из сигнала OptionButton/ColorPicker. Немедленное
        // уничтожение формы внутри этого сигнала повреждало состояние активного виджета.
        // Один deferred-вызов также объединяет несколько событий в пределах кадра.
        if (_storeRefreshQueued)
            return;

        _storeRefreshQueued = true;
        int generation = _uiGeneration;
        CallDeferred(nameof(RefreshAfterStoreChanged), generation);
    }

    private static ContentEditorWorkspace LoadWorkspace()
    {
        try
        {
            string path = ProjectSettings.GlobalizePath(WorkspaceStatePath);
            if (!File.Exists(path))
                return null;

            var config = new ConfigFile();
            if (config.Load(path) != Error.Ok)
                return null;

            var workspace = new ContentEditorWorkspace
            {
                ActiveId = config.GetValue("workspace", "active_id", "").AsString(),
                FormFieldFilter = config.GetValue("form", "field_filter", "").AsString(),
                BalanceViewId = config.GetValue("balance", "view_id", ContentEditorBalance.DefaultViewId).AsString(),
                BalanceSortColumnId = config.GetValue("balance", "sort_column", ContentEditorBalance.DefaultSortColumnId).AsString(),
                BalanceSortAsc = config.GetValue("balance", "sort_asc", false).AsBool(),
                BalanceVisibleColumns = config.GetValue("balance", "visible_columns", Array.Empty<string>()).AsStringArray(),
            };
            int count = config.GetValue("workspace", "session_count", 0).AsInt32();
            for (int i = 0; i < count; i++)
            {
                string section = $"session_{i}";
                ulong.TryParse(
                    config.GetValue(section, "loaded_stamp", "0").AsString(),
                    out ulong stamp);
                workspace.Sessions.Add(new ContentEditorSessionSnapshot
                {
                    Id = config.GetValue(section, "id", "").AsString(),
                    DraftText = config.GetValue(section, "draft_text", "").AsString(),
                    LoadedStamp = stamp,
                    Dirty = config.GetValue(section, "dirty", false).AsBool(),
                    ShowOnField = config.GetValue(section, "show_on_field", true).AsBool(),
                    FieldX = config.GetValue(section, "field_x", 0f).AsSingle(),
                    FieldY = config.GetValue(section, "field_y", 0f).AsSingle(),
                });
            }

            int savedCount = config.GetValue("balance", "saved_count", 0).AsInt32();
            for (int i = 0; i < savedCount; i++)
            {
                string section = $"balance_view_{i}";
                workspace.BalanceSavedViews.Add(new ContentEditorSavedView
                {
                    Name = config.GetValue(section, "name", "").AsString(),
                    ColumnIds = config.GetValue(section, "columns", Array.Empty<string>()).AsStringArray(),
                });
            }

            return workspace;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Content editor] tab state was not read: {ex.Message}");
            return null;
        }
    }

    private void PersistWorkspace()
    {
        if (_store == null)
            return;

        try
        {
            string path = ProjectSettings.GlobalizePath(WorkspaceStatePath);
            string temp = path + ".tmp";
            ContentEditorWorkspace workspace = _store.CaptureWorkspace();
            if (IsInstanceValid(_form))
                workspace.FormFieldFilter = _form.FieldFilter;
            if (IsInstanceValid(_balance))
            {
                var balance = _balance.CaptureUiState();
                workspace.BalanceViewId = balance.ViewId;
                workspace.BalanceVisibleColumns = balance.VisibleColumnIds;
                workspace.BalanceSortColumnId = balance.SortColumnId;
                workspace.BalanceSortAsc = balance.SortAsc;
                workspace.BalanceSavedViews = balance.SavedViews;
            }

            var config = new ConfigFile();
            config.SetValue("workspace", "active_id", workspace.ActiveId ?? "");
            config.SetValue("workspace", "session_count", workspace.Sessions.Count);
            for (int i = 0; i < workspace.Sessions.Count; i++)
            {
                ContentEditorSessionSnapshot session = workspace.Sessions[i];
                string section = $"session_{i}";
                config.SetValue(section, "id", session.Id ?? "");
                config.SetValue(section, "draft_text", session.DraftText ?? "");
                config.SetValue(section, "loaded_stamp", session.LoadedStamp.ToString());
                config.SetValue(section, "dirty", session.Dirty);
                config.SetValue(section, "show_on_field", session.ShowOnField);
                config.SetValue(section, "field_x", session.FieldX);
                config.SetValue(section, "field_y", session.FieldY);
            }

            config.SetValue("form", "field_filter", workspace.FormFieldFilter ?? "");
            config.SetValue("balance", "view_id", workspace.BalanceViewId ?? ContentEditorBalance.DefaultViewId);
            config.SetValue("balance", "sort_column", workspace.BalanceSortColumnId ?? ContentEditorBalance.DefaultSortColumnId);
            config.SetValue("balance", "sort_asc", workspace.BalanceSortAsc);
            config.SetValue("balance", "visible_columns", workspace.BalanceVisibleColumns ?? Array.Empty<string>());
            config.SetValue("balance", "saved_count", workspace.BalanceSavedViews?.Count ?? 0);
            if (workspace.BalanceSavedViews != null)
            {
                for (int i = 0; i < workspace.BalanceSavedViews.Count; i++)
                {
                    ContentEditorSavedView view = workspace.BalanceSavedViews[i];
                    string section = $"balance_view_{i}";
                    config.SetValue(section, "name", view.Name ?? "");
                    config.SetValue(section, "columns", view.ColumnIds ?? Array.Empty<string>());
                }
            }

            Error save = config.Save(temp);
            if (save != Error.Ok)
                throw new IOException($"ConfigFile.Save: {save}");
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Content editor] tab state was not saved: {ex.Message}");
        }
    }

    private void RefreshAfterStoreChanged(int generation)
    {
        // Callback старого поколения не должен менять флаг нового UI. Для текущего
        // поколения флаг снимается до проверок: Stale/Loading иначе блокировали все
        // последующие Changed.
        if (generation != _uiGeneration)
            return;

        _storeRefreshQueued = false;
        if (_lifecycle == ContentEditorLifecycle.Disposed
            || !IsInstanceValid(this)
            || !IsInsideTree()
            || !IsOperational)
            return;

        RefreshContextPanels();
        _form.Refresh();
        _preview.RefreshFromStore();
        _balance.Refresh();
        _waves.Refresh();
        RefreshTabs();
    }

    private void RefreshCatalog()
    {
        if (!IsInstanceValid(_catalog) || !IsInstanceValid(_catalogTabs) || _store == null)
            return;

        _catalog.Clear();
        string query = (_search?.Text ?? "").Trim().ToLowerInvariant();
        int category = _catalogTabs?.CurrentTab ?? 0;

        foreach (var entry in _store.Entries.OrderBy(e => e.FileName, StringComparer.OrdinalIgnoreCase))
        {
            // Abstract-шаблоны (base_unit и т.п.) в игре не создаются — в каталоге редактора тоже скрыты
            if (entry.Abstract)
                continue;

            if (!MatchesCategory(entry.Kind, category))
                continue;

            // Имя файла — устойчивый технический ориентир и совпадает с деревом FileSystem.
            // Отображаемое имя остаётся в скобках для художника и геймдизайнера.
            string label = $"{entry.FileName} ({entry.DisplayName})";
            if (query.Length > 0 &&
                !label.ToLowerInvariant().Contains(query) &&
                !entry.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;

            int index = _catalog.AddItem(label, CatalogIcon(entry));
            _catalog.SetItemMetadata(index, entry.Id);
        }
    }

    private void RefreshTabs()
    {
        if (!IsInstanceValid(_entityTabs) || _store == null)
            return;

        _refreshingEntityTabs = true;
        _entityTabs.ClearTabs();

        int active = -1;
        int index = 0;
        foreach (var session in _store.Sessions)
        {
            _entityTabs.AddTab(session.TabTitle);
            _entityTabs.SetTabMetadata(index, session.Id);
            _entityTabs.SetTabTooltip(index,
                $"{session.FileName} ({session.DisplayName})\n{session.Path}");

            if (_store.ActiveSession == session)
                active = index;

            index++;
        }

        if (active >= 0)
            _entityTabs.CurrentTab = active;

        _refreshingEntityTabs = false;
        UpdateActionStates();
    }

    private void OnEntityTabChanged(long index)
    {
        if (!IsOperational
            || !IsInstanceValid(_entityTabs)
            || _refreshingEntityTabs
            || index < 0
            || index >= _entityTabs.TabCount)
            return;

        string id = _entityTabs.GetTabMetadata((int)index).AsString();
        _store.Activate(id);
    }

    private void OnEntityTabClosePressed(long index)
    {
        if (!IsOperational
            || !IsInstanceValid(_entityTabs)
            || index < 0
            || index >= _entityTabs.TabCount)
            return;

        string id = _entityTabs.GetTabMetadata((int)index).AsString();
        RequestClose(id);
    }

    private void RequestClose(string id)
    {
        if (!IsOperational)
            return;

        var session = _store.Sessions.FirstOrDefault(s => s.Id == id);
        if (session == null)
            return;

        if (!session.Dirty)
        {
            _store.Close(id, discard: true);
            RefreshTabs();
            return;
        }

        _pendingCloseId = id;
        _closeDialog.PopupCentered();
    }

    private void OnCatalogActivated(long index) => OpenCatalogIndex((int)index);

    private void OnCatalogSelectedOpen(long index) => OpenCatalogIndex((int)index);

    private void OpenCatalogIndex(int index)
    {
        if (!IsOperational
            || !IsInstanceValid(_catalog)
            || index < 0
            || index >= _catalog.ItemCount)
            return;

        string id = _catalog.GetItemMetadata(index).AsString();
        _store.Open(id);
        RefreshTabs();
        SetStatus($"opened: {id}");
    }

    private void SetStatus(string text)
    {
        if (IsInstanceValid(_status))
            _status.Text = text ?? "";
    }

    private Texture2D CatalogIcon(ContentEditorEntry entry)
    {
        if (!IsInstanceValid(_icons) || _store == null || entry == null)
            return null;

        var unit = _store.PreviewUnit(entry.Id);
        if (unit != null)
            return _icons.GetUnit(unit);

        var tool = _store.PreviewTool(entry.Id);
        return tool == null ? null : _icons.GetSprite(tool.Sprite);
    }

    private void ApplyUiWorkspace(ContentEditorWorkspace workspace)
    {
        if (workspace == null)
            return;

        if (IsInstanceValid(_form))
            _form.FieldFilter = workspace.FormFieldFilter ?? "";

        if (IsInstanceValid(_balance))
        {
            _balance.RestoreUiState(new ContentEditorBalanceUiState
            {
                ViewId = workspace.BalanceViewId,
                VisibleColumnIds = workspace.BalanceVisibleColumns,
                SortColumnId = workspace.BalanceSortColumnId,
                SortAsc = workspace.BalanceSortAsc,
                SavedViews = workspace.BalanceSavedViews,
            });
        }
    }

    private void OnIconsReady()
    {
        if (!IsOperational)
            return;

        RefreshCatalog();
        _balance.Refresh();
        _waves.Refresh();
    }

    private static void StyleEntityTabs(TabBar tabs)
    {
        var selected = new StyleBoxFlat
        {
            BgColor = new Color(0.38f, 0.55f, 0.82f),
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
        };
        var hovered = new StyleBoxFlat
        {
            BgColor = new Color(0.28f, 0.36f, 0.5f),
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
        };
        var unselected = new StyleBoxFlat
        {
            BgColor = new Color(0.14f, 0.15f, 0.18f),
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
        };
        tabs.AddThemeStyleboxOverride("tab_selected", selected);
        tabs.AddThemeStyleboxOverride("tab_hovered", hovered);
        tabs.AddThemeStyleboxOverride("tab_unselected", unselected);
        tabs.AddThemeColorOverride("font_selected_color", Colors.White);
        tabs.AddThemeColorOverride("font_hovered_color", new Color(0.92f, 0.95f, 1f));
        tabs.AddThemeColorOverride("font_unselected_color", new Color(1f, 1f, 1f, 0.5f));
    }

    private static bool MatchesCategory(ContentEntityKind kind, int category) => category switch
    {
        0 => kind == ContentEntityKind.Unit,
        1 => kind == ContentEntityKind.Building,
        2 => kind == ContentEntityKind.Weapon,
        3 => kind == ContentEntityKind.WorkTool,
        _ => false,
    };
}
