using Godot;

/// <summary>
/// Слои мира. Порядок объявления и есть порядок отрисовки: что ниже в списке — то выше
/// на экране. Раньше эту роль исполняли ZIndex, расставленные вручную в трёх разных файлах
/// (снаряд знал про шестёрку, призрак постройки — про десятку), и добавление нового рода
/// сущностей означало угадывание свободного числа.
///
/// ПОРЯДОК ПОДКРЕПЛЁН ПОЛОСАМИ <c>z</c>. Одного положения в дереве оказалось мало: сервер
/// отрисовки строит очередь команд по уровням <c>z</c> для ВСЕГО холста сразу, а не внутри
/// каждого узла, поэтому картинка с ненулевым <c>z</c> выходила из своего слоя и ложилась
/// поверх последующих — ствол машины, объявленный единицей, рисовался над туманом войны
/// и над служебной графикой. Теперь каждому слою отведена полоса шириной
/// <see cref="Playground.Span"/>, внутри которой изображение сущности раскладывается
/// по уровням ради объединения вызовов отрисовки — см. <see cref="MaterialOrderBalancer"/>.
/// </summary>
public enum WorldLayer
{
    /// <summary>Земля, сетка застройки, границы карты.</summary>
    Terrain = 0,

    /// <summary>Месторождения — лежат под всем, что на них ставят.</summary>
    Deposits = 1,

    /// <summary>
    /// Тень препятствий. Лежит на земле и на месторождениях, но под тем, что тень отбрасывает,
    /// и под всем, что ходит: юнит закрывается тенью ни при каких настройках.
    /// </summary>
    Shadows = 2,

    /// <summary>
    /// Наземные эффекты: пыль из-под корпуса, следы, копоть. Лежат на земле и на тени,
    /// но под всем, что по земле ходит и стоит, поскольку поднимаются они от грунта,
    /// а не над машиной.
    /// </summary>
    GroundEffects = 3,

    /// <summary>Постройки и каркасы.</summary>
    Structures = 4,

    /// <summary>Всё, что ходит: юниты игрока и враги.</summary>
    Actors = 5,

    Projectiles = 6,

    /// <summary>
    /// Эффекты выше земли: попадания, взрывы, дым. Лежат поверх снарядов, поскольку
    /// показывают их конец, но под облаками и туманом войны.
    /// </summary>
    AirEffects = 7,

    /// <summary>
    /// Облака и их тень. Лежат поверх всего, что стоит и ходит по земле, поскольку тень
    /// падает сверху, но под туманом войны и служебной графикой.
    /// </summary>
    Clouds = 8,

    /// <summary>
    /// Туман войны. Лежит поверх мира, но под служебной графикой: приказы, призрак постройки
    /// и отладочные выкладки закрываться туманом не должны — они принадлежат не миру,
    /// а тому, кто на мир смотрит.
    /// </summary>
    Fog = 9,

    /// <summary>
    /// Служебная графика поверх мира: призрак постройки, отрисовка приказов, отладочные
    /// выкладки. Миру не принадлежит и потому эффектом не является — эффекты мира лежат
    /// в <see cref="GroundEffects"/>.
    /// </summary>
    Overlay = 10,
}

/// <summary>
/// Игровая площадка — контейнер всего видимого и физического. Сюда и только сюда
/// Spawner складывает рождённые сущности, разводя их по слоям.
///
/// Логики здесь нет намеренно: площадка ничего не считает и ни на что не подписана,
/// она лишь даёт миру структуру. Тем самым она безболезненно переживает подмену —
/// если однажды понадобится вид без симуляции (реплей, клиент в сетевой игре),
/// заменяется ровно эта ветка дерева.
/// </summary>
public partial class Playground : Node2D
{
    [Export] public Node2D Terrain;
    [Export] public Node2D Deposits;
    [Export] public Node2D Shadows;
    [Export] public Node2D GroundEffects;
    [Export] public Node2D Structures;
    [Export] public Node2D Actors;
    [Export] public Node2D Projectiles;
    [Export] public Node2D AirEffects;
    [Export] public Node2D Clouds;
    [Export] public Node2D Fog;
    [Export] public Node2D Overlay;

