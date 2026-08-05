using Godot;

/// <summary>
/// Биом: область местности, отобранная по температуре, и набор декалей, свойственных ей.
///
/// СОБСТВЕННЫХ ТЕКСТУР У БИОМА НЕТ. Базовый слой общий для всей местности и делится между
/// покрытиями по шуму, а не по температуре. Биом отвечает за то, чем поверхность усыпана,
/// а не за то, из чего она сделана: так устроено и в Planetary Annihilation, где подбиом
/// даёт одну тайлящуюся текстуру и список декалей.
/// </summary>
[Tool]
[GlobalClass]
public partial class SurfaceBiome : Resource
{
    /// <summary>Имя для чтения в инспекторе.</summary>
    [Export] public string Id = "";

    /// <summary>Отрезок температуры, на котором биом появляется.</summary>
    [Export] public Vector2 TemperatureRange = new(0f, 1f);

    /// <summary>
    /// Ширина размытия границы биома. Кандидат у самого края принимается тем реже, чем он
    /// ближе к границе, отчего стык двух биомов получается рваным, а не отрезанным по линии.
    /// </summary>
    [Export(PropertyHint.Range, "0,0.5,0.005")] public float Falloff = 0.05f;

    /// <summary>Декали, свойственные этому биому.</summary>
    [Export] public SurfaceDecal[] Decals = System.Array.Empty<SurfaceDecal>();
}
