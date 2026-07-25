using System.Collections.Generic;
using Godot;

/// <summary>
/// Справочники — библиотека определений, загруженных из .tres: чем является «завод»,
/// сколько стоит, какой он формы, какой сценой представлен в мире.
///
/// Только чтение и только шаблоны. Каталог ничего не создаёт и не знает о живых сущностях:
/// создание вынесено в Spawner, живые экземпляры лежат в EntityStore.
///
/// Зачем нужен вообще, если ресурс можно загрузить напрямую:
/// 1. Документы носят строковый DefId, а не ссылку на ресурс (см. BlueprintPlaced) —
///    так они переживают сохранение. Кто-то должен превращать этот id обратно в определение,
///    иначе запись «поставлен factory в клетке (5,2)» нечем восстановить.
/// 2. Нужно уметь ПЕРЕЧИСЛИТЬ доступное роли — из этого HUD строит панель построек.
///    Добавил новый .tres — кнопка появилась сама, кода не трогая.
/// 3. Загрузка один раз на старте, а не при каждом обращении, и пути к ресурсам
///    не размазаны по коду.
/// </summary>
public sealed class Catalog
{
    private readonly Dictionary<string, BuildableDef> _buildables = new();
    private readonly Dictionary<string, UnitDef> _units = new();

    public IReadOnlyCollection<BuildableDef> Buildables => _buildables.Values;

    public BuildableDef Buildable(string id) =>
        _buildables.TryGetValue(id, out var def) ? def : null;

    public UnitDef Unit(string id) => _units.TryGetValue(id, out var def) ? def : null;

    /// <summary>Что доступно строителю с этой ролью.</summary>
    public List<BuildableDef> AvailableFor(string role)
    {
        var result = new List<BuildableDef>();
        foreach (var def in _buildables.Values)
            if (def.AvailableFor(role))
                result.Add(def);
        result.Sort((a, b) => a.TotalWork.CompareTo(b.TotalWork));
        return result;
    }

    public void LoadAll()
    {
        foreach (var def in Load<BuildableDef>("res://resources/buildables/"))
            _buildables[def.Id] = def;

        foreach (var def in Load<UnitDef>("res://resources/units/"))
            _units[def.Id] = def;

        GD.Print($"[Catalog] построек: {_buildables.Count}, юнитов: {_units.Count}");
    }

    private static List<T> Load<T>(string dir) where T : Resource
    {
        var result = new List<T>();
        using var access = DirAccess.Open(dir);

        if (access == null)
        {
            GD.PushWarning($"[Catalog] каталог не найден: {dir}");
            return result;
        }

        foreach (var file in access.GetFiles())
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres"))
                continue;

            var res = ResourceLoader.Load<T>(dir + name);
            if (res != null)
                result.Add(res);
        }

        return result;
    }
}
