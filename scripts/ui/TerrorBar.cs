using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Плашки террора у правого края: итог, график сырых замеров со сглаженной кривой,
/// бюджет постоянного давления, вход и бюджет волны справа от итога, и слагаемые,
/// из которых итог собран. Слагаемое, выключенное в настройках, на панель не выводится:
/// нулевой вклад иначе читался бы как поломка показателя.
///
/// ПОКАЗЫВАЕТСЯ ВКЛАД ПОСЛЕ КРИВОЙ, а не сырая сумма весов. Это не оформительская мелочь:
/// по сырой сумме игрок не смог бы объяснить себе, почему двадцать новых заборов не сдвинули
/// итог, и решил бы, что показатель сломан. Сырая величина стоит рядом мелким шрифтом —
/// она отвечает на другой вопрос, «сколько всего», и нужна при настройке кривых.
///
/// ЧИСЛО — МГНОВЕННЫЙ ПОКАЗАТЕЛЬ, КРИВАЯ — СГЛАЖЕННЫЙ. Давление и волны читают сглаженное
/// значение; столбики показывают сырые замеры за отрезок длиной в постоянную сглаживания,
/// чтобы было видно, от чего сглаженное отстаёт. У волны сглаженный вход выводится числом
/// всегда, в том числе когда он совпадает с итогом: иначе нельзя отличить «волна читает
/// то же» от «волна читает другое, но подпись скрыта». Бюджет давления и бюджет волны
/// читаются уже готовые — из <see cref="PressureSystem"/> и <see cref="WaveSystem"/>.
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
        public Control Root;
        public Label Value;
        public Label Raw;
    }

    private Label _total;
    private Label _pressureBudget;
    private Label _waveBudget;
    private Spark _spark;
    private Control _partsSeparator;
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

        var rows = new VBoxContainer { CustomMinimumSize = new Vector2(180, 0) };
        rows.AddThemeConstantOverride("separation", 2);
        panel.AddChild(rows);

        (_total, _pressureBudget, _waveBudget) = AddTotal(rows);
        _spark = AddSpark(rows);

        _partsSeparator = new HSeparator();
        rows.AddChild(_partsSeparator);

        _production = AddPlate(rows, "производство");
        _expansion = AddPlate(rows, "экспансия");
        _army = AddPlate(rows, "армия");

        // Время стоит последним и показывается наравне с прочими: игрок должен видеть,
        // какая часть давления пришла от него самого, а какая набежала сама
        _time = AddPlate(rows, "время");
    }

    private static (Label total, Label pressure, Label wave) AddTotal(Node parent)
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

        var budgets = new VBoxContainer();
        budgets.AddThemeConstantOverride("separation", 0);
        row.AddChild(budgets);

        var pressure = BudgetLabel();
        var wave = BudgetLabel();
        budgets.AddChild(pressure);
        budgets.AddChild(wave);

        return (value, pressure, wave);
    }

    private static Label BudgetLabel()
    {
        var label = new Label
        {
            Text = "P: —",
            HorizontalAlignment = HorizontalAlignment.Right,
            CustomMinimumSize = new Vector2(88, 0),
        };
        label.AddThemeFontSizeOverride("font_size", 11);
        label.AddThemeColorOverride("font_color", RawColor);
        return label;
    }

    private static Spark AddSpark(Node parent)
    {
        var spark = new Spark
        {
            CustomMinimumSize = new Vector2(0, 36),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        parent.AddChild(spark);
        return spark;
    }

    private static Plate AddPlate(Node parent, string caption)
    {
        var row = new HBoxContainer();
        parent.AddChild(row);

        var name = new Label { Text = caption };
        name.AddThemeFontSizeOverride("font_size", 11);
        name.AddThemeColorOverride("font_color", PartColor);
        row.AddChild(name);

        var plate = new Plate { Root = row };

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

        var pressure = GameManager.I.System<PressureSystem>();
        _pressureBudget.Text = pressure != null ? $"P: {pressure.Budget:0.#}" : "P: —";

        var waves = GameManager.I.System<WaveSystem>();
        _waveBudget.Text = WaveLine(waves);

        var settings = terror.Settings;
        bool production = settings?.ProductionEnabled ?? true;
        bool expansion = settings?.ExpansionEnabled ?? true;
        bool army = settings?.ArmyEnabled ?? true;
        bool time = settings?.TimeEnabled ?? true;

        _partsSeparator.Visible = production || expansion || army || time;

        Show(_production, terror.Production, terror.RawProduction, production);
        Show(_expansion, terror.Expansion, terror.RawExpansion, expansion);
        Show(_army, terror.Army, terror.RawArmy, army);

        _time.Root.Visible = time;
        if (time)
        {
            // У времени сырая величина — секунды, и в секундах она нечитаема: показываем
            // минуты и секунды, как показывают длительность партии
            _time.Value.Text = $"{terror.Time:0.#}";
            _time.Raw.Text = Elapsed(terror.RawTime);
        }

        var metrics = GameManager.I.Metrics;
        int capacity = SampleCount(settings, metrics?.Step ?? 1f);

        if (metrics != null)
        {
            _spark.Show(
                metrics.Tail("terror.raw", capacity),
                metrics.Tail("terror.smoothed", capacity),
                capacity);
        }
        else
        {
            _spark.Show(ReadOnlySpan<float>.Empty, ReadOnlySpan<float>.Empty, capacity);
        }
    }

    /// <summary>
    /// Вход волны и её бюджет. Сглаженный террор выводится всегда: это то число,
    /// которым волна пользуется при отборе и при пересчёте бюджета, и скрывать его
    /// при совпадении с итогом нельзя — тогда не видно, что волна читает именно его.
    /// </summary>
    private static string WaveLine(WaveSystem waves)
    {
        if (waves == null)
            return "W: —";

        return waves.HasLaunched
            ? $"W: {waves.Terror:0.#} → {waves.Budget:0.#}"
            : $"W: {waves.Terror:0.#}";
    }

    /// <summary>
    /// Сколько замеров укладывается в постоянную сглаживания. Это не окно, по которому
    /// считается экспоненциальное среднее, а сопоставимый с ним отрезок: за это время
    /// вклад старого значения падает в e раз.
    ///
    /// Шаг берётся у самого ряда, а не у настроек террора: ряд ведёт <see cref="MetricsSystem"/>
    /// со своим тактом, и при расхождении тактов отрезок оказался бы иной длины, чем показано.
    /// </summary>
    private static int SampleCount(TerrorSettings settings, float step)
    {
        float interval = Mathf.Max(0.01f, step);
        float tau = Mathf.Max(0.01f, settings?.SmoothingSeconds ?? 60f);
        return Mathf.Max(1, Mathf.CeilToInt(tau / interval));
    }

    private static void Show(Plate plate, float value, float raw, bool enabled)
    {
        plate.Root.Visible = enabled;
        if (!enabled)
            return;

        plate.Value.Text = $"{value:0.#}";
        plate.Raw.Text = $"{raw:0.#}";
    }

    private static string Elapsed(float seconds)
    {
        int total = Mathf.FloorToInt(seconds);

        return $"{total / 60}:{total % 60:00}";
    }

    /// <summary>
    /// Столбики — сырые замеры за отрезок длиной в постоянную сглаживания;
    /// кривая поверх — сглаженный ряд за тот же отрезок. Число столбиков равно
    /// постоянной, выраженной в шагах замера. Недостающие слева слоты пусты:
    /// ширина столбика не зависит от того, сколько партии уже прошло.
    /// </summary>
    private sealed partial class Spark : Control
    {
        private static readonly Color BarColor = new(1f, 0.62f, 0.45f, 0.45f);
        private static readonly Color CurveColor = new(1f, 0.85f, 0.7f);

        private readonly List<float> _raw = new();
        private readonly List<float> _smoothed = new();
        private Vector2[] _curve = Array.Empty<Vector2>();
        private int _capacity = 1;

        public void Show(ReadOnlySpan<float> raw, ReadOnlySpan<float> smoothed, int capacity)
        {
            _capacity = Mathf.Max(1, capacity);
            Copy(_raw, raw);
            Copy(_smoothed, smoothed);
            QueueRedraw();
        }

        public override void _Draw()
        {
            var size = Size;

            if (size.X <= 1f || size.Y <= 1f)
                return;

            int count = Mathf.Min(_raw.Count, _smoothed.Count);
            float peak = Peak(count);

            if (peak <= 0f)
                return;

            float slot = size.X / _capacity;
            float barWidth = Mathf.Max(1f, slot - 1f);
            int pad = _capacity - count;

            for (int i = 0; i < count; i++)
            {
                float height = _raw[i] / peak * size.Y;
                float x = (pad + i) * slot;
                DrawRect(new Rect2(x, size.Y - height, barWidth, height), BarColor);
            }

            DrawCurve(count, pad, slot, size.Y, peak);
        }

        private void DrawCurve(int count, int pad, float slot, float height, float peak)
        {
            if (count < 2)
                return;

            if (_curve.Length != count)
                _curve = new Vector2[count];

            for (int i = 0; i < count; i++)
            {
                float x = (pad + i + 0.5f) * slot;
                float y = height - _smoothed[i] / peak * height;
                _curve[i] = new Vector2(x, y);
            }

            DrawPolyline(_curve, CurveColor, 1.5f, true);
        }

        private float Peak(int count)
        {
            float peak = 0f;

            for (int i = 0; i < count; i++)
            {
                if (_raw[i] > peak)
                    peak = _raw[i];

                if (_smoothed[i] > peak)
                    peak = _smoothed[i];
            }

            return peak;
        }

        private static void Copy(List<float> target, ReadOnlySpan<float> source)
        {
            target.Clear();

            for (int i = 0; i < source.Length; i++)
                target.Add(source[i]);
        }
    }
}
