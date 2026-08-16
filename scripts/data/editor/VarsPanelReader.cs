using System;
using System.Collections.Generic;
using System.Linq;
using Tomlyn.Model;

/// <summary>
/// Разложение файла переменных в плоский перечень строк для контекстной панели.
///
/// Вложенные таблицы разворачиваются в составные имена секций (<c>hull.heavy</c>), потому
/// что панель показывает справочные значения, а не правит их: дерево здесь потребовало бы
/// раскрытия узлов ради того же самого списка.
/// </summary>
public static class VarsPanelReader
{
    /// <summary>Добавить строки таблицы, рекурсивно раскрывая вложенные секции.</summary>
    public static void Fill(List<VarsPanelRow> rows, string section, TomlTable table)
    {
        if (rows == null || table == null)
            return;

        foreach (var pair in table.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            string nested = string.IsNullOrEmpty(section) ? pair.Key : $"{section}.{pair.Key}";
            if (pair.Value is TomlTable inner)
            {
                Fill(rows, nested, inner);
                continue;
            }

            rows.Add(new VarsPanelRow
            {
                Section = section ?? "",
                Key = pair.Key,
                Value = ContentValueReader.EncodeRaw(pair.Value),
            });
        }
    }
}
