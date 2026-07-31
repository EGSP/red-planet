using Godot;

/// <summary>
/// Корень приложения. Правил игры не знает — он лишь создаёт и удаляет сессию
/// целиком, а сам живёт от запуска до выхода. Отсюда со временем вырастут меню, настройки
/// и выбор карты; сеть тоже причалит сюда, потому что подключение переживает сессию.
///
/// Смысл разделения в одной фразе: что умирает вместе — живёт вместе. Сессия — это дерево
/// под <see cref="Session"/>, и уничтожается оно одним движением, вместе с журналом,
/// проекциями, сеткой и всеми сущностями. Никакого «доубрать за прошлой игрой» не остаётся,
/// а значит, и рассинхрону взяться неоткуда.
/// </summary>
public partial class Root : Node
{
    [Export] public PackedScene SessionScene;

    /// <summary>Начинать сессию сразу при запуске. Появится меню — снимем.</summary>
    [Export] public bool StartOnLaunch = true;

    /// <summary>Отладочная клавиша пересборки сессии: ею и проверяется, что снос чист.</summary>
    [Export] public Key RestartKey = Key.F5;

    /// <summary>Текущая сессия или null, если её сейчас нет.</summary>
    public Session Current { get; private set; }

    public override void _Ready()
    {
        if (StartOnLaunch)
            Start();
    }

    /// <summary>Новая сессия с чистого листа. Прежняя, если была, сносится до создания новой.</summary>
    public Session Start()
    {
        Stop();

        if (SessionScene == null)
        {
            GD.PushError("[Root] не задана сцена сессии");
            return null;
        }

        Current = SessionScene.Instantiate<Session>();
        AddChild(Current);

        return Current;
    }

    /// <summary>
    /// Снос сессии. Сначала вынимаем из дерева, и только потом освобождаем: выход из дерева
    /// происходит здесь же, синхронно, поэтому GameManager успевает отпустить статическую
    /// ссылку на себя ДО того, как на его место встанет менеджер следующей сессии.
    /// Один лишь QueueFree дал бы обратный порядок — и новый корень погасил бы сам себя.
    /// </summary>
    public void Stop()
    {
        if (Current == null)
            return;

        if (IsInstanceValid(Current))
        {
            RemoveChild(Current);
            Current.QueueFree();
        }

        Current = null;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || key.Keycode != RestartKey)
            return;

        GD.Print("[Root] перезапуск сессии");
        Start();
        GetViewport().SetInputAsHandled();
    }
}
