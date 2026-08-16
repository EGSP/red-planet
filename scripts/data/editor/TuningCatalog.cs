using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Описание одного настроечного ресурса, показываемого на странице глобальных параметров.
/// </summary>
public sealed class TuningResourceSpec
{
    /// <summary>Устойчивый ключ. Используется в снимке рабочего места.</summary>
    public string Id { get; init; }

    /// <summary>Заголовок плитки.</summary>
    public string Title { get; init; }

    /// <summary>Одна строка о том, на что влияет ресурс.</summary>
    public string Subtitle { get; init; }

    /// <summary>Путь ресурса, назначенного проекту изначально.</summary>
    public string DefaultPath { get; init; }

    /// <summary>
    /// Сцена, в которой ресурс назначен подсистеме. По ней определяется действующий вариант
    /// и в неё же вносится замена при переключении на новый ресурс.
    /// </summary>
    public string ScenePath { get; init; }

    /// <summary>
    /// Узел сцены, которому назначен ресурс. Опорой служит имя узла и имя свойства, а не
    /// путь файла: после переключения на вариант путь меняется, тогда как назначение
    /// остаётся на прежнем месте.
    /// </summary>
    public string NodeName { get; init; }

    /// <summary>Свойство узла, хранящее ссылку на ресурс.</summary>
    public string PropertyName { get; init; } = "Settings";

    /// <summary>Каталог, куда складываются варианты этого ресурса.</summary>
    public string Directory =>
        DefaultPath[..(DefaultPath.LastIndexOf('/') + 1)];

    /// <summary>Имя файла без расширения: основа для имени нового варианта.</summary>
    public string BaseName
    {
        get
        {
            string file = TomlText.FileName(DefaultPath);
            int dot = file.LastIndexOf('.');
            return dot > 0 ? file[..dot] : file;
        }
    }
}

/// <summary>
/// Состав страницы глобальных параметров.
///
/// ЧТО СЮДА ВХОДИТ. Только то, что определяет появление противника: подсчёт террора,
/// постоянное давление и общая настройка подсистемы волн. Прочие ресурсы
/// <c>resources/tuning</c> — камера, туман, тени, графика — к балансу отношения не имеют
/// и правятся инспектором по мере надобности; собранные на одной странице, они лишь
/// разбавляли бы то, ради чего страница заведена.
///
/// ПОЧЕМУ ЭТО НЕ ЛЕЖИТ РЯДОМ С САМИМИ РЕСУРСАМИ. Ресурс не знает, кому он назначен:
/// назначение живёт в сцене партии. Каталог связывает файл настройки с местом его
/// применения, поэтому здесь же определяется, какой вариант ресурса действует сейчас.
/// </summary>
public static class TuningCatalog
{
    /// <summary>Сцена партии: в ней настроечные ресурсы назначены системам давления.</summary>
    public const string SessionScenePath = "res://scenes/Session.tscn";

    public static IReadOnlyList<TuningResourceSpec> All { get; } = new List<TuningResourceSpec>
    {
        new()
        {
            Id = "terror",
            Title = "Terror",
            Subtitle = "How strongly the player has revealed itself: the score the whole "
                       + "pressure loop is built on",
            DefaultPath = "res://resources/tuning/terror.tres",
            ScenePath = SessionScenePath,
            NodeName = "TerrorSystem",
        },
        new()
        {
            Id = "pressure",
            Title = "Standing pressure",
            Subtitle = "How much enemy power the terror score buys and how fast losses "
                       + "are replaced",
            DefaultPath = "res://resources/tuning/pressure.tres",
            ScenePath = SessionScenePath,
            NodeName = "PressureSystem",
        },
        new()
        {
            Id = "waves",
            Title = "Wave subsystem",
            Subtitle = "Pace of waves and the spawn-shape defaults every wave file "
                       + "overrides key by key",
            DefaultPath = "res://resources/tuning/waves.tres",
            ScenePath = SessionScenePath,
            NodeName = "WaveSystem",
        },
    };

    public static TuningResourceSpec ById(string id) =>
        All.FirstOrDefault(spec => string.Equals(spec.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// Путь ресурса, действующего в партии. Отличается от <see cref="TuningResourceSpec.DefaultPath"/>
    /// после переключения на сохранённый вариант; при недоступной сцене возвращается путь
    /// по умолчанию, поскольку он же был назначен изначально.
    /// </summary>
    public static string ActivePath(TuningResourceSpec spec) =>
        spec == null
            ? null
            : TuningSceneBinding.ResourcePathOf(spec) ?? spec.DefaultPath;

    /// <summary>Действующий путь по ключу ресурса. Удобен для чтения из предпросмотров.</summary>
    public static string ActivePath(string id) => ActivePath(ById(id));
}
