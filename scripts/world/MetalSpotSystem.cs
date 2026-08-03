using Godot;

/// <summary>
/// Создание мира в той его части, которая касается метала: один раз расставляет точки
/// и на этом заканчивает работу. Респавна нет и быть не может — точка вечна.
///
/// ПОЧЕМУ ЭТО СИСТЕМА, А НЕ ВЫЗОВ ИЗ СЕССИИ. Расставлять точки можно только тогда, когда
/// стартовая база уже стоит: иначе точки встанут прямо под неё. Стартовую раздачу делает Session
/// в своём _Ready, а он приходит позже _Ready систем, поэтому единственный момент, когда мир
/// заведомо собран, — первый кадр симуляции. Тем же способом это делалось и раньше.
///
/// САМА РАСКЛАДКА СЧИТАЕТСЯ НЕ ЗДЕСЬ, а в <see cref="MetalSpotLayout"/>: тот же код зовёт
/// предпросмотр в редакторе, где ни мира, ни менеджера не существует. Системе остаётся то,
/// чего в редакторе действительно не бывает, — проверка занятости по настоящим препятствиям
/// и создание сущностей.
///
/// Все точки одинаковы по богатству отдельного рудника. Разницу создаёт кластер:
/// на краю карты кластеры крупнее и насыщеннее, ближе к базе — мельче.
/// </summary>
public partial class MetalSpotSystem : GameSystem
{
    /// <summary>Числа раскладки и сид. Не назначено — берутся умолчания класса.</summary>
    [Export] public WorldSettings Settings;

    private bool _seeded;

    protected override void OnRegister()
    {
        if (Settings != null)
            return;

        GD.PushWarning("[MetalSpotSystem] настройки не назначены, взяты умолчания");
        Settings = new WorldSettings();
    }

    public override void Step(double dt)
    {
        if (_seeded || GM.Playground == null)
            return;

        Generate();
        _seeded = true;
    }

    private void Generate()
    {
        var plan = MetalSpotLayout.Build(Settings, Settings.Seed, Free);

        foreach (var position in plan.Spots)
            GM.Spawn.SpawnMetalSpot(position);

        if (plan.Clusters.Count == 0 && (Settings.Rings == null || Settings.Rings.Length == 0))
            GD.PushWarning("[Точки метала] кольца руды не заданы");
        else if (plan.Spots.Count == 0)
            GD.PushWarning("[Точки метала] ни одной точки не размещено. " +
                           "Поле тесное для заданных кластеров и расстояния между точками");
    }

    /// <summary>
    /// Место в границах и свободно.
    ///
    /// Свободным считается место под экстрактор, а не под саму точку: точка ничего
    /// не занимает, но бесполезна там, где экстрактор не встанет.
    /// </summary>
    private bool Free(Vector2 position)
    {
        if (!MetalSpotLayout.InsideWorld(position))
            return false;

        float half = Const.Unit * 0.5f;
        var area = new Rect2(position - new Vector2(half, half), Const.Unit, Const.Unit);

        return !GM.Obstacles.Overlaps(Obb.FromRect(area).Grow(Const.BuildMarginPx));
    }
}
