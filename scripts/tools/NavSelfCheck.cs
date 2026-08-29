using System.Collections.Generic;
using System.Diagnostics;
using Godot;

/// <summary>
/// Прогон навигации на искусственном поле: стена с проходом, тупик, замкнутая область.
///
/// ЗАЧЕМ ОТДЕЛЬНАЯ СЦЕНА. Полоса областей, выборочная отмена путей и связность проверяются
/// только тогда, когда на поле есть здания и кто-то идёт мимо них, а в партии это работа
/// игрока. Сцена ставит те же вопросы поиску напрямую и печатает ответы в журнал, поэтому
/// правку навигации можно оценить, не играя. Запускается как отдельная сцена
/// (<c>scenes/tools/NavSelfCheck.tscn</c>) и по окончании закрывает окно.
///
/// Поле берётся размером с боевое: на мелком поле тайлов вдвое меньше ширины полосы,
/// и судить по нему о расходе узлов нельзя.
/// </summary>
public partial class NavSelfCheck : Node2D
{
    private sealed class Wall : IObstacle
    {
        private readonly Obb _shape;

        public Wall(Rect2 rect) => _shape = Obb.FromRect(rect);

        public Obb Footprint => _shape;
    }

    private ObstacleMap _obstacles;
    private NavGrid _nav;
    private PathSearch _search;

    private readonly List<Vector2> _points = new();
    private readonly List<int> _tiles = new();

    private int _waited;

    public override void _Ready()
    {
        World.Settings = new WorldSettings { Radius = 64 };

        _obstacles = new ObstacleMap();
        _nav = new NavGrid(_obstacles);
        _search = new PathSearch(_nav);

        float unit = Const.Unit;

        // Стена поперёк поля с проходом посередине верхней половины
        Add(new Rect2(-60f * unit, -8f * unit, 58f * unit, 2f * unit));
        Add(new Rect2(2f * unit, -8f * unit, 58f * unit, 2f * unit));

        // Тупик: карман, открытый только сверху, — прежняя оценка заводила поиск в него
        Add(new Rect2(-6f * unit, 10f * unit, 1f * unit, 14f * unit));
        Add(new Rect2(6f * unit, 10f * unit, 1f * unit, 14f * unit));
        Add(new Rect2(-6f * unit, 24f * unit, 13f * unit, 1f * unit));

        // Замкнутая коробка: цель внутри недостижима
        Add(new Rect2(30f * unit, 30f * unit, 6f * unit, 1f * unit));
        Add(new Rect2(30f * unit, 36f * unit, 6f * unit, 1f * unit));
        Add(new Rect2(30f * unit, 30f * unit, 1f * unit, 7f * unit));
        Add(new Rect2(35f * unit, 30f * unit, 1f * unit, 7f * unit));

        // Плотная застройка: сетка построек два на два с проходами в клетку — то, во что
        // превращается база к середине партии, и то, где поиск упирался в потолок узлов
        for (int gx = 0; gx < 10; gx++)
        {
            for (int gy = 0; gy < 10; gy++)
            {
                float x = (-52f + gx * 3f) * unit;
                float y = (-52f + gy * 3f) * unit;
                Add(new Rect2(x, y, 2f * unit, 2f * unit));
            }
        }

        // Гребёнка тупиков вдоль пути: место, где прежняя оценка расходовала тысячи узлов
        for (int i = 0; i < 6; i++)
        {
            float x = (-24f + i * 8f) * unit;
            Add(new Rect2(x, 14f * unit, 1f * unit, 16f * unit));
            Add(new Rect2(x + 6f * unit, 14f * unit, 1f * unit, 16f * unit));
            Add(new Rect2(x, 30f * unit, 7f * unit, 1f * unit));
        }

        GD.Print($"[NavSelfCheck] поле {NavGrid.Width}×{NavGrid.Width} ячеек, " +
                 $"тайлов {NavGrid.TilesPerSide}×{NavGrid.TilesPerSide}, препятствий {_obstacles.Count}");
    }

    private void Add(Rect2 rect) => _obstacles.Add(new Wall(rect));

    public override void _Process(double delta)
    {
        _nav.Poll();

        if (_nav.Active == null || _nav.ActiveRevision != _obstacles.Revision)
        {
            if (++_waited > 600)
            {
                GD.PrintErr("[NavSelfCheck] снимок не опубликован");
                GetTree().Quit();
            }

            return;
        }

        SetProcess(false);
        Run();
        GetTree().Quit();
    }