    private Node2D[] _layers;

    public override void _EnterTree() => Resolve();

    /// <summary>
    /// Узел слоя. Если слой в сцене не завели, отдаём саму площадку: мир от этого
    /// потеряет порядок отрисовки, но не сущность — она всё равно окажется в дереве.
    /// </summary>
    public Node2D Layer(WorldLayer layer)
    {
        if (_layers == null)
            Resolve();

        return _layers[(int)layer] ?? this;
    }

    /// <summary>Положить сущность в мир, в свой слой.</summary>
    public T Add<T>(WorldLayer layer, T node) where T : Node
    {
        Layer(layer).AddChild(node);
        return node;
    }

    private void Resolve()
    {
        Terrain ??= GetNodeOrNull<Node2D>(nameof(Terrain));
        Deposits ??= GetNodeOrNull<Node2D>(nameof(Deposits));
        Shadows ??= GetNodeOrNull<Node2D>(nameof(Shadows));
        GroundEffects ??= GetNodeOrNull<Node2D>(nameof(GroundEffects));
        Structures ??= GetNodeOrNull<Node2D>(nameof(Structures));
        Actors ??= GetNodeOrNull<Node2D>(nameof(Actors));
        Projectiles ??= GetNodeOrNull<Node2D>(nameof(Projectiles));
        AirEffects ??= GetNodeOrNull<Node2D>(nameof(AirEffects));
        Clouds ??= GetNodeOrNull<Node2D>(nameof(Clouds));
        Fog ??= GetNodeOrNull<Node2D>(nameof(Fog));
        Overlay ??= GetNodeOrNull<Node2D>(nameof(Overlay));

        _layers = new[]
        {
            Terrain, Deposits, Shadows, GroundEffects, Structures, Actors, Projectiles, AirEffects,
            Clouds, Fog, Overlay,
        };

        for (int i = 0; i < _layers.Length; i++)
            Band(_layers[i], (WorldLayer)i);
    }

    /// <summary>
    /// Отвести слою его полосу <c>z</c>. Признак относительности снимается: полоса задаёт
    /// место слоя в общей очереди холста, и складывать её с <c>z</c> самой площадки нечего.
    /// </summary>
    private static void Band(Node2D layer, WorldLayer which)
    {
        if (layer == null)
            return;

        layer.ZAsRelative = false;
        layer.ZIndex = BandZ(which);
    }

    /// <summary>
    /// Ширина полосы <c>z</c>, отведённой одному слою мира. Внутри полосы изображение
    /// сущности разложено по уровням, и все уровни всех видов лежат в общей раскладке,
    /// поэтому величина взята с запасом на рост числа видов.
    ///
    /// Одиннадцать слоёв по этой ширине укладываются в пределы <c>z</c>, допустимые сервером
    /// отрисовки (±4096), с запасом.
    /// </summary>
    public const int Span = 512;

    /// <summary>
    /// <c>z</c> пометок поверх изображения: полосы прочности, стрелок выездов, подсветки.
    /// Взят у верхнего края полосы, поскольку пометки обязаны лежать выше всех картинок
    /// своего слоя — см. <see cref="ModelLayer"/>.
    /// </summary>
    public const int MarksZ = Span - 2;

    /// <summary>
    /// Нижняя граница полосы слоя. Отсчёт ведётся от <see cref="WorldLayer.Actors"/>: слои
    /// расходятся в обе стороны, и весь набор укладывается в пределы <c>z</c>, допустимые
    /// сервером отрисовки.
    /// </summary>
    public static int BandZ(WorldLayer layer) =>
        ((int)layer - (int)WorldLayer.Actors) * Span;
}
