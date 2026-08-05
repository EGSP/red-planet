using Godot;

/// <summary>Чем кончилась партия. None — ещё идёт.</summary>
public enum SessionOutcome
{
    None,
    Victory,
    Defeat,
}

/// <summary>
/// Сессия — корень одной игры. Держит три ветки с разной ответственностью и разным
/// направлением зависимостей:
///
///   Systems     — GameManager и его системы: журнал, проекции, индекс, сетка, порядок кадра.
///   Playground  — мир: всё видимое и физическое, разложенное по слоям отрисовки.
///   View        — наблюдатели: камера и интерфейс. Читают всё, не читает их никто.
///
/// Порядок веток в дереве значим. Systems стоит первым, чтобы планировщик и разрезы
/// собрались раньше, чем проснётся хоть один объект мира; View — последним, потому что
/// ему нужно готовое состояние. Сам же Session просыпается после всех своих детей,
/// и потому стартовая раздача мира происходит здесь и в самом конце.
/// </summary>
public partial class Session : Node2D
{
    [Export] public GameManager Systems;
    [Export] public Playground Playground;

    /// <summary>
    /// Отладочная выдача на старте: с ней можно смотреть поздние стадии, не дожидаясь,
    /// пока раскрутится добыча. Публикуется документом, а не правкой проекции — запас меняется
    /// только через журнал, и у выдачи есть след.
    /// </summary>
    [Export] public float StartingMetal;

    [Export] public float StartingEnergy;

    /// <summary>Состояние паузы сменилось. На него подписан <see cref="PauseMenu"/>.</summary>
    [Signal] public delegate void PauseChangedEventHandler(bool paused);

    /// <summary>Партия завершилась победой или поражением. На него подписан <see cref="OutcomeMenu"/>.</summary>
    [Signal] public delegate void OutcomeChangedEventHandler(int outcome);

    /// <summary>Идёт ли пауза. Останавливается только симуляция, не весь кадр.</summary>
    public bool Paused { get; private set; }

    /// <summary>Исход партии. После End симуляция стоит, и снять паузу уже нельзя.</summary>
    public SessionOutcome Outcome { get; private set; }

    /// <summary>
    /// Пауза выключает обработку у ветки Systems, и этого достаточно: и физический, и
    /// графический циклы планировщика зовутся из колбэков композиционного корня, а логика
    /// систем живёт под ним. Заодно к системам перестаёт поступать ввод, поэтому на паузе
    /// нельзя ни отдать приказ, ни поставить постройку.
    ///
    /// Почему не GetTree().Paused: остановка всего дерева заморозила бы и наблюдателей.
    /// Здесь же камера продолжает слушаться WASD и колеса, и поле боя на паузе можно
    /// осмотреть — для стратегии это осмысленно, а мир при этом стоит намертво.
    /// </summary>
    public void SetPaused(bool paused)
    {
        // Завершённую партию снова не запускаем: меню исхода не «продолжить»
        if (Outcome != SessionOutcome.None)
            return;

        if (Paused == paused || Systems == null)
            return;

        Paused = paused;
        Systems.ProcessMode = paused ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;

        EmitSignal(SignalName.PauseChanged, paused);
    }

    public void TogglePause() => SetPaused(!Paused);

    /// <summary>
    /// Завершить партию. Симуляция останавливается навсегда для этой сессии; меню исхода
    /// предлагает перезапуск или выход. Повторный вызов ничего не делает: исход один.
    /// </summary>
    public void End(SessionOutcome outcome)
    {
        if (Outcome != SessionOutcome.None || outcome == SessionOutcome.None)
            return;

        Outcome = outcome;

        if (Systems != null)
        {
            Paused = true;
            Systems.ProcessMode = ProcessModeEnum.Disabled;
            EmitSignal(SignalName.PauseChanged, true);
        }

        EmitSignal(SignalName.OutcomeChanged, (int)outcome);
    }

    public override void _Ready()
    {
        Systems ??= GetNodeOrNull<GameManager>("Systems");
        Playground ??= GetNodeOrNull<Playground>("Playground");

        if (Systems == null || Playground == null)
        {
            GD.PushError("[Session] в сцене сессии не хватает Systems или Playground");
            return;
        }

        SpawnBase();
        SpawnCommander();
        GrantStartingResources();
    }

    private void GrantStartingResources()
    {
        var events = Systems.Events;

        if (StartingMetal > 0f)
            events.Append(new ResourceGained { Kind = ResourceKind.Metal, Amount = StartingMetal });

        if (StartingEnergy > 0f)
            events.Append(new ResourceGained { Kind = ResourceKind.Energy, Amount = StartingEnergy });
    }

    private void SpawnBase()
    {
        var def = Systems.Catalog.Unit("fortress");
        if (def == null)
        {
            GD.PushWarning("[Session] нет определения постройки fortress");
            return;
        }

        Systems.Spawn.SpawnBuilding(def, Const.LandingPoint);
    }

    private void SpawnCommander()
    {
        var def = Systems.Catalog.Unit("commander");
        if (def == null)
        {
            GD.PushWarning("[Session] нет определения коммандера");
            return;
        }

        var commander = Systems.Spawn.SpawnUnit(def, Const.CellCenter(new Vector2I(3, 0)));

        // Коммандер сразу лежит в первой группе. Приказы уходят только выделенным,
        // поэтому без этого начало игры требовало бы сперва найти его глазами
        // и щёлкнуть; с готовой группой довольно нажать единицу
        Systems.Command?.Groups.Assign(0, new IOrderable[] { commander });
    }
}
