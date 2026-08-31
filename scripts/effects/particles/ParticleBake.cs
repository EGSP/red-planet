using System.Collections.Generic;
using Godot;

/// <summary>
/// Разбор сцены эффекта в перечень вбросов. Проходит дерево один раз на сцену и снимает
/// с каждого узла частиц то, что описывает рождение, — см. <see cref="EmissionProfile"/>.
///
/// ПОЧЕМУ РАЗБОР, А НЕ ПРАВКА СЦЕН ХУДОЖНИКОМ. Настройки рождения можно было бы объявить
/// прямо ресурсом, но тогда художник потерял бы и предпросмотр эффекта в редакторе,
/// и привычный разбор частиц в инспекторе, а согласовывать ресурс с видом пришлось бы
/// вручную. Разбор оставляет источником правды саму сцену: правка узла расходится
/// в игру сама.
///
/// КЭШ ПО СЦЕНЕ. Одна и та же сцена попадания объявлена у десятка видов оружия, а разбирать
/// её каждый раз значило бы поднимать экземпляр на каждое объявление. Разобранное хранится
/// по ссылке на сцену и живёт всю партию: сцен эффектов немного, и они не меняются.
///
/// В РЕДАКТОРЕ КЭШ НЕ ДЕЙСТВУЕТ: сцену правят прямо сейчас, и запомненный разбор показывал бы
/// поле редактора контента таким, каким оно было до правки.
/// </summary>
public static class ParticleBake
{
    private static readonly Dictionary<PackedScene, EffectBake> Cache = new();

    /// <summary>
    /// Разобрать сцену эффекта. Экземпляр поднимается только ради разбора и освобождается
    /// тут же: в дерево мира он не входит, поэтому ни <c>_Ready</c>, ни выдача частиц
    /// у него не отрабатывают.
    /// </summary>
    public static EffectBake Of(PackedScene scene)
    {
        if (scene == null)
            return null;

        if (!Engine.IsEditorHint() && Cache.TryGetValue(scene, out var known))
            return known;

        if (scene.Instantiate() is not Node2D root)
        {
            GD.PushWarning($"[ParticleBake] корень сцены {scene.ResourcePath} не Node2D");
            return null;
        }

        var baked = Of(root);
        root.Free();

        if (!Engine.IsEditorHint())
            Cache[scene] = baked;

        return baked;
    }

    /// <summary>
    /// Разобрать живой узел эффекта. Так печётся то, что лежит прямо в сцене модели:
    /// пыль хода и вспышка выстрела — их сцена уже поднята запеканием модели, и поднимать
    /// её второй раз незачем.
    /// </summary>
    public static EffectBake Of(Node2D root)
    {
        var baked = new EffectBake();

        if (root == null)
            return baked;

        var parts = new List<EmissionProfile>();
        var stamps = new List<StampProfile>();

        // Сам корень тоже разбирается: пыль хода объявлена узлом частиц без обёртки,
        // тогда как вспышка и взрыв держат части под корнем-пустышкой
        Walk(root, Transform2D.Identity, parts, stamps, baked);

        baked.Parts = parts.ToArray();
        baked.Stamps = stamps.ToArray();

        foreach (var part in baked.Parts)
            baked.Duration = Mathf.Max(baked.Duration, part.Key.Lifetime);

        return baked;
    }

    /// <summary>Забыть разобранное. Зовётся перезапуском партии — см. <see cref="EffectSystem"/>.</summary>
    public static void Forget() => Cache.Clear();

    private static void Walk(Node node, Transform2D at, List<EmissionProfile> parts,
        List<StampProfile> stamps, EffectBake baked)
    {
        switch (node)
        {
            case GpuParticles2D emitter:
                Describe(emitter, at, parts, baked);
                break;

            case ScorchStamp stamp when stamp.Decal != null:
                stamps.Add(new StampProfile
                {
                    Decal = stamp.Decal,
                    Offset = at.Origin,
                    MinSize = stamp.MinSize,
                    MaxSize = stamp.MaxSize,
                });
                break;
        }

        foreach (var child in node.GetChildren())
        {
            var step = child is Node2D placed ? at * placed.Transform : at;
            Walk(child, step, parts, stamps, baked);
        }
    }

    /// <summary>
    /// Снять настройки рождения с одного узла частиц.
    ///
    /// НЕВИДИМЫЙ УЗЕЛ ПРОПУСКАЕТСЯ. Художник гасит признак видимости, откладывая часть
    /// эффекта «на потом», и в игре она не показывалась; запекание обязано это сохранить.
    /// </summary>
    private static void Describe(GpuParticles2D emitter, Transform2D at,
        List<EmissionProfile> parts, EffectBake baked)
    {
        if (!emitter.Visible || emitter.Amount <= 0 || emitter.Lifetime <= 0.0)
            return;

        if (emitter is MovementParticles dust)
        {
            baked.MinSpeed = dust.MinSpeed;
            baked.FullSpeed = dust.FullSpeed;
            baked.StopDelay = dust.StopDelay;
        }

        var layer = emitter is MovementParticles
            ? WorldLayer.GroundEffects
            : WorldLayer.AirEffects;

        var profile = new EmissionProfile
        {
            Key = new ParticleKey(emitter, layer),
            Offset = at.Origin,
            Angle = at.Rotation,
            Count = emitter.Amount,
            Rate = emitter.Amount / (float)emitter.Lifetime,
        };

        if (emitter.ProcessMaterial is ParticleProcessMaterial process)
        {
            profile.SpeedMin = process.InitialVelocityMin;
            profile.SpeedMax = process.InitialVelocityMax;
            profile.Spread = Mathf.DegToRad(process.Spread);
            profile.Course = Course(process.Direction);
            profile.Radius = Radius(process);
        }

        parts.Add(profile);
    }

    /// <summary>
    /// Направление разлёта плоской частью вектора.
    ///
    /// ПУСТОЕ НАПРАВЛЕНИЕ ЗНАЧИТ ОСЬ ЧАСТИ. Художник обнуляет вектор, когда разлёт должен
    /// идти вдоль самого узла, а не в объявленную сторону; ось X узла и есть та сторона,
    /// в которую он повёрнут.
    /// </summary>
    private static float Course(Vector3 direction)
    {
        var flat = new Vector2(direction.X, direction.Y);
        return flat.LengthSquared() < 0.000001f ? 0f : flat.Angle();
    }

    /// <summary>
    /// Разброс места рождения. Читается только у шара: прочие формы выдачи в сценах
    /// эффектов не встречаются, а угадывать их разброс по чужим полям значило бы
    /// показывать не то, что настроено.
    /// </summary>
    private static float Radius(ParticleProcessMaterial process) =>
        process.EmissionShape is ParticleProcessMaterial.EmissionShapeEnum.Sphere
            or ParticleProcessMaterial.EmissionShapeEnum.SphereSurface
            ? process.EmissionSphereRadius
            : 0f;
}
