using Godot;

/// <summary>
/// Раскладки мира по месту: кто где стоит. Держит пространственные сетки и следит за тем,
/// чтобы они были собраны не чаще раза за шаг и не позже первого спроса.
///
/// ПОЧЕМУ РАСКЛАДОК ДВЕ, А НЕ ОДНА. Различаются они не размером корзины, а содержимым:
/// в одной уязвимые (<see cref="IDamageable"/>), в другой подвижные (<see cref="IMobile"/>).
/// Это те же разрезы по признаку, какие ведёт <see cref="Index"/>, только с координатой,
/// и складывать их в одну структуру пришлось бы с пометками о роде — то есть приводить тип
/// на каждом кандидате там, где сейчас его нет вовсе. Размер корзины у обеих один: он выбран
/// под расталкивание, а поиск ближайшего от мелкой корзины не страдает, поскольку прекращается
/// в первых кольцах.
///
/// СОБИРАЕТСЯ ЛЕНИВО. Признак устаревания ставит <see cref="GameManager"/> раз в физический
/// шаг, а сборка происходит при первом обращении. Отсюда следует и то, насколько раскладка
/// отстаёт от действительности: система движения к своему спросу собирает её сама, а бой
/// спрашивает уже после движения и видит положения начала шага, разойдясь с ними на путь
/// одного кадра. Для выбора цели это безразлично — запас в одно кольцо покрывает такой сдвиг
/// с избытком.
/// </summary>
public sealed class WorldSpace
{
    /// <summary>
    /// Сторона корзины. Вдвое больше ячейки навигации: окрестность три на три покрывает
    /// радиус чутья при локальном обходе, ради которого размер и подбирался.
    /// </summary>
    public const int BucketPx = Const.Unit * 2;

    private readonly Index _index;

    private bool _targetsStale = true;
    private bool _mobilesStale = true;

    public WorldSpace(Index index)
    {
        _index = index;
        Targets = new SpatialGrid<IDamageable>(BucketPx);
        Mobiles = new SpatialGrid<IMobile>(BucketPx);
    }

    /// <summary>Уязвимые по месту: по ним идёт выбор цели и урон по области.</summary>
    public SpatialGrid<IDamageable> Targets { get; }

    /// <summary>Подвижные по месту: по ним идёт локальный обход и расталкивание.</summary>
    public SpatialGrid<IMobile> Mobiles { get; }

    /// <summary>Начался новый шаг: раскладки собраны для прошлого и больше не годятся.</summary>
    public void Invalidate()
    {
        _targetsStale = true;
        _mobilesStale = true;
    }

    /// <summary>Раскладка уязвимых, собранная в этом шаге.</summary>
    public SpatialGrid<IDamageable> ReadyTargets()
    {
        if (!_targetsStale)
            return Targets;

        _targetsStale = false;
        Targets.Clear();

        foreach (var target in _index.All<IDamageable>())
        {
            // Крупная цель кладётся во все корзины своего прямоугольника: иначе кольцевой
            // обход дошёл бы до её края раньше, чем до корзины центра, и не увидел ничего
            if (target is IFootprint { Footprint.IsEmpty: false } shaped)
                Targets.AddArea(target, shaped.Footprint, target.Faction);
            else
                // Радиус попадания и есть то, насколько поверхность цели отстоит от середины:
                // по нему поиск досягаемых назначает предел обхода
                Targets.Add(target, target.GlobalPosition, target.Faction, target.HitRadius);
        }

        return Targets;
    }

    /// <summary>Раскладка подвижных, собранная в этом шаге.</summary>
    public SpatialGrid<IMobile> ReadyMobiles()
    {
        if (!_mobilesStale)
            return Mobiles;

        _mobilesStale = false;
        Mobiles.Clear();

        foreach (var mobile in _index.All<IMobile>())
            Mobiles.Add(mobile, mobile.GlobalPosition, mobile.Faction, mobile.HitRadius);

        return Mobiles;
    }

    /// <summary>Сводка для отладочной панели и снимка состояния.</summary>
    public string Summary() =>
        $"цели {Targets.Count} в {Targets.FilledAt.Count} кл., " +
        $"подвижные {Mobiles.Count} в {Mobiles.FilledAt.Count} кл.";
}
