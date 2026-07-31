using Godot;

/// <summary>
/// Корень приложения. Правил игры не знает — он лишь переключает две взаимоисключающие
/// ветки: главное меню и сессию. Одновременно существует ровно одна из них, поэтому
/// при открытом меню сессии в дереве нет вовсе, а вместе с нею нет ни журнала,
/// ни проекций, ни единой сущности мира.
///
/// Смысл разделения в одной фразе: что умирает вместе — живёт вместе. Сессия — это дерево
/// под <see cref="Session"/>, и уничтожается оно одним движением, вместе с журналом,
/// проекциями, сеткой и всеми сущностями. Никакого «доубрать за прошлой игрой» не остаётся,
/// а значит, и рассинхрону взяться неоткуда.
/// </summary>
public partial class Root : Node
{
    [Export] public PackedScene MainMenuScene;
    [Export] public PackedScene SessionScene;

    /// <summary>Отладка: начинать игру сразу, минуя главное меню.</summary>
    [Export] public bool StartSessionOnLaunch;

    /// <summary>Отладочная клавиша пересборки сессии: ею проверяется, что удаление чисто.</summary>
    [Export] public Key RestartKey = Key.F5;

    /// <summary>Текущая сессия или null, если открыто главное меню.</summary>
    public Session Session { get; private set; }

    /// <summary>Главное меню или null, если идёт игра.</summary>
    public MainMenu Menu { get; private set; }

    public override void _Ready()
    {
        if (StartSessionOnLaunch)
            NewGame();
        else
            OpenMainMenu();
    }

    /// <summary>Новая игра с чистого листа. Меню и прежняя сессия закрываются.</summary>
    public Session NewGame()
    {
        CloseMainMenu();
        CloseSession();

        if (SessionScene == null)
        {
            GD.PushError("[Root] не задана сцена сессии");
            return null;
        }

        Session = SessionScene.Instantiate<Session>();
        AddChild(Session);

        return Session;
    }

    /// <summary>
    /// Перезапуск текущей игры. В главном меню бессмыслен, поэтому там не делает ничего:
    /// перезапускать нечего, а создание сессии втихую увело бы игрока из меню.
    /// </summary>
    public void RestartSession()
    {
        if (Session == null)
            return;

        GD.Print("[Root] перезапуск сессии");
        NewGame();
    }

    /// <summary>Выход в главное меню: сессия удаляется целиком, состояние игры теряется.</summary>
    public void OpenMainMenu()
    {
        CloseSession();

        if (Menu != null)
            return;

        if (MainMenuScene == null)
        {
            GD.PushError("[Root] не задана сцена главного меню");
            return;
        }

        Menu = MainMenuScene.Instantiate<MainMenu>();
        AddChild(Menu);
    }

    public void QuitGame() => GetTree().Quit();

    /// <summary>
    /// Удаление сессии. Сначала вынимаем из дерева, и только потом освобождаем: выход
    /// из дерева происходит здесь же, синхронно, поэтому GameManager успевает отпустить
    /// статическую ссылку на себя ДО того, как на его место встанет менеджер следующей
    /// сессии. Один лишь QueueFree дал бы обратный порядок — и новый корень погасил бы
    /// сам себя.
    /// </summary>
    private void CloseSession()
    {
        if (Session == null)
            return;

        if (IsInstanceValid(Session))
        {
            RemoveChild(Session);
            Session.QueueFree();
        }

        Session = null;
    }

    private void CloseMainMenu()
    {
        if (Menu == null)
            return;

        if (IsInstanceValid(Menu))
        {
            RemoveChild(Menu);
            Menu.QueueFree();
        }

        Menu = null;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || key.Keycode != RestartKey)
            return;

        RestartSession();
        GetViewport().SetInputAsHandled();
    }
}