    private void Run()
    {
        float unit = Const.Unit;
        float radius = 22f;

        GD.Print($"[NavSelfCheck] снимок за {_nav.LastBuildMs:0.00} мс, " +
                 $"тайлов пересчитано {_nav.LastRebuiltTiles}");

        var layer = _nav.Layer(radius);
        GD.Print(layer == null
            ? "[NavSelfCheck] слой областей не построен: порог только что запомнен"
            : $"[NavSelfCheck] областей {layer.Count}");

        // Порог мог появиться только что: даём пересборке пройти
        while (layer == null)
        {
            _nav.Poll();
            layer = _nav.Layer(radius);
        }

        // Через стену: прямой видимости нет, обход идёт через проход посередине
        Compare("через стену", new Vector2(-20f * unit, -20f * unit), new Vector2(20f * unit, 6f * unit), radius);

        // Вдоль гребёнки тупиков
        Compare("мимо тупиков", new Vector2(-40f * unit, 20f * unit),
            new Vector2(40f * unit, 20f * unit), radius);

        // Внутрь плотной застройки
        Compare("в плотную застройку", new Vector2(-56f * unit, -20f * unit),
            new Vector2(-38f * unit, -38f * unit), radius);

        // Цель внутри замкнутой коробки
        Compare("в коробку", new Vector2(0f, 0f), new Vector2(32.5f * unit, 33.5f * unit), radius);

        GD.Print($"[NavSelfCheck] полос {_search.Bands}, повторов без полосы {_search.Fallbacks}");

        CheckStamps(radius);
    }

    /// <summary>Один и тот же запрос в четырёх сочетаниях приёмов: сравнение расхода узлов.</summary>
    private void Compare(string caption, Vector2 from, Vector2 to, float radius)
    {
        Measure(caption + ": плоский поиск", from, to, radius, false, false);
        Measure(caption + ": оценка по графу", from, to, radius, false, true);
        Measure(caption + ": полоса", from, to, radius, true, false);
        Measure(caption + ": полоса и оценка", from, to, radius, true, true);
    }

    private void Measure(string caption, Vector2 from, Vector2 to, float radius,
        bool band, bool guidance)
    {
        _search.UseBand = band;
        _search.UseGuidance = guidance;

        var timer = Stopwatch.StartNew();
        // Потолок нарочно велик: сравнивать нужно расход узлов, а не факт упирания в предел
        bool found = _search.TryFind(from, to, radius, 200000, _points);
        timer.Stop();

        GD.Print($"[NavSelfCheck] {caption}: {(found ? "путь" : "нет пути")}, " +
                 $"узлов {_search.LastExpanded}, точек {_points.Count}, " +
                 $"областей в полосе {_search.LastBand}, " +
                 $"звеньев цепочки {_search.LastMacro?.Chain.Count ?? 0}, " +
                 $"{timer.Elapsed.TotalMilliseconds:0.00} мс");
    }

    /// <summary>
    /// Выборочная отмена: постройка на маршруте обязана тронуть его тайлы, постройка
    /// в стороне — не обязана.
    /// </summary>
    private void CheckStamps(float radius)
    {
        float unit = Const.Unit;
        var from = new Vector2(0f, -20f * unit);
        var to = new Vector2(0f, 6f * unit);

        _search.UseBand = true;
        _search.UseGuidance = true;

        if (!_search.TryFind(from, to, radius, 200000, _points))
        {
            GD.PrintErr("[NavSelfCheck] маршрут для проверки меток не найден");
            return;
        }

        _tiles.Clear();
        var previous = from;

        foreach (var point in _points)
        {
            NavGrid.TilesAlong(previous, point, _tiles);
            previous = point;
        }

        int revision = _nav.Revision;

        Add(new Rect2(40f * unit, 40f * unit, 2f * unit, 2f * unit));
        _nav.Poll();
        bool far = _nav.Touched(_tiles, revision);

        Add(new Rect2(-1f * unit, -2f * unit, 2f * unit, 2f * unit));
        _nav.Poll();
        bool near = _nav.Touched(_tiles, revision);

        GD.Print($"[NavSelfCheck] тайлов у маршрута {_tiles.Count}; " +
                 $"постройка в стороне тронула путь: {far}; постройка на пути: {near}");
    }
}
