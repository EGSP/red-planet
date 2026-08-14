using Godot;

/// <summary>
/// C#-мост вкладки «Контент». Сам EditorPlugin живёт в plugin.gd: узел на C# нельзя
/// оставлять корнем плагина, иначе выгрузка сборки застаёт его в дереве редактора.
///
/// ПОЧЕМУ MAIN SCREEN, А НЕ DOCK. Редактору нужны каталог, поле сравнения и форма
/// одновременно; узкая боковая панель для этого тесна. Вкладка рядом с 2D/3D/Script
/// даёт полную ширину и не перекрывает инспектор сцены.
///
/// ЖИЗНЕННЫЙ ЦИКЛ. Control создаётся в _EnterTree и остаётся потомком главного экрана
/// до выгрузки моста. Видимость переключает GDScript-плагин через MakeVisible.
/// Первая загрузка каталога откладывается до первого показа.
///
/// ВЫГРУЗКА СБОРКИ. native-узлы игровых типов снимаются в трёх местах: Teardown моста,
/// выход GDScript-плагина и AssemblyLoadContext.Unloading — последнее срабатывает
/// даже когда Godot не вызывает C# _ExitTree. Остаточный узел не переиспользуется.
/// Вкладки возвращает снимок .godot/content_editor_workspace.cfg.
///
/// ВНЕШНИЕ ПРАВКИ. Подписка на EditorFileSystem.FilesystemChanged ловит сохранение файла
/// внутри Godot. Возврат фокуса окна и повторный показ вкладки ловят правку во внешнем
/// редакторе (Cursor и т.п.), которую FilesystemChanged может не прислать сразу.
/// </summary>
[Tool]
public partial class ContentEditorPlugin : Node
{
    public const string BridgeNodeName = "RedPlanetContentEditorBridge";

    private const string FloatingWindowName = "RedPlanetContentEditorWindow";

    private static ContentEditorPlugin _instance;

    private ContentEditorMain _main;
    private Window _floatingWindow;
    private EditorFileSystem _watchedFileSystem;
    private bool _fileSystemSubscribed;
    private bool _floatingWindowCloseSubscribed;
    private bool _active;

    public override void _EnterTree()
    {
        _instance = this;
        Name = BridgeNodeName;
        AssemblyUnloadCleanup.BeforeUnload -= ReleaseForAssemblyUnload;
        AssemblyUnloadCleanup.BeforeUnload += ReleaseForAssemblyUnload;
        _active = true;
        CallDeferred(nameof(EnsureMainDeferred));
    }

    /// <summary>
    /// Создать скрытый главный экран после входа моста в дерево. Синхронный EnsureMain
    /// из _EnterTree оболочки останавливал загрузку редактора; отложенный вызов
    /// приходится на кадр, когда EditorInterface уже доступен.
    /// </summary>
    private void EnsureMainDeferred()
    {
        if (!_active || !IsInsideTree())
            return;

        EnsureMain();
    }

    public override void _ExitTree()
    {
        AssemblyUnloadCleanup.BeforeUnload -= ReleaseForAssemblyUnload;
        Teardown();
        if (_instance == this)
            _instance = null;
    }

    /// <summary>
    /// Снять C#-узлы редактора контента, пока управляемый код ещё исполняется.
    /// Вызывается из AssemblyLoadContext.Unloading до фактической выгрузки ALC.
    /// </summary>
    public static void ReleaseForAssemblyUnload()
    {
        if (!Engine.IsEditorHint())
            return;

        ContentEditorPlugin instance = _instance;
        _instance = null;

        if (instance != null && IsInstanceValid(instance))
        {
            AssemblyUnloadCleanup.BeforeUnload -= ReleaseForAssemblyUnload;
            instance.Teardown();
            FreeNode(instance);
            return;
        }

        var editor = EditorInterface.Singleton;
        FreeNamedChild(editor?.GetBaseControl(), FloatingWindowName);
        FreeNamedChild(editor?.GetEditorMainScreen(), ContentEditorMain.EditorNodeName);
    }

