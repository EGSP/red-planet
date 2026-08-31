using System.Collections.Generic;
using Godot;

/// <summary>
/// Корень сцены снаряда: спрайт, след, свечение — всё изображение того, что летит.
///
/// ЗАЧЕМ СЦЕНА, А НЕ ЧИСЛА СПРАВОЧНИКА. Снаряд оставался последней сущностью, чей вид
/// задавался ключами .toml (<c>projectile_color</c>) и рисовался кругом с хвостом прямо
/// в <see cref="Projectile._Draw"/>. Это то самое состояние, из которого выводили модели
/// юнитов: изображение описано числами, и всякая правка требует правки кода. Сцена даёт
/// спрайт, частицы следа и порядок наложения готовыми.
///
/// ГРАНИЦА С СИМУЛЯЦИЕЙ ТА ЖЕ, ЧТО У <see cref="UnitModel"/>. Радиус снаряда есть
/// одновременно размер попадания и число расчёта, поэтому он остаётся в справочнике;
/// модель задаёт только рисунок, а его соразмерность — множителем
/// <see cref="ProjectileDeclaration.Size"/>. Ни урона, ни скорости, ни срока жизни модель
/// не знает.
///
/// ЭКЗЕМПЛЯР ЖИВЁТ ДОЛЬШЕ ОДНОГО ВЫСТРЕЛА. Снарядов за бой тысячи, и поднимать сцену
/// на каждый значило бы перестраивать дерево узлов десятки раз в секунду. Экземпляры
/// выдаются набором готовых (<see cref="ProjectileSystem"/>), поэтому узел обязан уметь
/// не только начинать след, но и гасить его, ничего о событии не зная.
/// </summary>
[Tool, GlobalClass]
public partial class ProjectileModel : Node2D
{
    /// <summary>
    /// Основной спрайт снаряда. Ссылка нужна коду, которому важен именно он, а не всё
    /// дерево; на отрисовку не влияет — рисует себя сам узел.
    /// </summary>
    [Export] public Sprite2D Body { get; set; }

    /// <summary>
    /// Сколько секунд следу догорать после гашения. Ноль означает «взять по частицам»:
    /// узел выведет срок сам из longest <see cref="GpuParticles2D.Lifetime"/>.
    ///
    /// ЗАЧЕМ ЭТО ВООБЩЕ ЕСТЬ. След объявлен в мировых координатах и потому остаётся
    /// висеть там, где снаряд летел. Возврат экземпляра в набор сразу после попадания
    /// обрубил бы его на середине, поэтому возврат откладывается на этот срок.
    /// </summary>
    [Export(PropertyHint.Range, "0,4,0.05")] public float TrailLinger { get; set; }

    private GpuParticles2D[] _trails;
    private CanvasItem[] _parts;

    /// <summary>
    /// Части следа. Собираются один раз при первом обращении: состав сцены во время игры
    /// не меняется.
    /// </summary>
    public GpuParticles2D[] Trails => _trails ??= Collect<GpuParticles2D>();

    /// <summary>
    /// Сколько ждать перед возвратом экземпляра в набор. Заданный срок главнее выведенного:
    /// частицы бывают с задержкой начала, и подобрать его на глаз художнику проще, чем
    /// выводить из полей.
    /// </summary>
    public float Linger
    {
        get
        {
            if (TrailLinger > 0f)
                return TrailLinger;

            float longest = 0f;

            foreach (var trail in Trails)
                if (Alive.Is(trail) && trail.SpeedScale > 0f)
                    longest = Mathf.Max(longest, (float)(trail.Lifetime / trail.SpeedScale));

            return longest;
        }
    }

    /// <summary>
    /// Видимые части — прямые потомки корня, не являющиеся частицами. Гасятся они отдельно
    /// от следа, и отсюда требование к сцене: УЗЛЫ СЛЕДА ОБЯЗАНЫ ЛЕЖАТЬ ПРЯМО В КОРНЕ.
    ///
    /// ЗАЧЕМ ТАКОЕ РАЗДЕЛЕНИЕ. След объявлен в мировых координатах и после попадания
    /// догорает там, где летел снаряд. Погаси узел целиком — вместе с ним пропали бы
    /// и эти частицы, поскольку невидимость в Godot наследуется потомками. Поэтому
    /// попадание гасит только рисунок, а сам узел уходит в набор позже, когда следу
    /// уже нечего показывать.
    /// </summary>
    private CanvasItem[] Parts => _parts ??= Visible2D();

    /// <summary>Показать или скрыть рисунок снаряда, не трогая след.</summary>
    public void Show(bool shown)
    {
        foreach (var part in Parts)
            if (Alive.Is(part))
                part.Visible = shown;
    }

    private CanvasItem[] Visible2D()
    {
        var found = new List<CanvasItem>();

        foreach (var child in GetChildren())
            if (child is CanvasItem item and not GpuParticles2D)
                found.Add(item);

        return found.ToArray();
    }

    /// <summary>
    /// В игре выдача гасится принудительно — по той же причине, по какой это делает
    /// <see cref="BurstParticles"/>: художник включает её в сцене, чтобы видеть след,
    /// и признак сохраняется в файл. Без сброса лежащие в наборе снаряды сыпали бы след
    /// на месте своего появления.
    /// </summary>
    public override void _Ready()
    {
        if (!Engine.IsEditorHint())
            StopTrail();
    }

    /// <summary>Начать след заново. Зовётся при выдаче снаряда из набора.</summary>
    public void StartTrail()
    {
        foreach (var trail in Trails)
            if (Alive.Is(trail))
            {
                trail.Restart();
                trail.Emitting = true;
            }
    }

    /// <summary>
    /// Прекратить выдачу частиц, НЕ убирая уже выпущенные: они догорают там, где летел
    /// снаряд, — см. <see cref="Linger"/>.
    /// </summary>
    public void StopTrail()
    {
        foreach (var trail in Trails)
            if (Alive.Is(trail))
                trail.Emitting = false;
    }

    private T[] Collect<T>() where T : Node
    {
        var found = new List<T>();
        Walk(this, found);
        return found.ToArray();
    }

    private static void Walk<T>(Node node, List<T> found) where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T match)
                found.Add(match);

            Walk(child, found);
        }
    }
}
