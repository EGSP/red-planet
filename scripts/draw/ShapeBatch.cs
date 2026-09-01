using System.Collections.Generic;
using Godot;

/// <summary>
/// Одна множественная сетка служебной графики: все фигуры одной формы в одном слое мира.
/// Заводится и наполняется через <see cref="ShapeMesh"/>, самостоятельного применения
/// не имеет.
///
/// ПОЧЕМУ ФИГУРЫ СНАЧАЛА НАКАПЛИВАЮТСЯ, А НЕ ПИШУТСЯ СРАЗУ. Запас мест в сетке отводится
/// под всё принятое за кадр, а число фигур заранее не известно: перевыделение буфера
/// посреди записи стёрло бы уже записанные преобразования. Тот же приём применён
/// в <see cref="HealthBarSystem"/> и <see cref="BeamPainter"/>.
///
/// ЧТО НЕСЁТ ЭКЗЕМПЛЯР. Преобразование задаёт место, поворот и размеры четырёхугольника;
/// цвет экземпляра — оттенок с прозрачностью; данные экземпляра — четыре величины, смысл
/// которых определён шейдером формы. Параметры экземпляра шейдера (<see cref="InstanceParam"/>)
/// здесь не годятся: они принадлежат узлу целиком, а не месту в буфере.
/// </summary>
public sealed class ShapeBatch
{
    /// <summary>
    /// Начальный запас мест. Фигур служебной графики за кадр обычно сотни, а перевыделение
    /// буфера при каждом росте их числа обошлось бы дороже неиспользованных мест.
    /// </summary>
    private const int MinCapacity = 256;

    private readonly List<Item> _items = new();
    private readonly MultiMeshInstance2D _node;
    private readonly MultiMesh _mesh;

    public ShapeBatch(Node2D parent, string name, Material material, int zIndex)
    {
        _mesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,

            // Оттенок фигуры принадлежит экземпляру: материал у сетки один на все фигуры
            // формы, и цвет вида приказа различаться через него не может
            UseColors = true,

            // Размеры формы внутри четырёхугольника — доли радиуса, толщина обводки,
            // номер слоя значка. Всё это тоже принадлежит экземпляру
            UseCustomData = true,

            // Сторона в единицу: размеры фигуры задаются масштабом преобразования,
            // и одна геометрия годится фигуре любого габарита
            Mesh = new QuadMesh { Size = Vector2.One },
            InstanceCount = MinCapacity,
            VisibleInstanceCount = 0,
        };

        _node = new MultiMeshInstance2D
        {
            Name = name,
            Multimesh = _mesh,
            Material = material,
            ZIndex = zIndex,
            TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
        };

        parent.AddChild(_node);
    }

    /// <summary>Узел сетки. Нужен менеджеру для масштаба холста и записи параметров материала.</summary>
    public MultiMeshInstance2D Node => _node;

    /// <summary>
    /// Принять фигуру этого кадра. Преобразование задаётся в мировых координатах: перевод
    /// в местные координаты узла идёт при записи, одним умножением на весь буфер.
    /// </summary>
    public void Add(in Transform2D at, in Color tint, in Color data) =>
        _items.Add(new Item { At = at, Tint = tint, Data = data });

    /// <summary>Записать принятое в сетку и объявить, сколько мест она рисует.</summary>
    public void Flush()
    {
        if (!Alive.Is(_node))
        {
            _items.Clear();
            return;
        }

        Reserve(_items.Count);

        var toLocal = _node.GlobalTransform.AffineInverse();

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];

            _mesh.SetInstanceTransform2D(i, toLocal * item.At);
            _mesh.SetInstanceColor(i, item.Tint);
            _mesh.SetInstanceCustomData(i, item.Data);
        }

        // Незанятые места не прячутся по одному: сетка рисует ровно столько мест, сколько
        // здесь объявлено, а лежащие за этой границей до растеризации не доходят
        _mesh.VisibleInstanceCount = _items.Count;
        _items.Clear();
    }

    /// <summary>
    /// Довести запас мест до нужного. Число мест удваивается, а не подгоняется в точности:
    /// состав служебной графики меняется каждый кадр, и подгонка означала бы перевыделение
    /// буфера на каждую добавленную фигуру.
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

    /// <summary>Фигура, принятая до записи в сетку.</summary>
    private struct Item
    {
        public Transform2D At;
        public Color Tint;
        public Color Data;
    }
}
