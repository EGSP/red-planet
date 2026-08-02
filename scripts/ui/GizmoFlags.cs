using System.Collections.Generic;
using Godot;

/// <summary>
/// Вид области, которую можно показать поверх сущности: зрение, атака, рабочая рука.
/// Новые инструменты (радар и т.п.) добавляются сюда и в <see cref="GizmoTools"/>.
/// </summary>
public enum GizmoKind
{
    Vision,
    Attack,
    Work,
}

/// <summary>
/// Матрица включения одного вида отрисовки: «все», «союзники», «враги».
///
/// Если включено «все», остальные два поля не читаются. Иначе смотрятся по отдельности
/// для стороны сущности. Союзник здесь — сторона игрока, враг — <see cref="Faction.Hostile"/>.
/// </summary>
public sealed class GizmoFilter
{
    public bool All;
    public bool Ally;
    public bool Enemy;

    public bool Any => All || Ally || Enemy;

    public bool Matches(Faction faction)
    {
        if (All)
            return true;

        return faction == Faction.Player ? Ally : Enemy;
    }
}

/// <summary>
/// Какие круги инструментов есть у сущности. Единственное место, где перечислен набор
/// для отрисовки: сейчас зрение, атака и рука; позже сюда же войдёт радар.
///
/// Логика боя и стройки эти числа читает из справочника сама; здесь только то,
/// что позволено показать игроку как область.
/// </summary>
public readonly record struct GizmoTools(float VisionRadius, WeaponDefinition Weapon, float WorkRadius)
{
    public float AttackRadius => Weapon?.RangePx ?? 0f;

    public static GizmoTools From(UnitDefinition def)
    {
        if (def == null)
            return default;

        return new GizmoTools(
            def.VisionRadiusPx,
            def.Weapon,
            def.BuildTool != null ? def.WorkRangePx : 0f);
    }
}

/// <summary>
/// Признаки отрисовки областей юнитов. Панель пишет, отрисовщики читают.
///
/// По умолчанию всё выключено: в обычном геймплее круги зрения и атаки не нужны.
/// Включаются матрицей на вкладке <c>giz</c>, зажатым Ctrl при выделении или режимом
/// постановки вооружённой постройки (покрытие турелей).
/// </summary>
public static class GizmoFlags
{
    public static readonly GizmoFilter Vision = new();
    public static readonly GizmoFilter Attack = new();
    public static readonly GizmoFilter Work = new();

    public static GizmoFilter Of(GizmoKind kind) => kind switch
    {
        GizmoKind.Vision => Vision,
        GizmoKind.Attack => Attack,
        GizmoKind.Work => Work,
        _ => Vision,
    };
}

/// <summary>
/// Решает, рисовать ли область у конкретной сущности в этом кадре.
///
/// Источники разрешения, по убыванию узкости:
/// 1. матрица отладки (<see cref="GizmoFlags"/>);
/// 2. Ctrl при непустом выделении — инструменты выделенных;
/// 3. постановка постройки со стволом — радиусы атаки уже стоящих вооружённых построек.
/// </summary>
public static class GizmoGate
{
    private static IReadOnlyList<IOrderable> _selected = System.Array.Empty<IOrderable>();

    /// <summary>Ctrl зажат и есть выделение: показать инструменты выделенных.</summary>
    public static bool ShowSelectedTools { get; private set; }

    /// <summary>В панели выбрана постройка со стволом: показать покрытие турелей.</summary>
    public static bool ShowArmedCoverage { get; private set; }

    /// <summary>
    /// Обновить кадровые условия. Вызывает <see cref="CommandSystem"/>: только он знает
    /// выделение и выбранную для стройки постройку.
    /// </summary>
    public static void Refresh(IReadOnlyList<IOrderable> selected, UnitDefinition pending)
    {
        _selected = selected ?? System.Array.Empty<IOrderable>();
        ShowSelectedTools = _selected.Count > 0 && Input.IsKeyPressed(Key.Ctrl);
        ShowArmedCoverage = pending?.Weapon != null;
    }

    public static bool IsSelected(object entity)
    {
        for (int i = 0; i < _selected.Count; i++)
        {
            if (ReferenceEquals(_selected[i], entity))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Разрешена ли отрисовка данного вида для сущности.
    /// <paramref name="armedStructure"/> — готовая постройка со стволом (турель),
    /// нужна правилу покрытия при постановке.
    /// </summary>
    public static bool Allows(GizmoKind kind, Faction faction, bool selected,
        bool armedStructure = false)
    {
        if (GizmoFlags.Of(kind).Matches(faction))
            return true;

        if (ShowSelectedTools && selected)
            return true;

        if (kind == GizmoKind.Attack && ShowArmedCoverage && armedStructure)
            return true;

        return false;
    }
}

/// <summary>
/// Отрисовка областей инструментов с учётом <see cref="GizmoGate"/>.
/// Сущности зовут один метод вместо трёх разрозненных вызовов gizmo.
/// </summary>
public static class UnitGizmos
{
    public static void Draw(CanvasItem canvas, in GizmoTools tools, Faction faction,
        bool selected = false, bool armedStructure = false, float facingOffset = 0f)
    {
        if (tools.VisionRadius > 0f &&
            GizmoGate.Allows(GizmoKind.Vision, faction, selected))
            VisionGizmo.Draw(canvas, tools.VisionRadius);

        if (tools.Weapon != null &&
            GizmoGate.Allows(GizmoKind.Attack, faction, selected, armedStructure))
            WeaponGizmo.Draw(canvas, tools.Weapon, facingOffset);

        if (tools.WorkRadius > 0f &&
            GizmoGate.Allows(GizmoKind.Work, faction, selected))
            WorkGizmo.Draw(canvas, tools.WorkRadius);
    }
}

/// <summary>Круг рабочей руки / манипулятора — тот же стиль, что у коммандера раньше.</summary>
public static class WorkGizmo
{
    public static void Draw(CanvasItem canvas, float radius)
    {
        if (radius <= 0f)
            return;

        ShapeDraw.Circle(canvas, Vector2.Zero, radius, DrawTheme.Radius(VizKind.Work), 48);
    }
}
