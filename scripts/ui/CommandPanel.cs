using System.Collections.Generic;
using Godot;

/// <summary>
/// Панель приказов у правого края: что доступно текущему выделению.
///
/// Набор берём у самих сущностей — <c>AllowedOrders</c> работает и справкой, и фильтром,
/// поэтому показанное здесь и принятое на деле разойтись не могут. Приказы объединяются:
/// если в отряде есть хоть один умеющий копать, «копать» в списке будет, хотя уйдёт
/// он только копателям. Так и должно быть — приказ отдаётся отряду, а разбирают его
/// по себе сами исполнители.
///
/// Кнопок здесь нет намеренно: приказ отдаётся правым щелчком по цели, и вид приказа
/// выбирает сама цель. Список — подсказка, а не пульт.
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
        var selected = GameManager.I?.Command?.Selected;

        if (selected == null || selected.Count == 0)
        {
            _frame.Visible = false;
            _key = "";
            return;
        }

        var kinds = KindsOf(selected);
        string key = string.Join('|', kinds);

        _frame.Visible = kinds.Count > 0;

        if (key == _key)
            return;

        _key = key;
        Rebuild(kinds);
    }

    /// <summary>Объединение наборов выделенных, в порядке объявления видов приказов.</summary>
    private static List<OrderKind> KindsOf(IReadOnlyList<IOrderable> selected)
    {
        var union = OrderSet.None;

        foreach (var actor in selected)
            foreach (var kind in actor.AllowedOrders.Kinds)
                union = union.With(kind);

        return new List<OrderKind>(union.Kinds);
    }

    private void Rebuild(List<OrderKind> kinds)
    {
        foreach (var child in _list.GetChildren())
            child.QueueFree();

        foreach (var kind in kinds)
        {
            var line = new Label { Text = Order.Name(kind) };
            line.AddThemeFontSizeOverride("font_size", 14);

            // Тот же цвет, что у линии приказа на карте: подсказка и очередь читаются вместе
            line.AddThemeColorOverride("font_color", Order.Tint(kind));

            _list.AddChild(line);
        }
    }
}
