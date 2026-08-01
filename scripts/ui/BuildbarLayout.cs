using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Разметка строительной панели, сложенная из панелей всех выделенных строителей.
///
/// ЗАЧЕМ СЛИЯНИЕ. Выделить можно кого угодно и в каком угодно составе, а панель нужна одна.
/// Складывать наборы построек списком нельзя: тогда при каждом новом выделении кнопки
/// перескакивали бы с места на место, и мышечная память игрока не нарабатывалась бы вовсе.
/// Поэтому складываются не списки, а координаты, и позиция кнопки определяется панелью
/// с наибольшим главенством, а не составом выделения.
///
/// ПОРЯДОК. Панели идут по возрастанию Priority: у коммандера 1, у фабрикатора 2, и первая
/// задаёт основу разметки. Дальнейшие накладываются на неё и уступают спорные ячейки.
///
/// СПОР ЗА ЯЧЕЙКУ. Если накладываемая панель метит в занятую ячейку другой постройкой,
/// она вставляется сразу за занятой, а хвост строки сдвигается вправо. Сдвиг живёт внутри
/// одной строки и на соседние строки секции не распространяется — строки становятся разной
/// длины, и это нормально. Разметка при этом сохраняет главное: у постройки из более важной
/// панели позиция не меняется никогда.
/// </summary>
public sealed class BuildbarLayout
{
    /// <summary>Строка секции. Пустая ячейка — null, порядок в списке и есть координата X.</summary>
    public sealed class Row
    {
        public readonly List<string> Cells = new();
    }

    public sealed class Section
    {
        public string Id;
        public string Title;
        public int Order;

        /// <summary>Строки снизу вверх, как и в справочнике.</summary>
        public readonly List<Row> Rows = new();
    }

    public IReadOnlyList<Section> Sections { get; private set; } = new List<Section>();

    public bool IsEmpty => Sections.Count == 0;

    /// <summary>
    /// Сложить панели. Порядок передачи значения не имеет — он задаётся главенством,
    /// а при равном главенстве идентификатором, чтобы разметка не зависела от того,
    /// в каком порядке игрок щёлкал по юнитам.
    /// </summary>
    public static BuildbarLayout Merge(IEnumerable<BuildbarDefinition> bars)
    {
        var layout = new BuildbarLayout();
        var sections = new List<Section>();

        var ordered = bars
            .Where(bar => bar != null)
            .Distinct()
            .OrderBy(bar => bar.Priority)
            .ThenBy(bar => bar.Id)
            .ToList();

        foreach (var bar in ordered)
        foreach (var source in bar.Sections)
        {
            if (source == null)
                continue;

            var section = sections.FirstOrDefault(s => s.Id == source.Id);

            if (section == null)
            {
                // Заголовок и место секции берёт та панель, что ввела секцию первой,
                // то есть самая главная из объявивших её
                section = new Section { Id = source.Id, Title = source.Title, Order = source.Order };
                sections.Add(section);
            }

            foreach (var cell in source.Cells)
                Place(section, cell.Position.Y, cell.Position.X, cell.BuildableId);
        }

        layout.Sections = sections.OrderBy(s => s.Order).ThenBy(s => s.Id).ToList();
        return layout;
    }

    private static void Place(Section section, int y, int x, string buildableId)
    {
        while (section.Rows.Count <= y)
            section.Rows.Add(new Row());

        var cells = section.Rows[y].Cells;

        // Ту же постройку второй раз не кладём: её позицию уже определила более важная
        // панель, и повтор увёл бы кнопку с насиженного места
        if (cells.Contains(buildableId))
            return;

        while (cells.Count <= x)
            cells.Add(null);

        if (cells[x] == null)
        {
            cells[x] = buildableId;
            return;
        }

        // Спор: занятая ячейка остаётся за прежней хозяйкой, новая встаёт сразу следом
        cells.Insert(x + 1, buildableId);
    }
}
