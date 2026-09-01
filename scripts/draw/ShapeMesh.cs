using System.Collections.Generic;
using Godot;

/// <summary>Форма фигуры служебной графики. Форме соответствует своя сетка со своим шейдером.</summary>
public enum ShapeForm
{
    /// <summary>Отрезок заданной толщины.</summary>
    Line,

    /// <summary>Круг и кольцо: различаются внутренним радиусом.</summary>
    Disc,

    /// <summary>Прямоугольник с заливкой и рамкой.</summary>
    Frame,

    /// <summary>Значок из общего массива изображений.</summary>
    Icon,
}

/// <summary>
/// Служебная графика мира множественными сетками: приёмник фигур и распределитель их
/// по сеткам.
///
/// ЧЕМ ЭТО БЫЛО РАНЬШЕ. Каждое наложение рисовало себя само в собственном <c>_Draw</c>,
/// а окружность строилась разбиением на несколько десятков отрезков. Отсюда следовало, что
/// стоимость росла и с числом наложений, и с числом фигур в каждом: очередь приказов отряда
/// из полусотни машин обходилась в тысячи вершин и в отдельный список команд отрисовки.
/// Здесь фигуры одной формы лежат в общей сетке: геометрия у неё одна на всех, материал
/// общий, и весь мир обходится серверу отрисовки в один вызов на форму.
///
/// ЧТО СЧИТАЕТСЯ ЗДЕСЬ, А ЧТО В ШЕЙДЕРЕ. Здесь — то, что задаёт преобразование: место,
/// поворот, габарит четырёхугольника и перевод экранной толщины в мировую. В шейдере — сама
/// форма: расстояние до центра у круга, расстояние до края у прямоугольника, сглаживание
/// краёв. Поэтому окружность любого радиуса стоит два треугольника, а не полсотни.
///
/// НЕПОСРЕДСТВЕННЫЙ РЕЖИМ, А НЕ ХРАНИМЫЕ ФИГУРЫ. Владелец фигуры объявляет её каждый кадр
/// заново, как и при отрисовке через <see cref="ShapeDraw"/>. Хранимые фигуры с выдачей
/// опознавателя дали бы выигрыш только на неподвижном, а служебная графика подвижна почти
/// вся: точка приказа берётся из положения цели прямо сейчас, и преобразование всё равно
/// переписывается каждый кадр. Взамен непосредственный режим не требует ни освобождения
/// опознавателей, ни уплотнения буфера при удалении.
///
/// ГДЕ ЖИВУТ УЗЛЫ. По набору сеток на слой мира, к которому обратились; заводятся они
/// при первом обращении и складываются в узел-держатель этого слоя. Приказы лежат
/// в <see cref="WorldLayer.Overlay"/>, а метка выделения — под сущностью, в своём слое,
/// поэтому слой и оказался различающим признаком.
/// </summary>
public static class ShapeMesh
{
    /// <summary>
    /// Запас на сглаживание по внешнему краю фигуры, экранные пиксели. Без него край
    /// обводки совпал бы с границей четырёхугольника, и плавный переход обрезался бы
    /// растеризацией.
    /// </summary>
    private const float AaScreen = 1.5f;

    private const float MinLength = 1e-4f;

    private static readonly Dictionary<WorldLayer, Set> Sets = new();

    /// <summary>Масштаб местных единиц холста к экранным пикселям, взятый один раз за кадр.</summary>
    private static float _scale = 1f;

    private static ulong _scaleFrame = ulong.MaxValue;

    /// <summary>
    /// Отрезок. Толщина трактуется по <see cref="ShapeStyle.WidthMode"/> так же, как
    /// в <see cref="ShapeDraw"/>: экранная переводится в мировую по масштабу камеры.
    /// </summary>
    public static void Line(Vector2 from, Vector2 to, in ShapeStyle style,
        WorldLayer layer = WorldLayer.Overlay)
    {
        if (!style.HasStroke)
            return;

        var set = Ensure(layer);

        if (set?.Line == null)
            return;

        var along = to - from;
        float length = along.Length();

        if (length < MinLength)
            return;

        float width = Width(style);
        float margin = AaScreen / _scale;
        float side = width + margin * 2f;

        var dir = along / length;
        var across = new Vector2(-dir.Y, dir.X);

        set.Line.Add(
            new Transform2D(dir * length, across * side, (from + to) * 0.5f),
            style.Stroke,
            new Color(width / side, 0f, 0f, 0f));
    }

    /// <summary>Ломаная. Раскладывается на отрезки: стыков между звеньями сетка не знает.</summary>
    public static void Polyline(IReadOnlyList<Vector2> points, in ShapeStyle style,
        WorldLayer layer = WorldLayer.Overlay)
    {
        if (points == null || points.Count < 2)
            return;

        for (int i = 1; i < points.Count; i++)
            Line(points[i - 1], points[i], style, layer);
    }

