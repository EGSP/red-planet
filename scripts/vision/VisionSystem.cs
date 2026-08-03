using Godot;

/// <summary>
/// Сводит поля зрения стороны игрока в один растр и держит в согласии с ним отрисовку:
/// туман войны поверх карты и скрытие сущностей противника вне видимой области.
///
/// ПОЧЕМУ СИСТЕМА, А НЕ ЛЕНИВАЯ КАРТА ПО ОБРАЗЦУ <see cref="NavGrid"/>. Растр навигации
/// выводится из прямоугольников зданий и потому пересобирается по изменению источника —
/// несколько раз в секунду в самом плотном случае. Источники зрения подвижны, и признака
/// «ничего не менялось» у них не бывает вовсе, поэтому пересчёт идёт по таймеру, а таймер
/// нужно кому-то вести. Этим и занимается система.
///
/// НА СИМУЛЯЦИЮ НЕ ВЛИЯЕТ. Скрытие сущности есть <c>Visible</c> у её узла, то есть чистая
/// отрисовка: цель по-прежнему выбирается, приказ по-прежнему отдаётся, урон по-прежнему
/// проходит. Превращение зрения в игровую механику потребует правки выбора целей,
/// наведения приказов и выделения — и делаться должно отдельно.
///
/// ИДЁТ В ГРАФИЧЕСКОМ ЦИКЛЕ. Растр описывает не состояние мира, а его вид, и потому
/// собирается по тем же положениям, по которым в этом кадре рисуются сами сущности.
/// В физическом цикле при несовпадении частот граница тумана отставала бы от юнитов
/// на переменную величину, и движение её читалось бы как дрожание.
/// </summary>
public partial class VisionSystem : GameSystem
{
    /// <summary>Настройки отображения. Их же правит панель отладки.</summary>
    [Export] public FogSettings Settings;

    /// <summary>Растр видимости стороны игрока.</summary>
    public VisionField Field { get; } = new();

    private FogRenderer _renderer;

    /// <summary>Сколько осталось до следующей пересборки растра, секунд.</summary>
    private float _cooldown;

    /// <summary>Скрывали ли противника в прошлом шаге: выключение тоже требует прохода.</summary>
    private bool _hiding;

    /// <summary>Сколько сущностей противника скрыто сейчас. Показывает панель отладки.</summary>
    public int Hidden { get; private set; }

    protected override void OnRegister() =>
        // Настройки в сцене могли и не назначить: без них система должна работать
        // на умолчаниях, а не ронять сессию
        Settings ??= new FogSettings();

    public override void Step(double dt)
    {
        EnsureNodes();
        UpdateField(dt);
        ApplyVisibility();
    }

    private void EnsureNodes()
    {
        if (_renderer != null && IsInstanceValid(_renderer))
            return;

        _renderer = GM.Playground.Add(WorldLayer.Fog, new FogRenderer());
        _renderer.Field = Field;
        _renderer.Settings = Settings;
    }

    /// <summary>
    /// Пересобрать растр, если подошёл срок либо изменился размер мира. Второе условие
    /// важнее первого: после правки настроек мира массив уже другой длины, и ждать
    /// с ним до конца отсчёта значило бы показывать растр от прежней карты.
    /// </summary>
    private void UpdateField(double dt)
    {
        Field.Resize(Settings.CellPx);

        bool resized = Field.Fit();

        _cooldown -= (float)dt;

        if (!Settings.EveryFrame && _cooldown > 0f && !resized)
            return;

        _cooldown = 1f / Mathf.Max(Settings.UpdateHz, 1f);

        Field.Rebuild(GM.Index.All<IVision>(), Faction.Player);
    }

    /// <summary>
    /// Скрыть противника вне поля зрения. Проход делается и в том шаге, когда скрытие
    /// выключили: иначе скрытое так и осталось бы невидимым.
    /// </summary>
    private void ApplyVisibility()
    {
        bool hide = Settings.HideEnemies;

        if (!hide && !_hiding)
            return;

        _hiding = hide;

        int hidden = 0;

        foreach (var unit in GM.Units[Faction.Hostile])
        {
            bool visible = !hide || Field.IsVisible(unit.GlobalPosition);
            unit.Visible = visible;

            if (!visible)
                hidden++;
        }

        // Снаряды противника скрываются вместе с ним: летящий из пустоты выстрел выдавал бы
        // положение стрелка вернее, чем сам стрелок. Сторона снаряда названа целью, поэтому
        // выпущенный противником — тот, чья цель игрок
        foreach (var projectile in GM.Index.All<Projectile>())
        {
            if (projectile.TargetSide != Faction.Player)
                continue;

            bool visible = !hide || Field.IsVisible(projectile.GlobalPosition);
            projectile.Visible = visible;

            if (!visible)
                hidden++;
        }

        Hidden = hidden;
    }
}
