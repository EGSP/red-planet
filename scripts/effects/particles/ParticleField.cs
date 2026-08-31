using Godot;

/// <summary>
/// Общее поле частиц: один узел выдачи на все эффекты, совпавшие по <see cref="ParticleKey"/>.
///
/// ЧТО ЗДЕСЬ ПРОИСХОДИТ. Штатная выдача выключена; частицы вбрасываются извне вызовом
/// <see cref="GpuParticles2D.EmitParticle"/>, который задаёт лишь начальное состояние.
/// Дальнейшее поведение частицы считает материал обработки — тот самый, что художник
/// настроил в сцене эффекта, — и повторять его правила здесь не требуется.
///
/// ПОЧЕМУ КООРДИНАТЫ МИРОВЫЕ. Поле стоит в начале координат слоя и никуда не движется,
/// а частицы принадлежат тем местам, где случились события. При местных координатах
/// весь выпущенный дым ехал бы вслед за узлом.
///
/// ПОЧЕМУ ОБЛАСТЬ ВИДИМОСТИ ЗАДАНА С ЗАПАСОМ. Движок отсекает узел частиц целиком
/// по объявленной области. У прежних эффектов область охватывала окрестность вспышки,
/// но общее поле выпускает частицы по всей карте, и область обязана покрывать её всю,
/// иначе поле пропадало бы разом, стоит камере отойти от начала координат.
///
/// ЗАПАС КОЛЬЦЕВОЙ. Когда вбросов приходит больше, чем поле вмещает, новые частицы
/// вытесняют самые старые. Для боя это верное поведение: пропажа хвоста давнего взрыва
/// заметна меньше, чем отсутствие вспышки у только что попавшего снаряда.
/// </summary>
public sealed partial class ParticleField : GpuParticles2D
{
    /// <summary>Насколько далеко от начала координат простирается область видимости.</summary>
    private const float Reach = 1_000_000f;

    /// <summary>Признак, по которому поле заведено. Хранится ради отладки и разбора.</summary>
    public ParticleKey Key { get; private set; }

    /// <summary>
    /// Завести поле по признаку. Материал берётся у признака как есть, пока множитель
    /// размера натуральный; при ином множителе поле получает собственную копию материала
    /// с домноженными размером, скоростью, затуханием и разбросом — см. <see cref="ParticleKey"/>.
    /// </summary>
    public static ParticleField Make(in ParticleKey key, int capacity)
    {
        var field = new ParticleField
        {
            Name = $"Field_{key.Grade}_{key.Texture?.ResourcePath.GetFile() ?? "none"}",
            Key = key,
            Amount = Mathf.Max(capacity, 1),
            Lifetime = key.Lifetime,
            Texture = key.Texture,
            Material = key.Canvas,
            TextureFilter = key.Filter,
            ZIndex = key.ZIndex,
            LocalCoords = false,
            OneShot = false,
            Emitting = false,
            Explosiveness = 0f,
            VisibilityRect = new Rect2(-Reach, -Reach, Reach * 2f, Reach * 2f),
            ProcessMaterial = Resized(key),
        };

        return field;
    }

    /// <summary>
    /// Вбросить порцию частиц.
    ///
    /// Место, поворот и скорость считаются здесь, поскольку общее поле о событии не знает
    /// ничего: <paramref name="spot"/> есть место события в мире, <paramref name="facing"/> —
    /// его поворот, <paramref name="size"/> — множитель размера, объявленный в модели.
    /// Множитель размера уже учтён в материале поля, поэтому здесь он растит только разлёт
    /// и смещение части.
    /// </summary>
    public void Cast(EmissionProfile part, Vector2 spot, float facing, float size, int count)
    {
        if (part == null || count <= 0)
            return;

        float angle = facing + part.Angle;
        var origin = spot + part.Offset.Rotated(facing) * size;

        for (int i = 0; i < count; i++)
        {
            var born = origin + Scatter(part.Radius * size);
            var place = new Transform2D(angle, born);

            uint flags = (uint)EmitFlags.Position | (uint)EmitFlags.RotationScale;
            var speed = Vector2.Zero;

            if (part.Driven)
            {
                float course = angle + part.Course
                    + (float)GD.RandRange(-part.Spread, part.Spread);

                speed = Vector2.FromAngle(course)
                    * (float)GD.RandRange(part.SpeedMin, part.SpeedMax) * size;

                flags |= (uint)EmitFlags.Velocity;
            }

            EmitParticle(place, speed, Colors.White, Colors.White, flags);
        }
    }

    /// <summary>Случайная точка внутри круга заданного радиуса.</summary>
    private static Vector2 Scatter(float radius)
    {
        if (radius <= 0.01f)
            return Vector2.Zero;

        float angle = (float)GD.RandRange(0.0, Mathf.Tau);
        float reach = radius * Mathf.Sqrt(GD.Randf());

        return Vector2.FromAngle(angle) * reach;
    }

    /// <summary>
    /// Материал поля. При натуральном размере отдаётся общий материал сцены; при ином —
    /// его копия, у которой домножено всё, что имеет размерность длины. Домножать
    /// приходится сам материал, поскольку размер частицы вычисляется им заново каждый кадр
    /// и внешним преобразованием не задаётся.
    /// </summary>
    private static Material Resized(in ParticleKey key)
    {
        if (key.Process == null || key.Natural)
            return key.Process;

        if (key.Process.Duplicate() is not ParticleProcessMaterial copy)
            return key.Process;

        float size = key.Size;

        copy.ScaleMin *= size;
        copy.ScaleMax *= size;
        copy.InitialVelocityMin *= size;
        copy.InitialVelocityMax *= size;
        copy.DampingMin *= size;
        copy.DampingMax *= size;
        copy.EmissionSphereRadius *= size;
        copy.Gravity *= size;

        return copy;
    }
}