    /// <summary>Круг с заливкой и обводкой.</summary>
    public static void Circle(Vector2 center, float radius, in ShapeStyle style,
        WorldLayer layer = WorldLayer.Overlay) =>
        Ring(center, 0f, radius, style, layer);

    /// <summary>
    /// Кольцо между внутренним и внешним радиусом. Заливка занимает полосу между ними,
    /// обводка идёт по обеим границам — так же, как в <see cref="ShapeDraw.Ring"/>.
    /// </summary>
    public static void Ring(Vector2 center, float innerRadius, float outerRadius,
        in ShapeStyle style, WorldLayer layer = WorldLayer.Overlay)
    {
        float inner = Mathf.Max(Mathf.Min(innerRadius, outerRadius), 0f);
        float outer = Mathf.Max(innerRadius, outerRadius);

        if (outer <= 0f)
            return;

        var set = Ensure(layer);

        if (set?.Disc == null)
            return;

        float width = style.HasStroke ? Width(style) : 0f;
        var (tint, fill) = Blend(style);

        if (tint.A <= 0f)
            return;

        // Половина стороны четырёхугольника: внешний радиус, половина обводки за ним
        // и запас на сглаживание
        float half = outer + width * 0.5f + AaScreen / _scale;

        set.Disc.Add(
            new Transform2D(0f, new Vector2(half * 2f, half * 2f), 0f, center),
            tint,
            new Color(outer / half, inner / half, width / half, fill));
    }

    /// <summary>Прямоугольник с заливкой и рамкой. Стороны идут по осям мира.</summary>
    public static void Rect(Rect2 rect, in ShapeStyle style,
        WorldLayer layer = WorldLayer.Overlay) =>
        Box(rect.Position + rect.Size * 0.5f, rect.Size.Abs(), 0f, style, layer);

    /// <summary>
    /// Повёрнутый прямоугольник. Поворот принадлежит преобразованию экземпляра, поэтому
    /// стоит он ровно столько же, сколько неповёрнутый.
    /// </summary>
    public static void Obb(in Obb area, in ShapeStyle style,
        WorldLayer layer = WorldLayer.Overlay)
    {
        if (area.IsEmpty)
            return;

        Box(area.Center, area.Size, area.Angle, style, layer);
    }

    /// <summary>
    /// Общая часть прямоугольных фигур. Поле вокруг прямоугольника отводится под обводку
    /// и под запас на сглаживание, а шейдеру передаётся положение середины обводки и доля,
    /// которую она занимает в этом поле, — см. <c>shape_frame.gdshader</c>.
    /// </summary>
    private static void Box(Vector2 center, Vector2 size, float angle, in ShapeStyle style,
        WorldLayer layer)
    {
        if (size.X <= 0f || size.Y <= 0f)
            return;

        var set = Ensure(layer);

        if (set?.Frame == null)
            return;

        float width = style.HasStroke ? Width(style) : 0f;
        var (tint, fill) = Blend(style);

        if (tint.A <= 0f)
            return;

        // Фигуре без обводки поле не нужно: сглаживать нечего, а края заливки у неё
        // совпадают с краями четырёхугольника
        float margin = width > 0f ? AaScreen / _scale : 0f;
        float around = width + margin * 2f;

        var full = size + Vector2.One * around;
        var border = size / full;

        set.Frame.Add(
            new Transform2D(angle, full, 0f, center),
            tint,
            new Color(border.X, border.Y, fill, width > 0f ? width / around : 0f));
    }

    /// <summary>
    /// Значок вида приказа. Сторона задана экранной величиной из настройки: значок заменяет
    /// собой подпись и читаться должен одинаково на любом приближении камеры.
    /// </summary>
    public static void Icon(Vector2 center, OrderKind kind, Color tint,
        WorldLayer layer = WorldLayer.Overlay)
    {
        var settings = GraphicsSettings.Shapes;

        if (!settings.IconsEnabled || tint.A <= 0f)
            return;

        var set = Ensure(layer);

        if (set?.Icon == null)
            return;

        float side = settings.IconScreenSide / _scale;

        set.Icon.Add(
            new Transform2D(0f, new Vector2(side, side), 0f, center),
            tint,
            new Color(ShapeMeshSettings.Layer(kind), 0f, 0f, 0f));
    }

    /// <summary>
    /// Записать принятое за кадр во все сетки. Зовёт <see cref="ShapeMeshSystem"/> последним
    /// шагом графического цикла: до него объявляются фигуры, после — сеткам уже нечего ждать.
    /// </summary>
    public static void Flush()
    {
        foreach (var set in Sets.Values)
        {
            set.Line?.Flush();
            set.Disc?.Flush();
            set.Frame?.Flush();
            set.Icon?.Flush();
        }
    }

