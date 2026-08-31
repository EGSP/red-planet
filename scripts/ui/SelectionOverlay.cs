using Godot;

/// <summary>
/// Метки выделения — изображения над выделенными сущностями. Раньше на каждую выделенную
/// сущность строилось кольцо из отрезков (<see cref="OrderOverlay"/>), и стоимость отрисовки
/// росла вместе с размером отряда. Здесь метки лежат в множественных сетках: геометрия
/// у сетки одна на всех, изображение и материал общие, поэтому весь отряд обходится серверу
/// отрисовки в один вызов на сетку.
///
/// ПОЧЕМУ СЕТОК ДВЕ. Ходящая сущность и постройка помечаются по-разному, а изображение
/// и материал у сетки одни на все её экземпляры; отсюда следует, что два вида метки
/// не помещаются в общую сетку и требуют по своей. Разделяет их наличие занятого места
/// (<see cref="IObstacle"/>): постройка стоит на клетках, ходящая сущность — нет.
///
/// ЧТО ОСТАЁТСЯ КАДРОВОЙ РАБОТОЙ. Юниты движутся, поэтому преобразование каждой метки
/// приходится записывать в сетку каждый кадр. Записью дело и ограничивается: вершины
/// не строятся, поскольку сетка держит один четырёхугольник со стороной в единицу,
/// а размер метки задан масштабом её преобразования.
///
/// СВОИХ НАСТРОЕК У НОДЫ НЕТ: изображение, материал и поправки размера живут
/// в <see cref="SelectionSettings"/> — по своему своду на каждый вид метки, — а нода
/// заводится кодом и в сцене не значится.
/// </summary>
public partial class SelectionOverlay : Node2D
{
    /// <summary>
    /// Начальный запас мест в сетке. Отряд обычно меньше, а перевыделение буфера при каждом
    /// росте выделения обошлось бы дороже неиспользованных мест: место стоит одно
    /// преобразование, то есть восемь чисел.
    /// </summary>
    private const int MinCapacity = 64;

    private Batch _units;
    private Batch _structures;

    public override void _Ready()
    {
        _units = new Batch(this);
        _structures = new Batch(this);
    }

    public override void _Process(double delta)
    {
        var command = GameManager.I?.Command;
        int count = command?.Selected.Count ?? 0;

        // Запас берётся под всё выделение сразу для каждой сетки: перевыделение буфера
        // посреди обхода стёрло бы уже записанные преобразования
        _units.Begin(GraphicsSettings.UnitSelection, count);
        _structures.Begin(GraphicsSettings.StructureSelection, count);

        if (command != null)
        {
            foreach (var actor in command.Selected)
            {
                if (actor is not Node2D node || !Alive.Is(node))
                    continue;

                float radius = (actor as IDamageable)?.HitRadius ?? Const.Unit * 0.4f;
                var batch = actor is IObstacle ? _structures : _units;

                batch.Put(ToLocal(node.GlobalPosition), radius);
            }
        }

        _units.End();
        _structures.End();
    }

    /// <summary>
    /// Одна множественная сетка со своим сводом настроек. Кадр проходит по ней тремя шагами:
    /// <see cref="Begin"/> берёт действующие изображение и материал и отводит место,
    /// <see cref="Put"/> пишет преобразование очередной метки, <see cref="End"/> объявляет,
    /// сколько мест сетка рисует.
    /// </summary>
    private sealed class Batch
    {
        private readonly MultiMeshInstance2D _node;
        private readonly MultiMesh _mesh;

        private SelectionSettings _settings;
        private int _shown;

        public Batch(Node2D owner)
        {
            _mesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,

                // Сторона в единицу: размер метки задаётся масштабом преобразования,
                // и одна и та же геометрия годится сущностям любого габарита
                Mesh = new QuadMesh { Size = Vector2.One },
                InstanceCount = MinCapacity,
                VisibleInstanceCount = 0,
            };

            _node = new MultiMeshInstance2D
            {
                Multimesh = _mesh,
                TextureFilter = TextureFilterEnum.LinearWithMipmaps,
            };

            owner.AddChild(_node);
        }

        public void Begin(SelectionSettings settings, int capacity)
        {
            _shown = 0;
            _settings = settings != null && settings.Enabled && settings.Sprite != null
                ? settings
                : null;

            if (_settings == null)
                return;

            // Изображение и материал ставятся здесь, а не при заведении сетки: настройка
            // может смениться вместе со сводом графики, и сетка обязана взять действующие
            if (_node.Texture != _settings.Sprite)
                _node.Texture = _settings.Sprite;

            var coat = _settings.Coat;

            if (_node.Material != coat)
                _node.Material = coat;

            Reserve(capacity);
        }

        public void Put(Vector2 center, float radius)
        {
            if (_settings == null || _shown >= _mesh.InstanceCount)
                return;

            float side = _settings.SideFor(radius);

            _mesh.SetInstanceTransform2D(_shown,
                new Transform2D(0f, new Vector2(side, side), 0f, center));

            _shown++;
        }

        /// <summary>
        /// Незанятые места не прячутся по одному: сетка рисует ровно столько мест, сколько
        /// здесь объявлено, а лежащие за этой границей до растеризации не доходят.
        /// </summary>
        public void End() => _mesh.VisibleInstanceCount = _shown;

        /// <summary>
        /// Довести запас мест до нужного. Число мест удваивается, а не подгоняется под отряд
        /// в точности: выделение меняется часто, и подгонка означала бы перевыделение буфера
        /// на каждый добавленный юнит.
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
    }
}
