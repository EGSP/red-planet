using System;
using System.Collections.Generic;

/// <summary>
/// Снимок одной открытой вкладки: черновик и всё, что относится к вкладке, а не к файлу.
/// </summary>
public sealed class ContentEditorSessionSnapshot
{
    public string Id { get; set; }
    public string DraftText { get; set; }

    /// <summary>Метка времени файла на момент открытия: по ней обнаруживается внешняя правка.</summary>
    public ulong LoadedStamp { get; set; }

    public bool Dirty { get; set; }
    public bool ShowOnField { get; set; }
    public float FieldX { get; set; }
    public float FieldY { get; set; }
}

/// <summary>
/// Состояние таблицы баланса, которое можно сохранить под именем.
///
/// ФИЛЬТРЫ ВХОДЯТ В ПРЕДСТАВЛЕНИЕ НАМЕРЕННО. Набор колонок отвечает на вопрос, какие
/// величины сравниваются, а фильтры — на вопрос, какие строки сравниваются; вопрос
/// «сравнить дальнобойность подвижных машин противника» требует и того, и другого.
/// Пока представление хранило одни колонки, отбор приходилось задавать заново при каждом
/// возврате к той же задаче.
/// </summary>
public sealed class ContentEditorBalanceViewState
{
    /// <summary>Показанные колонки в порядке общего перечня.</summary>
    public string[] ColumnIds { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Ширина каждой колонки из <see cref="ColumnIds"/>, в пикселях. Длина массива может
    /// не совпадать с числом колонок: недостающие берут ширину по умолчанию.
    /// </summary>
    public int[] ColumnWidths { get; set; } = Array.Empty<int>();

    public string SortColumnId { get; set; }
    public bool SortAsc { get; set; }

    /// <summary>Строка поиска по имени и идентификатору.</summary>
    public string Search { get; set; } = "";

    /// <summary>Отбор по виду: 0 — все, 1 — подвижные, 2 — постройки.</summary>
    public int TypeFilter { get; set; }

    public bool Attackers { get; set; }
    public bool Builders { get; set; }

    public ContentEditorBalanceViewState Clone() => new()
    {
        ColumnIds = (string[])(ColumnIds ?? Array.Empty<string>()).Clone(),
        ColumnWidths = (int[])(ColumnWidths ?? Array.Empty<int>()).Clone(),
        SortColumnId = SortColumnId,
        SortAsc = SortAsc,
        Search = Search ?? "",
        TypeFilter = TypeFilter,
        Attackers = Attackers,
        Builders = Builders,
    };
}

/// <summary>Именованное представление таблицы баланса.</summary>
public sealed class ContentEditorSavedView
{
    public string Name { get; set; }
    public ContentEditorBalanceViewState State { get; set; } = new();
}

/// <summary>Состояние режима «Balance» целиком.</summary>
public sealed class ContentEditorBalanceState
{
    /// <summary>Выбранное представление: встроенное, сохранённое либо «Custom view».</summary>
    public string ViewId { get; set; }

    /// <summary>Действующие колонки, фильтры и сортировка — они же правятся элементами полосы.</summary>
    public ContentEditorBalanceViewState Current { get; set; } = new();

    public List<ContentEditorSavedView> SavedViews { get; set; } = new();

    /// <summary>Положение разделителя между таблицами игрока и противника.</summary>
    public int Split { get; set; }
}

/// <summary>Состояние режима «Entities»: отбор в каталоге, форма и разделители.</summary>
public sealed class ContentEditorEntitiesState
{
    public int CatalogTab { get; set; }
    public string CatalogSearch { get; set; } = "";

    /// <summary>Строка отбора полей формы: сохраняется при смене сущности и между запусками.</summary>
    public string FormFieldFilter { get; set; } = "";

    /// <summary>
    /// Раскрытые секции формы. Пустой массив означает «использовать умолчание»: пустое
    /// множество раскрытых секций и отсутствие сведений выглядели бы одинаково, а первое
    /// является законным состоянием — все секции свёрнуты.
    /// </summary>
    public string[] ExpandedSections { get; set; }

