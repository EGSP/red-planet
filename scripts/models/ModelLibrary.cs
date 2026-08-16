using System.Collections.Generic;
using Godot;

/// <summary>
/// Загрузка сцен моделей по пути из справочника с кэшем разобранной сцены.
///
/// ПОЧЕМУ КЭШ. <see cref="ResourceLoader"/> и сам держит загруженные ресурсы, но обращение
/// к нему на каждое рождение юнита стоит поиска по пути; сущностей одного вида в бою сотни.
///
/// Неудачная загрузка запоминается наравне с удачной: предупреждение выдаётся один раз,
/// а не по разу на каждого юнита с испорченной ссылкой.
/// </summary>
public static class ModelLibrary
{
    private static readonly Dictionary<string, PackedScene> Cache = new();

    /// <summary>
    /// Создать экземпляр модели. Пустой путь и незагруженная сцена дают null — вызывающий
    /// обязан обойтись без модели, а не считать её отсутствие ошибкой.
    /// </summary>
    public static UnitModel Instantiate(string path)
    {
        var scene = Scene(path);
        if (scene == null)
            return null;

        if (scene.Instantiate() is UnitModel model)
            return model;

        GD.PushWarning($"[ModelLibrary] корень сцены модели не UnitModel: {path}");
        return null;
    }

    private static PackedScene Scene(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        // В редакторе кэш пропускается: сцену модели правят прямо сейчас, и запомненный
        // разбор показывал бы поле редактора контента таким, каким оно было до правки.
        //
        // СВОЕГО КЭША МАЛО. ResourceLoader держит собственный набор загруженных ресурсов
        // и на повторный запрос по тому же пути отдаёт прежнюю сцену, поэтому правка .tscn
        // не была бы видна, сколько бы раз её ни запрашивали. CacheMode.Replace заставляет
        // разобрать файл заново и обновить уже выданный ресурс
        if (Engine.IsEditorHint())
            return ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.Replace);

        if (Cache.TryGetValue(path, out var cached))
            return cached;

        var scene = ResourceLoader.Load<PackedScene>(path);

        if (scene == null)
            GD.PushWarning($"[ModelLibrary] сцена модели не загружена: {path}");

        Cache[path] = scene;
        return scene;
    }
}
