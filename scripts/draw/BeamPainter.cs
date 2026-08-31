using System.Collections.Generic;
using Godot;

/// <summary>
/// Отрисовка лучей одного вида множественными сетками — по сетке на часть луча.
///
/// ЗАЧЕМ. Прежде каждый луч рисовал себя сам в собственном <c>_Draw</c>: полоса набиралась
/// отрезками, вспышки — окружностями, и на каждый работающий инструмент приходился свой
/// список команд отрисовки. Здесь геометрия одна на все лучи вида: четырёхугольник со
/// стороной в единицу, растянутый преобразованием. Кадровая работа сводится к записи
/// преобразований, а сервер отрисовки получает три вызова на любое число лучей.
///
/// ПОЧЕМУ СЕТОК ТРИ, А НЕ ОДНА. Тело луча и вспышки рисуются разными шейдерами: у тела
/// рисунок повторяется вдоль и бежит, у вспышки он круговой и вращается. Материал же
/// у сетки один на все её экземпляры, поэтому части не помещаются в общую сетку. Вспышки
/// у среза и в точке попадания делят шейдер, но не сетку: текстура и цвет у них расходятся.
///
/// ЧТО СЧИТАЕТСЯ ЗДЕСЬ, А ЧТО В ШЕЙДЕРЕ. Здесь — то, что задаёт преобразование: длина,
/// толщина с пульсацией, угол, поворот вспышки. В шейдере — то, что зависит от места внутри
/// изображения: повтор рисунка вдоль луча, бег, смягчение краёв, спад яркости вспышки.
/// Разгорание входит в прозрачность цвета экземпляра.
///
/// ГДЕ ЖИВУТ УЗЛЫ. Отрисовщик добавляет сетки потомками переданного узла и пишет
/// преобразования в его местных координатах. В игре это общий узел лучей в слое мира,
/// в редакторе — сам <see cref="BeamVisual"/>, и приём отрисовки в обоих случаях один:
/// предпросмотр показывает ровно то, что будет видно в игре.
/// </summary>
public sealed class BeamPainter
{
    /// <summary>Начальный запас мест в сетке. Лучей одного вида обычно немного.</summary>
    private const int MinCapacity = 32;

    private readonly List<Beam> _beams = new();
    private readonly Node2D _parent;

    private readonly Batch _body;
    private readonly Batch _muzzle;
    private readonly Batch _impact;

    public BeamPainter(Node2D parent, BeamStyle style)
    {
        _parent = parent;
        Style = style;

        // Тело ниже вспышек: вспышка перекрывает стык полосы с концом луча
        _body = new Batch(parent, zIndex: 0, tiled: true);
        _muzzle = new Batch(parent, zIndex: 1, tiled: false);
        _impact = new Batch(parent, zIndex: 1, tiled: false);
    }

    /// <summary>Вид, которому принадлежит отрисовщик. По нему лучи и разводятся.</summary>
    public BeamStyle Style { get; }

    /// <summary>
    /// Принять луч этого кадра. Записи в сетку ещё не происходит: запас мест отводится
    /// сразу под все принятые лучи, а перевыделение буфера посреди записи стёрло бы
    /// уже записанные преобразования.
    /// </summary>
    public void Add(Vector2 from, Vector2 to, float power, float time)
    {
        if (power <= 0f)
            return;

        _beams.Add(new Beam { From = from, To = to, Power = power, Time = time });
    }

    /// <summary>Записать принятое в сетки и объявить, сколько мест они рисуют.</summary>
    public void Flush()
    {
        var toLocal = _parent.GlobalTransform.AffineInverse();

        WriteBody(toLocal);
        WriteCaps(toLocal, _muzzle, Style?.Muzzle, atTarget: false);
        WriteCaps(toLocal, _impact, Style?.Impact, atTarget: true);

        _beams.Clear();
    }

    /// <summary>Убрать узлы сеток. Нужно предпросмотру: вид сменился — сетки пересобираются.</summary>
    public void Discard()
    {
        _body.Discard();
        _muzzle.Discard();
        _impact.Discard();
        _beams.Clear();
    }

    private void WriteBody(Transform2D toLocal)
    {
        var style = Style?.Body;

        if (!_body.Begin(Coat(style), style?.Texture, _beams.Count))
            return;

        for (int i = 0; i < _beams.Count; i++)
        {
            var beam = _beams[i];
            var span = beam.To - beam.From;
            float length = span.Length();

            if (length < 1f)
                continue;

            float width = style.Width * Pulse(style.PulseDepth, style.PulseSpeed, beam.Time);

            // Четырёхугольник ставится серединой луча и растягивается по его длине:
            // геометрия у сетки одна, а длина и толщина задаются масштабом
            var placed = new Transform2D(span.Angle(), new Vector2(length, width), 0f,
                (beam.From + beam.To) * 0.5f);

            // Повтор рисунка считается по длине луча в мировых пикселях, а не по числу
            // экземпляров: иначе на длинном луче рисунок растягивается, а на коротком мнётся
            float tiles = style.TileLength > 0f ? length / style.TileLength : 1f;
            float offset = style.TileLength > 0f
                ? -beam.Time * style.ScrollSpeed / style.TileLength
                : 0f;

            _body.Put(toLocal * placed, Tint(style.Color, beam.Power),
                new Color(tiles, offset, 0f, 0f));
        }

        _body.End();
    }

