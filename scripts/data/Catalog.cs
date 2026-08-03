using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Справочники — библиотека определений, собранных из .toml: чем является «завод»,
/// сколько стоит, какой он формы, чем снабжён.
///
/// Только чтение и только шаблоны. Каталог ничего не создаёт и не знает о живых сущностях:
/// создание вынесено в Spawner, живые экземпляры лежат в EntityStore.
///
/// Зачем нужен вообще, если файл можно прочитать напрямую:
/// 1. Документы носят строковый DefinitionId, а не ссылку — так они переживают сохранение.
///    Кто-то должен превращать этот идентификатор обратно в определение, иначе запись
///    «поставлен factory в клетке (5,2)» нечем восстановить.
/// 2. Нужно уметь ПЕРЕЧИСЛИТЬ доступное роли — из этого HUD строит панель построек.
///    Добавил новый .toml — кнопка появилась сама, кода не трогая.
/// 3. Чтение один раз на старте, а не при каждом обращении, и пути к файлам
///    не размазаны по коду.
///
/// Разбор, наследование и проверка связности вынесены в ContentCompiler: каталог держит
/// собранное, а не собирает.
/// </summary>
public sealed class Catalog
{
    private readonly Dictionary<string, UnitDefinition> _units = new();
    private readonly Dictionary<string, ToolDefinition> _tools = new();
    private readonly Dictionary<string, BuildbarDefinition> _buildbars = new();
    private readonly Dictionary<string, WaveDefinition> _waves = new();

    /// <summary>Реестр тегов. Заполняется сборкой из resources/content/tags.toml.</summary>
    public TagRegistry Tags { get; } = new();

    /// <summary>Все определения сущностей: боты, постройки, противник.</summary>
    public IReadOnlyCollection<UnitDefinition> Units => _units.Values;

    public IReadOnlyCollection<BuildbarDefinition> Buildbars => _buildbars.Values;

    /// <summary>Всё, что можно построить: у чего задана секция сборки.</summary>
    public IEnumerable<UnitDefinition> Buildables =>
        _units.Values.Where(definition => definition.Assembly != null);

    /// <summary>Виды противника: из них система спавна и набирает поток по весам.</summary>
    public IEnumerable<UnitDefinition> Enemies =>
        _units.Values.Where(definition => definition.Class == UnitClass.Enemy);

    /// <summary>Все волны. Из них подсистема волн отбирает подходящую по террору.</summary>
    public IReadOnlyCollection<WaveDefinition> Waves => _waves.Values;

    public UnitDefinition Unit(string id) =>
        id != null && _units.TryGetValue(id, out var definition) ? definition : null;

    public ToolDefinition Tool(string id) =>
        id != null && _tools.TryGetValue(id, out var tool) ? tool : null;

    public WaveDefinition Wave(string id) =>
        id != null && _waves.TryGetValue(id, out var wave) ? wave : null;

    public BuildbarDefinition Buildbar(string id) =>
        id != null && _buildbars.TryGetValue(id, out var bar) ? bar : null;

    public bool AddUnit(UnitDefinition definition) => _units.TryAdd(definition.Id, definition);

    public bool AddTool(ToolDefinition tool) => _tools.TryAdd(tool.Id, tool);

    public bool AddBuildbar(BuildbarDefinition bar) => _buildbars.TryAdd(bar.Id, bar);

    public bool AddWave(WaveDefinition wave) => _waves.TryAdd(wave.Id, wave);

    /// <summary>
    /// Собрать содержимое с диска. Ошибки не глотаются: сборка с ошибками означает,
    /// что часть определений отсутствует, и увидеть это надо в первую секунду запуска,
    /// а не через полчаса игры на кнопке, которая ничего не ставит.
    /// </summary>
    public void LoadAll()
    {
        int errors = ContentCompiler.Compile(this);

        GD.Print($"[Каталог] определений: {_units.Count}, инструментов: {_tools.Count}, " +
                 $"панелей: {_buildbars.Count}, волн: {_waves.Count}, тегов: {Tags.Names.Count}");

        if (errors > 0)
            GD.PushError($"[Каталог] содержимое собрано с ошибками: {errors}. " +
                         "Перечисленные выше определения в игру не попали");
    }
}
