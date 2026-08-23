using System.Collections.Generic;
using Godot;

/// <summary>
/// Слой отметин копоти: хранит все отпечатки, оставленные взрывами, и рисует их.
///
/// ОТРИСОВКА — МНОЖЕСТВЕННАЯ СЕТКА, как у декалей поверхности. Отпечаток есть неподвижное
/// изображение, различающееся с соседями только положением, поворотом, размером и
/// оттенком, поэтому сотня отпечатков стоит ровно одного вызова отрисовки на изображение.
/// Отдельными узлами <c>Sprite2D</c> та же сотня стоила бы сотни вызовов и сотни узлов
/// в дереве — при том, что меняться после постановки им нечем.
///
/// ЧИСЛО МЕСТ ОГРАНИЧЕНО, И ЭТО НЕ ОГРАНИЧЕНИЕ ПАМЯТИ, А ПРАВИЛО ПОКАЗА. Партия идёт
/// часами, гибнет в ней всё, и без предела карта к середине партии оказалась бы залита
/// копотью сплошь. Набор кольцевой: заполнив последнее место, слой возвращается к первому
/// и переписывает самый старый отпечаток. Отсюда следует, что свежий след виден всегда,
/// а исчезают самые давние — то есть ровно те, о которых игрок уже забыл.
///
/// СЛЕДСТВИЕ, О КОТОРОМ СЛЕДУЕТ ПОМНИТЬ. Порядок наложения задан номером места, а не
/// возрастом отпечатка, поскольку сетка рисует экземпляры по порядку. После первого
/// оборота новый отпечаток ложится под те, что старше его. Для тёмных полупрозрачных
/// пятен разницы не видно; для чего-нибудь с резким краем это было бы заметно.
///
/// СВОЙ НАБОР НА КАЖДОЕ ИЗОБРАЖЕНИЕ. Множественная сетка рисуется одной текстурой,
/// поэтому набор отметин с тремя изображениями даёт три сетки. Атлас здесь не нужен:
/// изображений единицы, значит и вызовов отрисовки единицы.
/// </summary>
public partial class ScorchField : Node2D
{
    /// <summary>Путь к шейдеру угасания. Общий на все наборы: считает он одно и то же.</summary>
    private const string ShaderPath = "res://resources/shaders/scorch.gdshader";

    /// <summary>Имя параметра, через который в шейдер передаётся время слоя.</summary>
    private const string NowParameter = "now";

    /// <summary>
    /// Сколько отпечатков держать на каждое изображение набора. Считается на изображение,
    /// а не на слой целиком: сетка своя у каждого, и делить общий предел между ними
    /// значило бы менять вместимость от того, сколько изображений художник положил в набор.
    /// </summary>
    [Export(PropertyHint.Range, "8,1024,8,or_greater")] public int Capacity { get; set; } = 96;

    /// <summary>Кольцевой набор мест под одно изображение.</summary>
    private sealed class Ring
    {
        public MultiMesh Mesh;
        public int Next;
    }

    private readonly Dictionary<Texture2D, Ring> _rings = new();

    /// <summary>
    /// Часы слоя, секунды от его создания. Свои, а не встроенное время шейдера: срок
    /// отпечатка отсчитывается от постановки, и записать этот миг обязан тот же счётчик,
    /// который потом сравнивает с ним шейдер.
    /// </summary>
    private float _clock;

    private ShaderMaterial _material;
    private QuadMesh _quad;

    public override void _Ready()
    {
        _quad = new QuadMesh { Size = Vector2.One };

        var shader = GD.Load<Shader>(ShaderPath);

        if (shader == null)
        {
            GD.PushWarning($"[ScorchField] шейдер не найден: {ShaderPath}");
            return;
        }

        _material = new ShaderMaterial { Shader = shader };
    }

