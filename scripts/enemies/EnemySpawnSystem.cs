using System.Collections.Generic;
using Godot;

/// <summary>
/// Держит на карте постоянное давление: врагов всегда примерно поровну, погиб один —
/// через паузу приходит следующий. Ровно та же механика, что у слотов руды, только
/// вместо месторождений — противник.
///
/// Появляются они на окружности вокруг базы радиусом с внешнее кольцо руды, взятым
/// с запасом (Const.EnemyRingFactor): дальние месторождения оказываются на линии подхода,
/// и добыча на краю кольца перестаёт быть безопасной.
/// </summary>
public partial class EnemySpawnSystem : GameSystem
{
    [Export] public PackedScene EnemyScene;

    /// <summary>Сколько врагов держим на карте одновременно.</summary>
    [Export] public int Population = Const.EnemyPopulation;

    /// <summary>Пауза между появлениями, секунд.</summary>
    [Export] public float Delay = Const.EnemySpawnDelay;

    /// <summary>Отсрочка первого врага — время развернуть базу.</summary>
    [Export] public float FirstDelay = Const.EnemyFirstDelay;

    private readonly RandomNumberGenerator _rng = new();
    private float _timer;

    protected override void OnRegister()
    {
        _rng.Randomize();
        _timer = FirstDelay;
    }

    public override void Step(double dt)
    {
        if (GM.Playground == null || EnemyScene == null)
            return;

        if (GM.Index.All<Enemy>().Count >= Population)
            return;

        _timer -= (float)dt;
        if (_timer > 0f)
            return;

        _timer = Delay;
        Spawn();
    }

    private void Spawn()
    {
        var def = PickType();
        if (def == null)
            return;

        float angle = _rng.RandfRange(0f, Mathf.Tau);
        var position = Heading.Forward(angle) * Const.EnemySpawnRadiusPx;

        var enemy = GM.Spawn.SpawnEnemy(EnemyScene, def, position);

        GM.Events.Append(new EnemySpawned
        {
            EntityId = enemy.Id,
            DefId = def.Id,
            Pos = position,
        });
    }

    /// <summary>Вид выбирается по весам справочника: вес — доля вида в потоке.</summary>
    private EnemyDef PickType()
    {
        var types = new List<EnemyDef>();
        float total = 0f;

        foreach (var def in GM.Catalog.Enemies)
        {
            if (def.SpawnWeight <= 0f)
                continue;

            types.Add(def);
            total += def.SpawnWeight;
        }

        if (types.Count == 0)
            return null;

        float roll = _rng.RandfRange(0f, total);

        foreach (var def in types)
        {
            roll -= def.SpawnWeight;
            if (roll <= 0f)
                return def;
        }

        return types[^1];
    }
}
