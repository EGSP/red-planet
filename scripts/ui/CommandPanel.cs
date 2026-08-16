using System.Collections.Generic;
using Godot;

/// <summary>
/// Панель приказов у правого края: что доступно текущему выделению.
///
/// Принятые приказы (<see cref="IOrderable.AllowedOrders"/>) и мягкие
/// (<see cref="IOrderable.SoftOrders"/>) объединяются по выделению. Мягкий приказ
/// помечается звёздочкой: умение есть, в allow и deny его нет. Явный deny в список
/// не входит вовсе. Кнопок здесь нет: приказ отдаётся правым щелчком; список — подсказка.
/// </summary>
public partial class CommandPanel : CanvasLayer
{
    private Control _frame;
    private VBoxContainer _list;

    /// <summary>Отпечаток набора: пересобираем строки только когда он сменился.</summary>
    private string _key = "";

    public override void _Ready()
    {
        _frame = new UiFrame { Visible = false };
        AddChild(_frame);

        var row = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _frame.AddChild(row);
        row.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_right", 12);
        row.AddChild(margin);

        var column = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        margin.AddChild(column);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(150, 0) };
        column.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        panel.AddChild(box);

        var caption = new Label { Text = "ПРИКАЗЫ" };
        caption.AddThemeFontSizeOverride("font_size", 11);
        caption.AddThemeColorOverride("font_color", new Color(0.45f, 0.85f, 0.95f));
        box.AddChild(caption);

        box.AddChild(new HSeparator());

        _list = new VBoxContainer();
        _list.AddThemeConstantOverride("separation", 2);
        box.AddChild(_list);
    }

    public override void _Process(double delta)
    {
        var command = GameManager.I?.Command;
        var selected = command?.Selected;

        if (selected == null || selected.Count == 0)
        {
            _frame.Visible = false;
            _key = "";
            return;
        }

        var lines = LinesOf(selected);

        // Выбранный вид входит в отпечаток: подсветка обязана переключаться, а состав
        // набора при этом не меняется
        string key = string.Join('|', lines) + "@" + command.Aimed;

        _frame.Visible = lines.Count > 0;

        if (key == _key)
            return;

        _key = key;
        Rebuild(lines, command.Aimed);
    }

    /// <summary>
    /// Объединение наборов выделенных. Если хотя бы у одного приказ принят — строка
    /// без звёздочки; если у всех он только мягкий — со звёздочкой.
    /// </summary>
    private static List<(OrderKind Kind, bool Soft)> LinesOf(IReadOnlyList<IOrderable> selected)
    {
        var accepted = OrderSet.None;
        var soft = OrderSet.None;

        foreach (var actor in selected)
        {
            accepted = accepted.Union(actor.AllowedOrders);
            soft = soft.Union(actor.SoftOrders);
        }

        soft = soft.Except(accepted);

        var lines = new List<(OrderKind, bool)>();

        foreach (OrderKind kind in System.Enum.GetValues<OrderKind>())
        {
            if (accepted.Allows(kind))
                lines.Add((kind, false));
            else if (soft.Allows(kind))
                lines.Add((kind, true));
        }

        return lines;
    }

    /// <summary>
    /// Строка приказа: название, назначенная клавиша справа и пометка выбранного вида.
    ///
    /// КЛАВИША ПОКАЗАНА ЗДЕСЬ, А НЕ В ОТДЕЛЬНОЙ СПРАВКЕ: список приказов игрок и так читает
    /// глазами, выбирая, что отдать, и держать соответствие клавиш вторым списком значило бы
    /// заставить сверять два места. Берётся она из карты действий, поэтому переназначение
    /// отражается здесь само.
    /// </summary>
    private void Rebuild(List<(OrderKind Kind, bool Soft)> lines, OrderKind? aimed)
    {
        foreach (var child in _list.GetChildren())
            child.QueueFree();

        foreach (var (kind, soft) in lines)
        {
            bool active = aimed == kind;

            var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 6);

            var name = new Label
            {
                // Выбранный вид помечается стрелкой, а не только цветом: подсветка одним
                // оттенком неразличима рядом с мягким приказом, который тоже приглушён
                Text = (active ? "▸ " : "") + (soft ? $"{Order.Name(kind)} *" : Order.Name(kind)),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            name.AddThemeFontSizeOverride("font_size", 14);

            // Тот же цвет, что у линии приказа на карте: подсказка и очередь читаются вместе.
            // Мягкий приказ чуть бледнее — видно, что к исполнению он не принимается.
            var tint = Order.Tint(kind);
            if (soft)
                tint = new Color(tint.R, tint.G, tint.B, 0.55f);

            name.AddThemeColorOverride("font_color", tint);
            row.AddChild(name);

            string label = InputActions.KeyLabel(InputActions.ActionOf(kind));

            if (label.Length > 0)
            {
                var hint = new Label { Text = label };
                hint.AddThemeFontSizeOverride("font_size", 12);
                hint.AddThemeColorOverride("font_color",
                    new Color(tint.R, tint.G, tint.B, soft ? 0.4f : 0.7f));
                row.AddChild(hint);
            }

            _list.AddChild(row);
        }
    }
}
