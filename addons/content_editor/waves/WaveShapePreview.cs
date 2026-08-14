using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Готовые к отрисовке сведения о том, как волна встанет на карте: форма появления,
/// набранный состав и места каждой машины.
///
/// ПОЧЕМУ СЧИТАЕТ НЕ САМА ПАНЕЛЬ. Геометрию и состав считают <see cref="WaveFormation"/>
/// и <see cref="WaveComposer"/> — те же классы, что работают в партии. Панель обязана
/// показывать построение, а не похожее на него: иначе подбирать по ней числа нельзя.
/// Здесь собирается всё, что этим классам нужно, и результат передаётся отрисовке.
///
/// ЧЕРНОВИК ВАЖНЕЕ КАТАЛОГА. Волна, не прошедшая проверку, в каталог не попадает, а числа
/// формы подбирают как раз в такие моменты. Поэтому при отсутствии собранного определения
/// оно составляется из черновика: списки видов связываются с каталогом по идентификаторам,
/// а непригодные ссылки просто отбрасываются.
/// </summary>
public sealed class WaveShapePreview
{
    /// <summary>Настройки мира: радиус поля застройки и окружность появления.</summary>
    private const string WorldSettingsPath = "res://resources/tuning/world.tres";

    /// <summary>Умолчания формы волн и темпа.</summary>
    private const string WaveSettingsPath = "res://resources/tuning/waves.tres";

    /// <summary>Форма появления с подставленными умолчаниями подсистемы.</summary>
    public WaveShape Shape { get; private init; }

    /// <summary>Состав волны, упорядоченный по убыванию боевой мощи, как в партии.</summary>
    public IReadOnlyList<UnitDefinition> Ordered { get; private init; } = Array.Empty<UnitDefinition>();

    /// <summary>Число очагов, не превышающее числа машин.</summary>
    public int Groups { get; private init; }

    /// <summary>Направление первого очага, радиан.</summary>
    public float CenterAngle { get; private init; }

    /// <summary>Радиус окружности появления, пикселей мира.</summary>
    public float SpawnRadiusPx { get; private init; }

    /// <summary>Радиус поля застройки, пикселей мира: он же указывает, куда идёт волна.</summary>
    public float FieldRadiusPx { get; private init; }

    /// <summary>Бюджет волны при выбранном показателе террора.</summary>
    public float Budget { get; private init; }

    /// <summary>Израсходованная часть бюджета: остаток означает, что его некому занять.</summary>
    public float Spent { get; private init; }

    /// <summary>Состав перечислением видов с количествами.</summary>
    public string Composition { get; private init; } = "";

    /// <summary>Подходит ли волна к выбранному показателю террора.</summary>
    public bool Applicable { get; private init; }

    /// <summary>Собрано ли определение волны каталогом либо составлено из черновика.</summary>
    public bool FromDraft { get; private init; }

    /// <summary>Число рядов построения при заданной глубине и промежутке.</summary>
    public int Rows => WaveFormation.Rows(Shape);

    /// <summary>Места всех машин очага с их видами.</summary>
    public IEnumerable<(Vector2 Position, UnitDefinition Definition)> Slots()
    {
        for (int group = 0; group < Groups; group++)
        {
            float angle = WaveFormation.GroupAngle(Shape, CenterAngle, group);
            int index = 0;

            // Состав раскладывается по очагам через один, как это делает WaveSystem:
            // иначе первый очаг забирал бы всё тяжёлое, а последний — всё лёгкое.
            for (int i = group; i < Ordered.Count; i += Groups)
            {
                var (position, _) = WaveFormation.Slot(Shape, angle, index++);
                yield return (position, Ordered[i]);
            }
        }
    }

    /// <summary>Направление каждого очага, радиан.</summary>
    public IEnumerable<float> GroupAngles()
    {
        for (int group = 0; group < Groups; group++)
            yield return WaveFormation.GroupAngle(Shape, CenterAngle, group);
    }

