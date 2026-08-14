using Godot;

/// <summary>
/// Плагин вкладки главного экрана «Контент».
///
/// ПОЧЕМУ MAIN SCREEN, А НЕ DOCK. Редактору нужны каталог, поле сравнения и форма
/// одновременно; узкая боковая панель для этого тесна. Вкладка рядом с 2D/3D/Script
/// даёт полную ширину и не перекрывает инспектор сцены.
///
/// ЖИЗНЕННЫЙ ЦИКЛ. Control создаётся в _EnterTree и остаётся потомком главного экрана
/// до выгрузки плагина. Видимость переключает Godot через _MakeVisible. Первая загрузка
/// каталога откладывается до первого показа: нет смысла читать все .toml, пока вкладку
/// не открыли.
///
/// ВНЕШНИЕ ПРАВКИ. Подписка на EditorFileSystem.FilesystemChanged ловит сохранение файла
/// внутри Godot. Возврат фокуса окна и повторный показ вкладки ловят правку во внешнем
/// редакторе (Cursor и т.п.), которую FilesystemChanged может не прислать сразу.
/// </summary>
[Tool]
public partial class ContentEditorPlugin : EditorPlugin
{
    private const string FloatingWindowName = "RedPlanetContentEditorWindow";

    private ContentEditorMain _main;
    private Window _floatingWindow;
    private EditorFileSystem _watchedFileSystem;
    private bool _fileSystemSubscribed;
    private bool _floatingWindowCloseSubscribed;
    private bool _active;

    public override void _EnterTree()
    {
        _active = true;
        EnsureMain();
        EnsureFileSystemSubscription();
    }

    public override void _ExitTree()
    {
        _active = false;
        RemoveFileSystemSubscription();

        if (IsMainValid())
        {
            _main.FloatingModeToggleRequested -= ToggleFloatingMode;
            _main.Shutdown();

            if (_main.GetParent() != null)
                _main.GetParent().RemoveChild(_main);
            // QueueFree исполняется в конце кадра, а выгрузка C# может начаться раньше.
            // Синхронное освобождение удаляет все signal delegates до unload assembly.
            _main.Free();
        }

        if (IsInstanceValid(_floatingWindow))
        {
            RemoveFloatingWindowSubscription();
            _floatingWindow.Free();
        }

        _main = null;
        _floatingWindow = null;
    }

    public override bool _HasMainScreen() => true;

    public override void _MakeVisible(bool visible)
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

    public override string _GetPluginName() => "Content";

    public override Texture2D _GetPluginIcon()
    {
        var editor = EditorInterface.Singleton;
        var baseControl = editor?.GetBaseControl();
        return IsInstanceValid(baseControl)
            ? baseControl.GetThemeIcon("ResourcePreloader", "EditorIcons")
            : null;
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
    /// Получить целый главный экран либо создать новый. После перезагрузки C# поле _main
    /// может обнулиться, хотя нативный дочерний Control ещё существует. Поиск по имени
    /// позволяет повторно использовать его; узел иного типа удаляется как остаточный.
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

        // После перезагрузки C# отдельное нативное окно может остаться в дереве,
        // хотя поля нового managed-экземпляра плагина обнулены.
        _floatingWindow = baseControl.GetNodeOrNull<Window>(FloatingWindowName);
        var floatingMain = IsInstanceValid(_floatingWindow)
            ? _floatingWindow.GetNodeOrNull<ContentEditorMain>(ContentEditorMain.EditorNodeName)
            : null;

        if (IsInstanceValid(floatingMain)
            && floatingMain.Lifecycle != ContentEditorLifecycle.Disposed)
        {
            _main = floatingMain;
        }
        else
        {
            if (IsInstanceValid(_floatingWindow))
            {
                RemoveFloatingWindowSubscription();
                _floatingWindow.QueueFree();
                _floatingWindow = null;
            }

            Node existing = screen.GetNodeOrNull(ContentEditorMain.EditorNodeName);
            if (existing is ContentEditorMain contentMain
            && IsInstanceValid(contentMain)
            && contentMain.Lifecycle != ContentEditorLifecycle.Disposed)
            {
                _main = contentMain;
            }
            else
            {
                if (IsInstanceValid(existing))
                {
                    screen.RemoveChild(existing);
                    existing.QueueFree();
                }

                _main = new ContentEditorMain
                {
                    Name = ContentEditorMain.EditorNodeName,
                    Visible = false,
                };
                screen.AddChild(_main);
            }
        }

        // EditorMainScreen не является Container для дочернего Control плагина.
        _main.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        SubscribeMain();
        EnsureFloatingWindowSubscription();
        _main.SetFloatingMode(IsFloating());
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
}