    /// <summary>
    /// Кадровая работа слоя целиком: одна запись числа. Прозрачность каждого отпечатка
    /// шейдер выводит из неё сам.
    /// </summary>
    public override void _Process(double delta)
    {
        _clock += (float)delta;
        _material?.SetShaderParameter(NowParameter, _clock);
    }

    /// <summary>
    /// Поставить отпечаток. Изображение, поворот и размер выбираются здесь: набор задаёт
    /// границы, а какой именно след вышел — свойство этого отпечатка, и запоминать его
    /// негде и незачем.
    /// </summary>
    public void Stamp(ScorchDecal decal, Vector2 position, float minSize, float maxSize)
    {
        if (decal == null || _quad == null)
            return;

        var texture = Pick(decal);

        if (texture == null)
            return;

        var ring = Slots(texture);
        float size = (float)GD.RandRange(Mathf.Min(minSize, maxSize), Mathf.Max(minSize, maxSize));
        float angle = decal.RandomRotation ? (float)GD.RandRange(0d, Mathf.Tau) : 0f;

        ring.Mesh.SetInstanceTransform2D(ring.Next, new Transform2D(
            angle, new Vector2(size, size), 0f, ToLocal(position)));

        ring.Mesh.SetInstanceColor(ring.Next, decal.Tint);
        ring.Mesh.SetInstanceCustomData(ring.Next,
            new Color(_clock, Mathf.Max(decal.Lifetime, 0f), Mathf.Max(decal.Fade, 0f), 0f));

        ring.Next = (ring.Next + 1) % Mathf.Max(Capacity, 1);
    }

    /// <summary>Убрать все отпечатки. Нужно при смене партии: копоть прошлой карты чужая.</summary>
    public void Clear()
    {
        foreach (var ring in _rings.Values)
        {
            for (int i = 0; i < ring.Mesh.InstanceCount; i++)
                Hide(ring.Mesh, i);

            ring.Next = 0;
        }
    }

    /// <summary>
    /// Набор мест под изображение. Поднимается при первом отпечатке этим изображением:
    /// в наборе отметин могут лежать изображения, которые за партию так и не выпадут.
    /// </summary>
    private Ring Slots(Texture2D texture)
    {
        if (_rings.TryGetValue(texture, out var ring))
            return ring;

        int capacity = Mathf.Max(Capacity, 1);

        var mesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,
            UseCustomData = true,
            Mesh = _quad,
            InstanceCount = capacity,
        };

        // Пустые места обязаны быть незаметны. Преобразование по умолчанию единичное,
        // то есть каждое из них нарисовало бы изображение размером в один пиксель в начале
        // координат; нулевой размер снимает вопрос и не доходит до растеризации вовсе
        for (int i = 0; i < capacity; i++)
            Hide(mesh, i);

        ring = new Ring { Mesh = mesh };
        _rings[texture] = ring;

        AddChild(new MultiMeshInstance2D
        {
            Multimesh = mesh,
            Texture = texture,
            Material = _material,
            TextureFilter = TextureFilterEnum.LinearWithMipmaps,
        });

        return ring;
    }

    private static void Hide(MultiMesh mesh, int index)
    {
        mesh.SetInstanceTransform2D(index, new Transform2D(0f, Vector2.Zero, 0f, Vector2.Zero));
        mesh.SetInstanceColor(index, new Color(0f, 0f, 0f, 0f));
        mesh.SetInstanceCustomData(index, new Color(0f, 0f, 0f, 0f));
    }

    private static Texture2D Pick(ScorchDecal decal)
    {
        var textures = decal.Textures;

        if (textures == null || textures.Length == 0)
            return null;

        // Случайный выбор с одной попыткой не годится: пустая ячейка в наборе означала бы
        // взрыв без следа. Ищем от случайного места по кругу
        int start = GD.RandRange(0, textures.Length - 1);

        for (int i = 0; i < textures.Length; i++)
        {
            var texture = textures[(start + i) % textures.Length];

            if (texture != null)
                return texture;
        }

        return null;
    }
}