    /// <summary>
    /// Построить предпросмотр для выбранной волны. Возвращает <c>null</c>, если волны нет:
    /// панель в этом случае показывает пояснение, а не пустую сетку.
    /// </summary>
    public static WaveShapePreview Build(
        ContentEditorStore store,
        OpenEntitySession session,
        float terror,
        float directionDegrees,
        int seed)
    {
        if (store == null || session == null || session.Kind != ContentEntityKind.Wave)
            return null;

        var world = Load<WorldSettings>(WorldSettingsPath) ?? new WorldSettings();

        // Настройка волн читается по действующему пути, а не по постоянному: страница
        // глобальных параметров позволяет назначить партии другой вариант ресурса,
        // и панель формы обязана показывать построение по тому же, по какому оно пойдёт.
        var waves = Load<WaveSettings>(TuningCatalog.ActivePath("waves") ?? WaveSettingsPath)
                    ?? new WaveSettings();

        // WaveSettings.ShapeOf берёт WaveStart из настроек мира, когда волна его не задала.
        // Стенд предпросмотра мира поступает так же: подставляет правимый ресурс в World,
        // иначе форма считалась бы по умолчаниям класса, а не по числам проекта.
        World.Settings = world;

        var definition = store.Catalog.Wave(session.Id);
        bool fromDraft = definition == null;
        definition ??= DefinitionFromDraft(store, session);

        var shape = waves.ShapeOf(definition.Shape, world.SpawnRadiusPx);
        float budget = definition.Budget(terror);

        var composer = new WaveComposer();
        var rng = new RandomNumberGenerator { Seed = (ulong)Math.Max(seed, 1) };
        composer.Compose(store.Catalog, definition, budget, rng);

        var ordered = new List<UnitDefinition>(composer.Composition);
        ordered.Sort((left, right) => right.ArmyPower.CompareTo(left.ArmyPower));

        return new WaveShapePreview
        {
            Shape = shape,
            Ordered = ordered,
            Groups = Mathf.Max(Mathf.Min(shape.Groups, ordered.Count), 1),
            CenterAngle = Mathf.DegToRad(directionDegrees),
            SpawnRadiusPx = world.SpawnRadiusPx,
            FieldRadiusPx = Mathf.Max(world.Radius, 1) * Const.Unit,
            Budget = budget,
            Spent = composer.Spent,
            Composition = composer.Describe(),
            Applicable = definition.Fits(terror),
            FromDraft = fromDraft,
        };
    }

    /// <summary>
    /// Определение волны, составленное из черновика. Нужно ровно тогда, когда файл ещё
    /// не проходит проверку связности, то есть в разгар правки.
    /// </summary>
    private static WaveDefinition DefinitionFromDraft(ContentEditorStore store, OpenEntitySession session)
    {
        var overview = store.WaveOverview(session.Id);
        var definition = new WaveDefinition
        {
            Id = session.Id,
            DisplayName = session.DisplayName,
            TerrorMin = overview?.TerrorMin ?? -1f,
            TerrorMax = overview?.TerrorMax ?? -1f,
            ArmyPowerBudget = overview?.Budget ?? 10f,
            ArmyPowerPerTerror = overview?.BudgetPerTerror ?? 0f,
            Shape = WaveDraftReader.ShapeOverrides(session.DraftText),
        };

        foreach (var list in WaveDraftReader.UnitLists(session.DraftText))
        {
            var units = list.UnitIds
                .Select(store.PreviewUnit)
                .Where(unit => unit != null)
                .ToList();

            definition.UnitLists.Add(new WaveUnitList
            {
                Mode = Enum.TryParse(list.Mode, ignoreCase: true, out UnitListMode mode)
                    ? mode
                    : UnitListMode.Allow,
                UnitIds = list.UnitIds,
                TargetBudgetShare = list.Share,
                Units = units,
            });
        }

        return definition;
    }

    /// <summary>
    /// Прочитать ресурс настройки. Отсутствие файла не является отказом: панель тогда
    /// показывает форму по умолчаниям класса и остаётся работоспособной.
    /// </summary>
    private static T Load<T>(string path) where T : Resource
    {
        try
        {
            return ResourceLoader.Exists(path) ? ResourceLoader.Load<T>(path) : null;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Content editor] tuning resource {path} was not read: {ex.Message}");
            return null;
        }
    }
}
