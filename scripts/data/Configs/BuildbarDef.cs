using System.Collections.Generic;
using Godot;

/// <summary>Ячейка строительной панели: координата в секции и что в ней стоит.</summary>
public readonly struct BuildbarCell
{
    public readonly Vector2I Position;
    public readonly string BuildableId;

    public BuildbarCell(Vector2I position, string buildableId)
    {
        Position = position;
        BuildableId = buildableId;
    }
}

/// <summary>
/// Секция строительной панели — сетка ячеек с общим заголовком.
/// Счёт от левого нижнего угла: X растёт вправо, Y вверх. Пустая ячейка не описывается
/// никак — её просто нет в списке.
/// </summary>
public sealed class BuildbarSectionDef
{
    public string Id = "";
    public string Title = "";

    /// <summary>Порядок секции на панели: чем меньше, тем выше.</summary>
    public int Order;

    public readonly List<BuildbarCell> Cells = new();
}

/// <summary>
/// Строительная панель юнита: что он умеет строить и как это разложено по секциям.
///
/// Панель — справочник, а не свойство юнита: юнит ссылается на неё по строковому
/// идентификатору, как документ ссылается на определение. Отдельного списка доступных
/// построек юнит не держит — панель и есть ответ на вопрос, что он умеет строить.
///
/// Хранится не ресурсом Godot, а файлом .toml: определение правят руками и часто,
/// а сетка ячеек с координатами в инспекторе читается плохо. Разбор — в BuildbarLoader,
/// правила слияния и соглашение о координатах — в resources/buildbars/buildbar.md.
/// </summary>
public sealed class BuildbarDef
{
    public string Id = "";

    /// <summary>
    /// Главенство при слиянии: чем МЕНЬШЕ число, тем важнее панель. У коммандера 1,
    /// у фабрикатора 2. Панель с меньшим числом задаёт основу разметки, остальные
    /// накладываются поверх и уступают ей спорные ячейки.
    /// </summary>
    public int Priority = 10;

    public readonly List<BuildbarSectionDef> Sections = new();
}