    /// <summary>
    /// Забыть заведённые узлы. Нужно при смене сессии: узлы уходят вместе с миром, а словарь
    /// переживает его и держал бы ссылки на снесённое.
    /// </summary>
    public static void Forget() => Sets.Clear();

    /// <summary>Мировая толщина обводки с учётом режима и масштаба камеры.</summary>
    private static float Width(in ShapeStyle style) => style.WidthMode switch
    {
        WidthMode.World => style.StrokeWidth,
        WidthMode.Screen => style.StrokeWidth / _scale,
        WidthMode.MinScreen => Mathf.Max(style.StrokeWidth, style.StrokeWidth / _scale),
        _ => style.StrokeWidth,
    };

    /// <summary>
    /// Свести заливку и обводку к одному цвету экземпляра и доле. Экземпляр несёт один цвет,
    /// а у фигур зоны заливка и обводка различаются только плотностью при общем оттенке:
    /// цвет берётся от обводки, заливка получается умножением на долю. Фигура без обводки
    /// отдаёт цвет заливки и полную долю.
    /// </summary>
    private static (Color Tint, float Fill) Blend(in ShapeStyle style)
    {
        if (!style.HasStroke)
            return style.HasFill ? (style.Fill, 1f) : (Colors.Transparent, 0f);

        if (!style.HasFill)
            return (style.Stroke, 0f);

        return (style.Stroke, Mathf.Clamp(style.Fill.A / style.Stroke.A, 0f, 1f));
    }

    /// <summary>
    /// Взять набор сеток слоя, заводя узлы при первом обращении, и обновить масштаб холста,
    /// если кадр сменился.
    ///
    /// ЗАВЕДЕНИЕ ИДЁТ ЗДЕСЬ, А НЕ В СИСТЕМЕ, именно ради масштаба: перевод экранной толщины
    /// в мировую нужен уже при объявлении фигуры, то есть раньше, чем система дойдёт
    /// до записи буферов.
    /// </summary>
    private static Set Ensure(WorldLayer layer)
    {
        var playground = GameManager.I?.Playground;

        if (playground == null)
            return null;

        var settings = GraphicsSettings.Shapes;

        if (Sets.TryGetValue(layer, out var set) && Alive.Is(set.Root))
        {
            EnsureIcons(set, settings);
            Refresh(set);
            return set;
        }

        var root = playground.Add(layer, new Node2D { Name = "Shapes" });

        set = new Set { Root = root };

        // Порядок внутри слоя: отрезки ниже фигур зоны, значки поверх всего — значок
        // отмечает точку приказа, и перекрывать его линией очереди нельзя
        set.Line = new ShapeBatch(root, "Lines", settings.LineCoat, zIndex: 0);
        set.Frame = new ShapeBatch(root, "Frames", settings.FrameCoat, zIndex: 1);
        set.Disc = new ShapeBatch(root, "Discs", settings.DiscCoat, zIndex: 2);

        EnsureIcons(set, settings);

        Sets[layer] = set;
        Refresh(set);

        return set;
    }

    /// <summary>
    /// Завести сетку значков, если она нужна и ещё не заведена. Отдельно от прочих форм,
    /// поскольку показ значков выключается настройкой: при выключенном показе сетка
    /// не заводится вовсе, а при включении посреди сессии заводится тут же.
    /// </summary>
    private static void EnsureIcons(Set set, ShapeMeshSettings settings)
    {
        if (!settings.IconsEnabled || set.Icon != null)
            return;

        set.Icon = new ShapeBatch(set.Root, "Icons", settings.IconCoat, zIndex: 3);

        // Массив изображений принадлежит набору значков, а не отдельной сетке, поэтому
        // пишется в материал: сеток значков по одной на слой, и материал у них общий
        if (set.Icon.Node.Material is ShaderMaterial coat)
            coat.SetShaderParameter("icons", settings.Icons);
    }

    /// <summary>Обновить масштаб холста, если кадр сменился. Дважды за кадр он не меняется.</summary>
    private static void Refresh(Set set)
    {
        ulong frame = Engine.GetProcessFrames();

        if (frame == _scaleFrame)
            return;

        _scaleFrame = frame;
        _scale = ShapeDraw.CanvasScale(set.Root);
    }

    /// <summary>Набор сеток одного слоя мира: по сетке на форму.</summary>
    private sealed class Set
    {
        public Node2D Root;
        public ShapeBatch Line;
        public ShapeBatch Disc;
        public ShapeBatch Frame;
        public ShapeBatch Icon;
    }
}