    /// <summary>Задано ли состояние секций явно.</summary>
    public bool HasExpandedSections { get; set; }

    /// <summary>Разделитель «каталог — рабочая область».</summary>
    public int CatalogSplit { get; set; }

    /// <summary>Разделитель «центр — форма свойств».</summary>
    public int WorkspaceSplit { get; set; }

    /// <summary>Разделитель «измерительное поле — контекстные вкладки».</summary>
    public int CenterSplit { get; set; }
}

/// <summary>Состояние режима «Waves»: отбор на карте, инспектор и панель формы.</summary>
public sealed class ContentEditorWavesState
{
    public string SelectedId { get; set; }
    public string Search { get; set; } = "";
    public bool LogScale { get; set; }

    /// <summary>Проверочный показатель террора, выставленный ползунком карты.</summary>
    public float Probe { get; set; }

    public string[] ExpandedSections { get; set; }
    public bool HasExpandedSections { get; set; }

    /// <summary>Разделитель «карта и форма — инспектор».</summary>
    public int Split { get; set; }

    /// <summary>Разделитель «карта — панель формы волны».</summary>
    public int ShapeSplit { get; set; }

    /// <summary>Показывать ли на панели формы состав волны, а не одни границы секторов.</summary>
    public bool ShapeShowUnits { get; set; } = true;

    /// <summary>Показывать ли окружность появления и поле застройки.</summary>
    public bool ShapeShowWorld { get; set; } = true;

    /// <summary>Направление удара, градусов: в игре берётся случайным.</summary>
    public float ShapeDirectionDegrees { get; set; } = -90f;

    /// <summary>Зерно набора состава: разные зёрна дают разный состав при том же бюджете.</summary>
    public int ShapeSeed { get; set; } = 1;
}

/// <summary>Несохранённые правки одной плитки страницы глобальных параметров.</summary>
public sealed class ContentEditorTuningDraftState
{
    /// <summary>Ключ ресурса из <see cref="TuningCatalog"/>.</summary>
    public string Id { get; set; }

    /// <summary>Правки строками вида «свойство=значение» в инвариантной культуре.</summary>
    public string[] Edits { get; set; } = Array.Empty<string>();
}

/// <summary>Состояние режима «Globals»: свёрнутые плитки и их черновики.</summary>
public sealed class ContentEditorGlobalsState
{
    /// <summary>Ключи свёрнутых плиток.</summary>
    public string[] Collapsed { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Черновики плиток. Хранятся затем же, зачем черновики файлов содержимого:
    /// перезагрузка C#-сборки не должна уничтожать несохранённую правку.
    /// </summary>
    public List<ContentEditorTuningDraftState> Drafts { get; set; } = new();
}

/// <summary>
/// Сериализуемый снимок всего рабочего пространства редактора.
///
/// ЗАЧЕМ ОН НУЖЕН. Перезагрузка C#-сборки уничтожает managed-объекты вместе с черновиками
/// и настройкой интерфейса. Снимок пишется в <c>.godot/</c> при каждом значимом изменении
/// и читается до чтения файлов с диска, поэтому вкладки, отбор и раскладка переживают
/// и сборку, и перезапуск редактора.
/// </summary>
public sealed class ContentEditorWorkspace
{
    /// <summary>Выбранный режим верхнего TabContainer.</summary>
    public int ModeTab { get; set; }

    /// <summary>Активная вкладка режима «Entities».</summary>
    public string ActiveEntityId { get; set; }

    /// <summary>Волна, выбранная на карте режима «Waves».</summary>
    public string ActiveWaveId { get; set; }

    public List<ContentEditorSessionSnapshot> Sessions { get; set; } = new();
    public ContentEditorEntitiesState Entities { get; set; } = new();
    public ContentEditorBalanceState Balance { get; set; } = new();
    public ContentEditorWavesState Waves { get; set; } = new();
    public ContentEditorGlobalsState Globals { get; set; } = new();
}
