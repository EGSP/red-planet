using System;
using System.Collections.Generic;

/// <summary>
/// Область редактора, которой принадлежит открытая сессия.
///
/// ЗАЧЕМ ЭТО НУЖНО. Волна правится в режиме «Waves» и сущностью каталога не является:
/// у неё нет силуэта, она не показывается на измерительном поле и её форма свойств
/// устроена иначе. Пока сессии всех видов лежали в одном перечне, выбор волны на карте
/// открывал вкладку в режиме «Entities» и подменял там активную сущность. Область
/// разделяет перечни вкладок и активную сессию, оставляя общими тексты, черновики
/// и сборку каталога.
/// </summary>
public enum ContentEditorScope
{
    /// <summary>Юниты, постройки, оружие и рабочие инструменты — вкладки режима «Entities».</summary>
    Entities,

    /// <summary>Волны — правятся инспектором режима «Waves».</summary>
    Waves,
}

/// <summary>
/// Откуда взялось итоговое значение поля после Resolve.
/// Форма показывает эту метку рядом с подписью, чтобы было видно, правка ли это
/// текущего файла или унаследованное число.
/// </summary>
public enum FieldProvenance
{
    /// <summary>Ключ записан в черновике текущего файла.</summary>
    Local,

    /// <summary>Ключа в текущем файле нет; значение пришло из цепочки <c>base</c>.</summary>
    Base,

    /// <summary>Значение задано ссылкой на <c>*.vars.toml</c> либо пришло из vars.</summary>
    Vars,

    /// <summary>Ни в файле, ни в base ключа нет — действует умолчание из определения в коде.</summary>
    Default,

    /// <summary>Поле недоступно для этой сущности (нет сессии или нет определения).</summary>
    Missing,
}

/// <summary>Происхождение итогового значения и TOML-файл, в котором оно задано.</summary>
public sealed class FieldSourceInfo
{
    /// <summary>Вид происхождения: local, base, vars либо умолчание кода.</summary>
    public FieldProvenance Provenance { get; init; }

    /// <summary>Путь к файлу, где найден ключ. Для умолчания кода пуст.</summary>
    public string Path { get; init; }
}

/// <summary>Содержимое одного <c>*.vars.toml</c>, разложенное для показа таблицей.</summary>
public sealed class VarsPanelData
{
    /// <summary>Сущность, ради которой панель открыта: при смене сущности она закрывается.</summary>
    public string ContextId { get; init; }

    public string Path { get; init; }
    public string FileName { get; init; }
    public List<VarsPanelRow> Rows { get; init; } = new();
}

/// <summary>Одна строка панели значений файла переменных.</summary>
public sealed class VarsPanelRow
{
    /// <summary>Имя вложенной секции либо пустая строка для корневого ключа.</summary>
    public string Section { get; init; }

    public string Key { get; init; }
    public string Value { get; init; }
}

/// <summary>Вид узла графа зависимостей: определяет его цвет и подпись.</summary>
public enum ContentGraphNodeKind
{
    Unit,
    Building,
    Base,
    Vars,
    Weapon,
    WorkTool,

    /// <summary>
    /// Ресурс движка, на который ссылается определение: спрайт, сцена модели, шейдер.
    /// Отдельный вид нужен затем, что такой узел не является документом содержимого:
    /// цепочки <c>base</c> у него нет, и открывается он не формой, а редактором Godot.
    /// </summary>
    Asset,
}

/// <summary>Узел графа зависимостей: файл определения, шаблона либо переменных.</summary>
public sealed class ContentGraphNode
{
    /// <summary>Ключ узла в графе. Совпадает с путём файла и потому уникален.</summary>
    public string Key { get; init; }

    public string Title { get; init; }
    public string Detail { get; init; }
    public string Path { get; init; }
    public ContentGraphNodeKind Kind { get; init; }
}

/// <summary>Ребро графа: наследование, секционный base либо ссылка на переменную.</summary>
public sealed class ContentGraphEdge
{
    public string From { get; init; }
    public string To { get; init; }
    public string Label { get; init; }
}