    public void MakeVisible(bool visible)
    {
        if (!_active || !EnsureMain())
            return;

        // Отдельное окно не связано с переключением вкладок 2D/3D/Script.
        if (IsFloating())
        {
            _main.Visible = true;
            if (_main.EnsureOperational())
                _main.CheckExternalChanges();
            return;
        }

        _main.Visible = visible;
        if (!visible)
            return;

        EnsureFileSystemSubscription();
        if (_main.EnsureOperational())
            _main.CheckExternalChanges();
    }

    public Texture2D GetPluginIcon()
    {
        var editor = EditorInterface.Singleton;
        var baseControl = editor?.GetBaseControl();
        return IsInstanceValid(baseControl)
            ? baseControl.GetThemeIcon("ResourcePreloader", "EditorIcons")
            : null;
    }

    private void Teardown()
    {
        _active = false;
        RemoveFileSystemSubscription();

        if (IsInstanceValid(_main))
        {
            _main.FloatingModeToggleRequested -= ToggleFloatingMode;
            _main.Shutdown();
        }

        RemoveFloatingWindowSubscription();

        var editor = EditorInterface.Singleton;
        var screen = editor?.GetEditorMainScreen();
        var baseControl = editor?.GetBaseControl();

        // Окно держит главный экран как потомка; его освобождают первым.
        FreeNamedChild(baseControl, FloatingWindowName);
        FreeNamedChild(screen, ContentEditorMain.EditorNodeName);
        FreeNode(_main);
        FreeNode(_floatingWindow);

        _main = null;
        _floatingWindow = null;
    }

    private void OnFilesystemChanged()
    {
        // Пока вкладка скрыта, копим только факт; сверка при показе. Если открыта —
        // проверяем сразу (с debounce внутри Main).
        if (!_active || !EnsureMain())
            return;

        _main.RequestExternalCheck();
    }

    /// <summary>
    /// Получить целый главный экран либо создать новый. Остаточный узел прошлой сборки
    /// не переиспользуется: его типы принадлежат выгружаемому контексту.
    /// </summary>
    private bool EnsureMain()
    {
        if (IsMainValid())
        {
            SubscribeMain();
            EnsureFloatingWindowSubscription();
            return true;
        }

        var editor = EditorInterface.Singleton;
        var screen = editor?.GetEditorMainScreen();
        var baseControl = editor?.GetBaseControl();
        if (!IsInstanceValid(screen) || !IsInstanceValid(baseControl))
            return false;

        if (IsInstanceValid(_main))
            _main.FloatingModeToggleRequested -= ToggleFloatingMode;

        RemoveFloatingWindowSubscription();
        FreeNamedChild(baseControl, FloatingWindowName);
        FreeNamedChild(screen, ContentEditorMain.EditorNodeName);
        _main = null;
        _floatingWindow = null;

        _main = new ContentEditorMain
        {
            Name = ContentEditorMain.EditorNodeName,
            Visible = false,
        };
        screen.AddChild(_main);

        // EditorMainScreen не является Container для дочернего Control плагина.
        _main.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        SubscribeMain();
        _main.SetFloatingMode(false);
        return true;
    }

    private void SubscribeMain()
    {
        _main.FloatingModeToggleRequested -= ToggleFloatingMode;
        _main.FloatingModeToggleRequested += ToggleFloatingMode;
    }

    private bool IsMainValid() =>
        _main != null
        && IsInstanceValid(_main)
        && _main.Lifecycle != ContentEditorLifecycle.Disposed;

    private bool IsFloating() =>
        IsMainValid()
        && IsInstanceValid(_floatingWindow)
        && _main.GetParent() == _floatingWindow;

    private void ToggleFloatingMode()
    {
        if (!_active || !EnsureMain())
            return;

        if (IsFloating())
            DockMain();
        else
            OpenFullscreenWindow();
    }

