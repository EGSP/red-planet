using System.Collections.Generic;
using Godot;

/// <summary>
/// Полосы прочности всего мира одной множественной сеткой.
///
/// ЧЕМ ЭТО БЫЛО РАНЬШЕ. Полосу рисовала себе каждая сущность сама, в своём слое пометок,
/// двумя прямоугольниками. Отсюда следовало, что стоимость росла вместе с числом сущностей
/// дважды: на каждую приходился свой список команд отрисовки, и ради него сущность каждый
/// кадр объявляла перерисовку — даже при целой прочности, когда полосы нет вовсе.
/// Здесь полосы лежат в одной сетке: геометрия у неё одна на всех, материал общий,
/// и весь мир обходится серверу отрисовки в один вызов.
///
/// ЧТО ДЕЛАЕТ ШЕЙДЕР. Полоса не складывается из двух прямоугольников: экземпляр один,
/// а деление на заполненную часть и фон вместе с выбором цвета по доле прочности
/// и наложением рамки идёт в материале — см. <c>resources/shaders/health_bar.gdshader</c>.
/// Доля передаётся данными экземпляра, поскольку материал у сетки общий и различаться
/// по сущностям не может.
///
/// ПОЧЕМУ СИСТЕМОЙ, А НЕ НОДОЙ СО СВОИМ _Process. Заполнение буфера обязано идти после того,
/// как перемещения и наведение этого кадра уже применены, иначе полосы отстают от корпусов
/// на кадр. Порядок задаёт планировщик, а неявный обход дерева нод его не даёт.
///
/// ЧЕГО СТОИТ ОБХОД. Сущности перебираются разрезом индекса каждый кадр, и полосу получают
/// лишь повреждённые, видимые и попавшие в область камеры. Первая же проверка — целая
/// прочность — отсекает подавляющее большинство раньше остальных. Проверка видимости нужна
/// потому, что раньше скрытая нода не рисовала ничего сама собой, а теперь решение
/// принимается здесь.
/// </summary>
public partial class HealthBarSystem : GameSystem
{
    /// <summary>
    /// Начальный запас мест в сетке. Повреждённых обычно меньше, а перевыделение буфера
    /// при каждом росте их числа обошлось бы дороже неиспользованных мест.
    /// </summary>
    private const int MinCapacity = 128;

    /// <summary>
    /// Запас к области камеры, мировые пиксели. Полоса шире корпуса и поднята над ним,
    /// поэтому у сущности за краем экрана она может быть ещё видна.
    /// </summary>
    private const float ViewMargin = Const.Unit * 4f;

    private readonly List<Bar> _bars = new();

    private MultiMeshInstance2D _node;
    private MultiMesh _mesh;

    public HealthBarSystem()
    {
        // Показ, а не изменение мира: полосы ведутся в графическом цикле после ввода
        // и перемещений этого кадра
        Phase = Phase.React;
        UpdateCycle = UpdateCycle.Process;
    }

    public override void Step(double dt)
    {
        if (!EnsureNode())
            return;

        var settings = GraphicsSettings.Bars;

        _node.Visible = settings.Enabled;

        if (!settings.Enabled)
            return;

        Collect(settings);
        Write(settings);
    }

    /// <summary>
    /// Собрать полосы этого кадра. Отдельным проходом до записи в сетку, поскольку запас
    /// мест отводится сразу под всё собранное: перевыделение буфера посреди записи стёрло бы
    /// уже записанные преобразования.
    /// </summary>
    private void Collect(HealthBarSettings settings)
    {
        _bars.Clear();

        var view = (_node.GetCanvasTransform().AffineInverse() * _node.GetViewportRect())
            .Grow(ViewMargin);

        foreach (var marked in GM.Index.All<IHealthMarked>())
        {
            var health = marked.Health;

            if (health == null || health.Ratio >= settings.FullEnough)
                continue;

            // Показ берётся полем сущности — см. то же место в UnitGizmoOverlay
            if (marked is not Entity node || !Alive.Is(node) || !node.Visible)
                continue;

            var at = marked.GlobalPosition;

            if (!view.HasPoint(at))
                continue;

            _bars.Add(new Bar
            {
                At = at - new Vector2(0f, marked.HealthBarLift),
                Width = marked.HealthBarWidth,
                Ratio = health.Ratio,
            });
        }
    }

    /// <summary>Записать собранное в сетку: место на полосу, преобразование и долю.</summary>
    private void Write(HealthBarSettings settings)
    {
        Reserve(_bars.Count);

        var toLocal = _node.GlobalTransform.AffineInverse();
        var size = new Vector2(1f, Mathf.Max(settings.Height, 0.1f));

        for (int i = 0; i < _bars.Count; i++)
        {
            var bar = _bars[i];

            _mesh.SetInstanceTransform2D(i,
                new Transform2D(0f, size with { X = bar.Width }, 0f, toLocal * bar.At));

            _mesh.SetInstanceCustomData(i, new Color(bar.Ratio, 0f, 0f, 0f));
        }

        // Незанятые места не прячутся по одному: сетка рисует ровно столько мест, сколько
        // здесь объявлено, а лежащие за этой границей до растеризации не доходят
        _mesh.VisibleInstanceCount = _bars.Count;
    }

    /// <summary>
    /// Довести запас мест до нужного. Число мест удваивается, а не подгоняется в точности:
    /// повреждённых становится то больше, то меньше каждый кадр, и подгонка означала бы
    /// перевыделение буфера на каждое попадание.
    /// </summary>
    private void Reserve(int count)
    {
        if (count <= _mesh.InstanceCount)
            return;

        int capacity = Mathf.Max(_mesh.InstanceCount, MinCapacity);

        while (capacity < count)
            capacity *= 2;

        _mesh.InstanceCount = capacity;
    }

    /// <summary>
    /// Завести узел сетки, если его ещё нет. Ленивое заведение, а не поле в сцене:
    /// узел принадлежит слою мира, а система живёт в ветке систем — так же поступают
    /// CommandSystem и HoverSystem со своими наложениями.
    /// </summary>
    private bool EnsureNode()
    {
        if (Alive.Is(_node))
            return true;

        if (GM?.Playground == null)
            return false;

        _mesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,

            // Доля прочности идёт данными экземпляра: цвет полосы выбирает шейдер,
            // и модуляция экземпляра для этого не годится — порогов у неё нет
            UseCustomData = true,

            // Сторона в единицу: ширина и высота полосы задаются масштабом преобразования,
            // и одна геометрия годится сущности любого габарита
            Mesh = new QuadMesh { Size = Vector2.One },
            InstanceCount = MinCapacity,
            VisibleInstanceCount = 0,
        };

        _node = GM.Playground.Add(WorldLayer.Overlay, new MultiMeshInstance2D
        {
            Name = "HealthBars",
            Multimesh = _mesh,
            Material = GraphicsSettings.Bars.Coat,
        });

        return true;
    }

    /// <summary>Полоса одной сущности, собранная до записи в сетку.</summary>
    private struct Bar
    {
        public Vector2 At;
        public float Width;
        public float Ratio;
    }
}
