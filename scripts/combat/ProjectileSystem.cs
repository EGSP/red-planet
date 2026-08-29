using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Набор готовых снарядов одной сцены изображения. Массив свободных узлов и счёт того,
/// сколько их всего создано, — больше набору знать нечего.
///
/// НАБОР ЗАВЕДЁН НА СЦЕНУ, А НЕ НА СТВОЛ, поскольку сцену два ствола вправе делить: разнести
/// их по разным наборам значило бы держать вдвое больше узлов ради одного и того же
/// изображения. Всё, чем выстрелы одной сцены различаются, назначается при выдаче.
/// </summary>
internal sealed class ProjectilePool
{
    /// <summary>Сцена изображения. Null — набор безмодельных снарядов, рисуемых кругом.</summary>
    public PackedScene Scene;

    /// <summary>Свободные узлы. Выдача снимает с конца, возврат кладёт туда же.</summary>
    public Projectile[] Free = Array.Empty<Projectile>();

    /// <summary>Сколько ячеек <see cref="Free"/> занято.</summary>
    public int Count;

    /// <summary>Сколько узлов создано всего — и свободных, и летящих. По нему идёт удвоение.</summary>
    public int Total;
}

/// <summary>
/// Двигает снаряды, даёт им проверить попадание и владеет их узлами.
///
/// ПОЛЁТ ОТДЕЛЬНОЙ СИСТЕМОЙ, а не в _Process самих снарядов, потому что он есть часть
/// симуляции и обязан идти в том же такте и в том же порядке, что стрельба и урон.
///
/// НАБОРЫ ГОТОВЫХ УЗЛОВ ЛЕЖАТ ЗДЕСЬ, поскольку выбытие снаряда система и замечает: она одна
/// обходит все снаряды каждый шаг. Рождение по-прежнему проходит через
/// <see cref="Spawner.SpawnProjectile"/> — точка появления всего в мире остаётся одна,
/// но узел ей теперь выдаёт набор.
///
/// ЧТО ИМЕННО ЭТИМ СБЕРЕГАЕТСЯ. Не вызов <c>new</c>, а перестройка дерева: узел снаряда
/// подвешивается к слою один раз за всю партию и больше его не покидает. Выстрел стоит
/// снятия ячейки с конца массива и трёх записей в узел, а попадание — возврата ячейки.
/// </summary>
public partial class ProjectileSystem : GameSystem
{
    /// <summary>
    /// Сколько узлов создаётся в наборе изначально. Исчерпание набора удваивает его:
    /// сотня снарядов в воздухе означает, что и впредь их будет сотня, а не десяток,
    /// и добирать по одному значило бы растянуть создание узлов на весь бой.
    /// </summary>
    [Export(PropertyHint.Range, "8,512,8")] public int PoolSize { get; set; } = 64;

    /// <summary>
    /// Чем показывается снаряд ствола, не объявившего своей сцены. Не назначена — снаряд
    /// рисуется кругом цвета <c>projectile_color</c>, как было до появления моделей.
    /// </summary>
    [Export] public PackedScene DefaultModel { get; set; }

    /// <summary>Наборы по сценам. Безмодельные снаряды лежат отдельно — ключа у них нет.</summary>
    private readonly Dictionary<PackedScene, ProjectilePool> _pools = new();

    private ProjectilePool _bare;

    /// <summary>Снаряд, ждущий возврата в набор, пока догорает его след.</summary>
    private struct Cooling
    {
        public Projectile Node;
        public float Left;
    }

    private readonly List<Cooling> _cooling = new();

    public override void Step(double dt)
    {
        Cool(dt);

        foreach (var projectile in GM.Index.All<Projectile>())
        {
            projectile.Step(dt);

            if (projectile.Spent)
                Recycle(projectile);
        }
    }