    private void WriteCaps(Transform2D toLocal, Batch batch, BeamCapStyle style, bool atTarget)
    {
        if (!batch.Begin(Coat(style), style?.Texture, _beams.Count))
            return;

        if (style.Radius > 0f)
        {
            for (int i = 0; i < _beams.Count; i++)
            {
                var beam = _beams[i];
                float radius = style.Radius * Pulse(style.PulseDepth, style.PulseSpeed, beam.Time);
                float side = radius * 2f;
                float spin = beam.Time * style.SpinSpeed * Mathf.Tau;

                var placed = new Transform2D(spin, new Vector2(side, side), 0f,
                    atTarget ? beam.To : beam.From);

                batch.Put(toLocal * placed, Tint(style.Color, beam.Power), Colors.Black);
            }
        }

        batch.End();
    }

    /// <summary>Материал включённой части. Пустой ответ означает, что часть не рисуется.</summary>
    private static Material Coat(BeamBodyStyle style) => style is { Enabled: true } ? style.Coat : null;

    /// <inheritdoc cref="Coat(BeamBodyStyle)"/>
    private static Material Coat(BeamCapStyle style) => style is { Enabled: true } ? style.Coat : null;

    /// <summary>Пульсация размера вокруг заданной величины. Свойство изображения, не работы.</summary>
    private static float Pulse(float depth, float speed, float time) =>
        depth > 0f ? 1f + depth * Mathf.Sin(time * speed * Mathf.Tau) : 1f;

    /// <summary>Цвет части с поправкой на разгорание: гаснет луч прозрачностью.</summary>
    private static Color Tint(Color color, float power) => color with { A = color.A * power };

    /// <summary>Луч, принятый в этом кадре, до записи в сетки.</summary>
    private struct Beam
    {
        public Vector2 From;
        public Vector2 To;
        public float Power;
        public float Time;
    }

    /// <summary>
    /// Одна множественная сетка со своим материалом и текстурой. Кадр проходит по ней тремя
    /// шагами: <see cref="Begin"/> берёт действующие материал с текстурой и отводит место,
    /// <see cref="Put"/> пишет очередной экземпляр, <see cref="End"/> объявляет, сколько
    /// мест сетка рисует.
    /// </summary>
    private sealed class Batch
    {
        private readonly Node2D _parent;
        private readonly int _zIndex;
        private readonly bool _tiled;

        private MultiMeshInstance2D _node;
        private MultiMesh _mesh;
        private int _shown;

        public Batch(Node2D parent, int zIndex, bool tiled)
        {
            _parent = parent;
            _zIndex = zIndex;
            _tiled = tiled;
        }

        /// <summary>
        /// Начать кадр. Пустой материал означает выключенную часть: узел прячется,
        /// и записи не происходит вовсе. Ответ говорит, есть ли куда писать.
        /// </summary>
        public bool Begin(Material material, Texture2D texture, int capacity)
        {
            _shown = 0;

            if (material == null || capacity <= 0)
            {
                if (Alive.Is(_node))
                    _node.Visible = false;

                return false;
            }

            Ensure();

            _node.Visible = true;

            // Материал и текстура ставятся здесь, а не при заведении узла: свод вида
            // правится в инспекторе на ходу, и сетка обязана взять действующие
            if (_node.Material != material)
                _node.Material = material;

            if (_node.Texture != texture)
                _node.Texture = texture;

            Reserve(capacity);
            return true;
        }

        public void Put(Transform2D placed, Color tint, Color custom)
        {
            if (_node == null || _shown >= _mesh.InstanceCount)
                return;

            _mesh.SetInstanceTransform2D(_shown, placed);
            _mesh.SetInstanceColor(_shown, tint);
            _mesh.SetInstanceCustomData(_shown, custom);
            _shown++;
        }

        public void End()
        {
            if (_mesh != null)
                _mesh.VisibleInstanceCount = _shown;
        }

        public void Discard()
        {
            if (Alive.Is(_node))
                _node.QueueFree();

            _node = null;
            _mesh = null;
        }

        private void Ensure()
        {
            if (Alive.Is(_node))
                return;

            _mesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,

                // Цветом передаётся окраска части вместе с разгоранием, данными — повтор
                // рисунка и его смещение: у материала нет способа остаться общим, когда
                // эти величины у каждого луча свои
                UseColors = true,
                UseCustomData = true,

                // Сторона в единицу: длина, толщина и радиус задаются масштабом
                Mesh = new QuadMesh { Size = Vector2.One },
                InstanceCount = MinCapacity,
                VisibleInstanceCount = 0,
            };

            _node = new MultiMeshInstance2D
            {
                Multimesh = _mesh,
                ZIndex = _zIndex,

                // Повтор нужен бегущему рисунку тела: без него участок за краем изображения
                // тянется краевым пикселем вместо следующего повтора
                TextureRepeat = _tiled
                    ? CanvasItem.TextureRepeatEnum.Enabled
                    : CanvasItem.TextureRepeatEnum.ParentNode,
            };

            _parent.AddChild(_node);
        }

        /// <summary>
        /// Довести запас мест до нужного удвоением. Точная подгонка означала бы
        /// перевыделение буфера на каждый начатый и погашенный луч.
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
