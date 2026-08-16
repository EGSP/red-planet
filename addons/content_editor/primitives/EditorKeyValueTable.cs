using System.Collections.Generic;
using Godot;

/// <summary>Компактное представление секции, ключа и значения для справочных панелей.</summary>
[Tool]
public partial class EditorKeyValueTable : Tree
{
    public EditorKeyValueTable()
    {
        Columns = 3;
        ColumnTitlesVisible = true;
        HideRoot = true;
        SelectMode = SelectModeEnum.Row;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;

        SetColumnTitle(0, "Section");
        SetColumnTitle(1, "Key");
        SetColumnTitle(2, "Value");
        SetColumnExpand(0, false);
        SetColumnCustomMinimumWidth(0, 130);
        SetColumnExpand(1, false);
        SetColumnCustomMinimumWidth(1, 180);
        SetColumnExpand(2, true);
    }

    public void SetRows(IEnumerable<VarsPanelRow> rows)
    {
        Clear();
        var root = CreateItem();
        foreach (var row in rows)
        {
            var item = CreateItem(root);
            item.SetText(0, row.Section);
            item.SetText(1, row.Key);
            item.SetText(2, row.Value);
            item.SetTooltipText(2, row.Value);
        }
    }
}
