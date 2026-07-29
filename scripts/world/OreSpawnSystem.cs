using Godot;

/// <summary>
/// Держит слоты руды заполненными: месторождения появляются в кольце вокруг базы,
/// новое — только когда слот освободился.
/// </summary>
public partial class OreSpawnSystem : GameSystem
{
    [Export] public PackedScene OreScene;

    private readonly RandomNumberGenerator _rng = new();
    private float _timer;
    private bool _seeded;

    protected override void OnRegister() => _rng.Randomize();

    public override void Step(double dt)
    {
        // Стартовое заполнение — уже после того, как Main назначил WorldRoot
        if (!_seeded)
        {
            if (GM.WorldRoot == null)
                return;

            for (int i = 0; i < Const.OreSlots; i++)
                TrySpawn();

            _seeded = true;
            return;
        }

        if (GM.Index.All<OreDeposit>().Count >= Const.OreSlots)
            return;

        _timer -= (float)dt;
        if (_timer > 0f)
            return;

        _timer = Const.OreRespawnDelay;
        TrySpawn();
    }

    private void TrySpawn()
    {
        if (OreScene == null)
            return;

        for (int attempt = 0; attempt < 40; attempt++)
        {
            float angle = _rng.RandfRange(0f, Mathf.Tau);
            float radius = _rng.RandfRange(Const.OreRingMin, Const.OreRingMax);

            var cell = new Vector2I(
                Mathf.RoundToInt(Mathf.Cos(angle) * radius),
                Mathf.RoundToInt(Mathf.Sin(angle) * radius));

            if (!GM.Grid.InBounds(cell) || GM.Grid.IsOccupied(cell))
                continue;

            GM.Spawn.SpawnOre(OreScene, cell);
            return;
        }
    }
}
