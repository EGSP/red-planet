using Godot;

/// <summary>
/// Плашки террора у правого края: итог и четыре слагаемых, из которых он собран.
///
/// ПОКАЗЫВАЕТСЯ ВКЛАД ПОСЛЕ КРИВОЙ, а не сырая сумма весов. Это не оформительская мелочь:
/// по сырой сумме игрок не смог бы объяснить себе, почему двадцать новых заборов не сдвинули
/// итог, и решил бы, что показатель сломан. Сырая величина стоит рядом мелким шрифтом —
/// она отвечает на другой вопрос, «сколько всего», и нужна при настройке кривых.
///
/// Сглаженное значение сюда не выводится намеренно: игроку показывается то, что он сделал,
/// а задержка есть внутреннее устройство давления.
///
/// Отдельным слоем, как и полоса ресурсов: показывает состояние базы целиком и потому
/// не зависит ни от выделения, ни от того, чем игрок сейчас занят.
/// </summary>
public partial class TerrorBar : CanvasLayer
{
    private static readonly Color TerrorColor = new(1f, 0.62f, 0.45f);
    private static readonly Color PartColor = new(0.78f, 0.78f, 0.82f);
    private static readonly Color RawColor = new(0.55f, 0.55f, 0.6f);

    /// <summary>Одно слагаемое: имя, вклад в очках террора и сырая величина под ним.</summary>
    private sealed class Plate
    {
        public Label Value;
        public Label Raw;
    }

    private Label _total;
    private Plate _production;
    private Plate _expansion;
    private Plate _army;
    private Plate _time;

    public override void _Ready()
    {
        var frame = new UiFrame();
        AddChild(frame);

        // Раскладка та же, что у полосы ресурсов, и по той же причине: якорь на контейнере
        // нулевого размера не работает — прижимать надо цепочкой контейнеров от каркаса,
        // который размер от окна получает
        var column = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Begin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        frame.AddChild(column);
        column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 12);
        column.AddChild(margin);

        var right = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        margin.AddChild(right);

        var panel = new PanelContainer();
        right.AddChild(panel);

        var rows = new VBoxContainer { CustomMinimumSize = new Vector2(160, 0) };
        rows.AddThemeConstantOverride("separation", 2);
        panel.AddChild(rows);

        _total = AddTotal(rows);

        rows.AddChild(new HSeparator());

        _production = AddPlate(rows, "производство");
        _expansion = AddPlate(rows, "экспансия");
        _army = AddPlate(rows, "армия");

        // Время стоит последним и показывается наравне с прочими: игрок должен видеть,
        // какая часть давления пришла от него самого, а какая набежала сама
        _time = AddPlate(rows, "время");
    }

    private static Label AddTotal(Node parent)
    {
        var row = new HBoxContainer();
        parent.AddChild(row);

        var caption = new Label { Text = "ТЕРРОР" };
        caption.AddThemeFontSizeOverride("font_size", 10);
        caption.AddThemeColorOverride("font_color", TerrorColor);
        row.AddChild(caption);

        var value = new Label
        {
            Text = "0",
            HorizontalAlignment = HorizontalAlignment.Right,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        value.AddThemeFontSizeOverride("font_size", 18);
        value.AddThemeColorOverride("font_color", TerrorColor);
        row.AddChild(value);

        return value;
    }

    private static Plate AddPlate(Node parent, string caption)
    {
        var row = new HBoxContainer();
        parent.AddChild(row);

        var name = new Label { Text = caption };
        name.AddThemeFontSizeOverride("font_size", 11);
        name.AddThemeColorOverride("font_color", PartColor);
        row.AddChild(name);

        var plate = new Plate();

        // Сырая величина идёт перед вкладом и мельче: главное здесь — очки террора,
        // а «сколько всего» служит подсказкой при настройке
        plate.Raw = new Label
        {
            Text = "0",
            HorizontalAlignment = HorizontalAlignment.Right,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        plate.Raw.AddThemeFontSizeOverride("font_size", 10);
        plate.Raw.AddThemeColorOverride("font_color", RawColor);
        row.AddChild(plate.Raw);

        plate.Value = new Label
        {
            Text = "0",
            HorizontalAlignment = HorizontalAlignment.Right,
            CustomMinimumSize = new Vector2(34, 0),
        };
        plate.Value.AddThemeFontSizeOverride("font_size", 13);
        plate.Value.AddThemeColorOverride("font_color", PartColor);
        row.AddChild(plate.Value);

        return plate;
    }

    public override void _Process(double delta)
    {
        var terror = GameManager.I?.System<TerrorSystem>();

        if (terror == null)
            return;

        _total.Text = $"{terror.Raw:0}";

        Show(_production, terror.Production, terror.RawProduction);
        Show(_expansion, terror.Expansion, terror.RawExpansion);
        Show(_army, terror.Army, terror.RawArmy);

        // У времени сырая величина — секунды, и в секундах она нечитаема: показываем
        // минуты и секунды, как показывают длительность партии
        _time.Value.Text = $"{terror.Time:0.#}";
        _time.Raw.Text = Elapsed(terror.RawTime);
    }

    private static void Show(Plate plate, float value, float raw)
    {
        plate.Value.Text = $"{value:0.#}";
        plate.Raw.Text = $"{raw:0.#}";
    }

    private static string Elapsed(float seconds)
    {
        int total = Mathf.FloorToInt(seconds);

        return $"{total / 60}:{total % 60:00}";
    }
}