/// <summary>Граф зависимостей одной сущности со всеми достижимыми из неё файлами.</summary>
public sealed class ContentGraphData
{
    /// <summary>Сущность, для которой построен граф.</summary>
    public string ContextId { get; init; }

    /// <summary>Ключ корневого узла: с него начинается раскладка по уровням.</summary>
    public string RootKey { get; init; }

    public List<ContentGraphNode> Nodes { get; init; } = new();
    public List<ContentGraphEdge> Edges { get; init; } = new();
}

/// <summary>Одна запись каталога редактора — файл с <c>id</c>, ещё до сборки Catalog.</summary>
public sealed class ContentEditorEntry
{
    public string Id;
    public string Path;
    public string FileName;
    public string DisplayName;
    public ContentEntityKind Kind;

    /// <summary>Шаблоны с <c>abstract = true</c> в каталог игры не попадают, но файл существует.</summary>
    public bool Abstract;

    /// <summary>Область редактора, в которой правится запись этого вида.</summary>
    public ContentEditorScope Scope =>
        Kind == ContentEntityKind.Wave ? ContentEditorScope.Waves : ContentEditorScope.Entities;
}

/// <summary>
/// Строка карты волн, прочитанная снисходительно из черновика. Отличается от
/// <see cref="WaveDefinition"/> тем, что существует и для волны, не прошедшей проверку:
/// правка проходит через недопустимые промежуточные состояния, и строка на карте
/// не должна при этом исчезать.
/// </summary>
public sealed class WaveOverview
{
    public string Id { get; init; }
    public string Path { get; init; }
    public string FileName { get; init; }
    public string DisplayName { get; init; }
    public string[] Tags { get; init; } = Array.Empty<string>();

    /// <summary>Нижняя граница применимости; отрицательное значение означает «безразлично».</summary>
    public float TerrorMin { get; init; }

    /// <summary>Верхняя граница применимости; отрицательное значение означает «безразлично».</summary>
    public float TerrorMax { get; init; }

    public float Budget { get; init; }
    public float BudgetPerTerror { get; init; }
    public float ChillMultiplier { get; init; }

    /// <summary>Число блоков <c>[[unit_list]]</c> в черновике.</summary>
    public int ListCount { get; init; }

    /// <summary>Прошла ли волна проверку связности и попала ли в собранный каталог.</summary>
    public bool Compiled { get; init; }

    public bool Fits(float terror) =>
        (TerrorMin < 0f || terror >= TerrorMin) && (TerrorMax < 0f || terror <= TerrorMax);

    public float BudgetAt(float terror) => Math.Max(Budget + BudgetPerTerror * terror, 0f);
}

/// <summary>Один блок <c>[[unit_list]]</c> черновика волны.</summary>
public sealed class WaveUnitListView
{
    /// <summary>Порядковый номер блока в файле: обращение к блокам идёт по номеру.</summary>
    public int Index { get; init; }

    public string Mode { get; init; }
    public string[] UnitIds { get; init; } = Array.Empty<string>();
    public float Share { get; init; }

    /// <summary>Задана ли доля бюджета явно: ноль и отсутствие ключа значат разное.</summary>
    public bool HasShare { get; init; }
}

/// <summary>
/// Итог проверки внешних правок: что можно подтянуть молча и что конфликтует
/// с несохранённым черновиком.
/// </summary>
public sealed class ExternalChangeReport
{
    /// <summary>Открытые вкладки без локальных правок, уже перечитанные с диска.</summary>
    public readonly List<string> Reloaded = new();

    /// <summary>
    /// Открытые вкладки с Dirty=true, у которых метка времени на диске новее LoadedStamp.
    /// Черновик не трогаем: иначе правка в редакторе контента будет потеряна.
    /// </summary>
    public readonly List<string> Conflicts = new();

    /// <summary>Файлы каталога, появившиеся или изменившиеся без открытой вкладки.</summary>
    public int CatalogChanged;

    public bool Any => Reloaded.Count > 0 || Conflicts.Count > 0 || CatalogChanged > 0;
}