    private void OpenFullscreenWindow()
    {
        var baseControl = EditorInterface.Singleton?.GetBaseControl();
        if (!IsInstanceValid(baseControl) || !IsMainValid())
            return;

        _floatingWindow = new Window
        {
            Name = FloatingWindowName,
            Title = "Red Planet — content editor",
            Exclusive = false,
            Transient = false,
        };
        _floatingWindow.CloseRequested += DockMain;
        _floatingWindowCloseSubscribed = true;
        baseControl.AddChild(_floatingWindow);

        // Reparent внутри одного SceneTree не вызывает _ExitTree у Main, поэтому Store,
        // открытые вкладки и отложенные проверки сохраняются.
        _main.ReparentWithoutShutdown(_floatingWindow);
        if (_main.GetParent() != _floatingWindow)
        {
            RemoveFloatingWindowSubscription();
            _floatingWindow.QueueFree();
            _floatingWindow = null;
            return;
        }
        _main.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _main.Visible = true;
        _main.SetFloatingMode(true);

        _floatingWindow.Show();
        _floatingWindow.Mode = Window.ModeEnum.Fullscreen;
    }

    private void DockMain()
    {
        if (!IsFloating())
            return;

        var editor = EditorInterface.Singleton;
        var screen = editor?.GetEditorMainScreen();
        if (!IsInstanceValid(screen))
            return;

        Window oldWindow = _floatingWindow;
        _main.ReparentWithoutShutdown(screen);
        if (_main.GetParent() != screen)
            return;
        _main.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _main.SetFloatingMode(false);
        _main.Visible = true;
        RemoveFloatingWindowSubscription();
        _floatingWindow = null;

        oldWindow.Hide();
        oldWindow.QueueFree();
        editor.SetMainScreenEditor("Content");
    }

    private void EnsureFloatingWindowSubscription()
    {
        if (!IsInstanceValid(_floatingWindow) || _floatingWindowCloseSubscribed)
            return;

        _floatingWindow.CloseRequested += DockMain;
        _floatingWindowCloseSubscribed = true;
    }

    private void RemoveFloatingWindowSubscription()
    {
        if (_floatingWindowCloseSubscribed && IsInstanceValid(_floatingWindow))
            _floatingWindow.CloseRequested -= DockMain;

        _floatingWindowCloseSubscribed = false;
    }

    private void EnsureFileSystemSubscription()
    {
        var fs = EditorInterface.Singleton?.GetResourceFilesystem();
        if (!IsInstanceValid(fs))
            return;

        if (_fileSystemSubscribed
            && IsInstanceValid(_watchedFileSystem)
            && _watchedFileSystem != fs)
        {
            _watchedFileSystem.FilesystemChanged -= OnFilesystemChanged;
            _fileSystemSubscribed = false;
        }

        if (_fileSystemSubscribed && _watchedFileSystem == fs)
            return;

        fs.FilesystemChanged += OnFilesystemChanged;
        _watchedFileSystem = fs;
        _fileSystemSubscribed = true;
    }

    private void RemoveFileSystemSubscription()
    {
        if (_fileSystemSubscribed && IsInstanceValid(_watchedFileSystem))
            _watchedFileSystem.FilesystemChanged -= OnFilesystemChanged;

        _fileSystemSubscribed = false;
        _watchedFileSystem = null;
    }

    private static void FreeNamedChild(Node parent, string name)
    {
        if (!IsInstanceValid(parent))
            return;

        FreeNode(parent.GetNodeOrNull(name));
    }

    private static void FreeNode(Node node)
    {
        if (!IsInstanceValid(node))
            return;

        Node parent = node.GetParent();
        if (IsInstanceValid(parent))
            parent.RemoveChild(node);

        // QueueFree ждёт конца кадра, а выгрузка C# может начаться раньше.
        node.Free();
    }
}