    /// <summary>
    /// Выдать узел под новый выстрел. Пустая сцена означает ствол без объявления: берётся
    /// общая, а нет и её — набор безмодельных.
    ///
    /// Поля выстрела назначает вызывающий: набор о них ничего не знает, и знать ему нечего.
    /// </summary>
    public Projectile Take(PackedScene scene)
    {
        var pool = PoolOf(scene ?? DefaultModel);

        if (pool.Count == 0)
            Fill(pool);

        var projectile = pool.Free[--pool.Count];
        pool.Free[pool.Count] = null;

        projectile.Wake();
        return projectile;
    }

    /// <summary>
    /// Убрать отлетавший снаряд. Возврат в набор откладывается на срок догорания следа:
    /// частицы объявлены в мировых координатах и остаются висеть там, где снаряд летел,
    /// а выдача того же узла под новый выстрел оборвала бы их.
    ///
    /// Даже при нулевом сроке узел пропускает один шаг: снятие с индекса применяется
    /// уборкой в конце кадра, и выдача того же узла до неё смешала бы два выстрела в один.
    /// </summary>
    private void Recycle(Projectile projectile)
    {
        projectile.Park();

        _cooling.Add(new Cooling
        {
            Node = projectile,
            Left = projectile.Model?.Linger ?? 0f,
        });
    }

    /// <summary>Досчитать сроки догорания и вернуть в наборы то, что своё отжило.</summary>
    private void Cool(double dt)
    {
        for (int i = _cooling.Count - 1; i >= 0; i--)
        {
            var item = _cooling[i];
            item.Left -= (float)dt;

            if (item.Left > 0f)
            {
                _cooling[i] = item;
                continue;
            }

            _cooling.RemoveAt(i);

            if (!Alive.Is(item.Node) || item.Node.Home is not { } pool)
                continue;

            item.Node.Sleep();
            Push(pool, item.Node);
        }
    }

    private ProjectilePool PoolOf(PackedScene scene)
    {
        if (scene == null)
            return _bare ??= new ProjectilePool();

        if (_pools.TryGetValue(scene, out var pool))
            return pool;

        pool = new ProjectilePool { Scene = scene };
        _pools[scene] = pool;
        return pool;
    }

    /// <summary>
    /// Пополнить исчерпанный набор. Первый раз создаётся <see cref="PoolSize"/> узлов,
    /// далее — столько же, сколько уже есть: набор удваивается.
    /// </summary>
    private void Fill(ProjectilePool pool)
    {
        int batch = pool.Total > 0 ? pool.Total : Mathf.Max(PoolSize, 1);

        for (int i = 0; i < batch; i++)
            Push(pool, Create(pool));

        pool.Total += batch;
    }

    /// <summary>
    /// Создать узел вместе с его изображением и подвесить к слою снарядов. Единственное
    /// место, где дерево узлов перестраивается: дальше узел живёт до конца партии.
    /// </summary>
    private Projectile Create(ProjectilePool pool)
    {
        var projectile = new Projectile { Home = pool };

        if (pool.Scene?.Instantiate() is ProjectileModel model)
            projectile.Attach(model);
        else if (pool.Scene != null)
            GD.PushError($"[ProjectileSystem] корень сцены {pool.Scene.ResourcePath} " +
                         "не является ProjectileModel");

        GM.Playground.Add(WorldLayer.Projectiles, projectile);

        // Свежий узел ничем не отличается от вернувшегося: он гаснет и объявляет себя
        // выбывшим, чтобы до первой выдачи не попасться ни отрисовке, ни проверкам живости.
        // Гасится он сразу целиком — следа, который надо было бы дать догореть, у него нет
        projectile.Park();
        projectile.Sleep();

        return projectile;
    }

    private static void Push(ProjectilePool pool, Projectile projectile)
    {
        if (pool.Count == pool.Free.Length)
            Array.Resize(ref pool.Free, Mathf.Max(pool.Free.Length * 2, 8));

        pool.Free[pool.Count++] = projectile;
    }
}
