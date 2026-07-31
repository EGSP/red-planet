using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Интерфейс: ресурсы общего хранилища, выделение и строительная панель.
/// Кнопки только порождают намерение — постановку делает CommandSystem.
/// </summary>
public partial class Hud : CanvasLayer
{
    /// <summary>Размер ячейки строительной панели. Один на все кнопки — иначе сетка поплывёт.</summary>
    private static readonly Vector2 Cell = new(112, 28);

    private Label _metal;
    private Label _energy;
    private Label _efficiency;
    private Label _combat;
    private Label _selection;
    private Label _status;

    private VBoxContainer _buildbar;

    /// <summary>
    /// Отпечаток выделения: список панелей выделенных строителей. Пересобираем сетку
    /// только когда он сменился — иначе кнопки перестраивались бы каждый кадр,
    /// теряя наведение и нажатие.
    /// </summary>
    private string _buildbarKey = "";

    public override void _Ready()
    {
        var panel = new PanelContainer
        {
            Position = new Vector2(12, 12),
            CustomMinimumSize = new Vector2(240, 0),
        };
        AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        panel.AddChild(box);

        _metal = new Label { Text = "метал 0" };
        _metal.AddThemeFontSizeOverride("font_size", 17);
        _metal.AddThemeColorOverride("font_color", new Color(0.85f, 0.88f, 0.95f));
        box.AddChild(_metal);

        _energy = new Label { Text = "энергия 0" };
        _energy.AddThemeFontSizeOverride("font_size", 17);
        _energy.AddThemeColorOverride("font_color", new Color(1f, 0.92f, 0.5f));
        box.AddChild(_energy);

        _efficiency = new Label { Text = "производительность 100%" };
        _efficiency.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(_efficiency);

        _combat = new Label { Text = "врагов 0" };
        _combat.AddThemeFontSizeOverride("font_size", 13);
        _combat.AddThemeColorOverride("font_color", new Color(1f, 0.7f, 0.65f));
        box.AddChild(_combat);

        box.AddChild(new HSeparator());

        // Набор приказов выделенного показываем прямо здесь: он же и есть ответ
        // на вопрос «что этому юниту вообще можно поручить»
        _selection = new Label { Text = "" };
        _selection.AddThemeFontSizeOverride("font_size", 12);
        _selection.AddThemeColorOverride("font_color", new Color(0.65f, 0.95f, 0.75f));
        box.AddChild(_selection);

        // Строительная панель пуста, пока не выделен строитель, и места тогда не занимает
        _buildbar = new VBoxContainer { Visible = false };
        _buildbar.AddThemeConstantOverride("separation", 4);
        box.AddChild(_buildbar);

        box.AddChild(new HSeparator());

        _status = new Label { Text = "ПКМ — идти, копать или атаковать" };
        _status.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(_status);

        AddPauseButton(box);
    }

    /// <summary>
    /// Кнопка паузы просит сессию переключить состояние, а не показывает меню сама:
    /// решение о паузе принимается в одном месте, поэтому клавиша и кнопка не разойдутся.
    /// </summary>
    private void AddPauseButton(Node parent)
    {
        var button = new Button { Text = "Пауза (Esc)" };
        button.Pressed += () => this.Ancestor<Session>()?.TogglePause();
        parent.AddChild(button);
    }

    public override void _Process(double delta)
    {
        var gm = GameManager.I;
        if (gm == null)
            return;

        var stockpile = gm.Stockpile;
        var economy = gm.Economy;

        _metal.Text = Line("метал", stockpile, ResourceKind.Metal,
            economy.MetalIncome, economy.MetalDemand);

        _energy.Text = Line("энергия", stockpile, ResourceKind.Energy,
            economy.EnergyIncome, economy.EnergyDemand);

        // Просадку показываем цветом: при полном ходе число не должно мозолить глаз
        int percent = Mathf.RoundToInt(economy.Efficiency * 100f);
        _efficiency.Text = $"производительность {percent}%";
        _efficiency.AddThemeColorOverride("font_color", percent >= 95
            ? new Color(0.6f, 0.85f, 0.6f)
            : percent >= 50
                ? new Color(0.95f, 0.8f, 0.4f)
                : new Color(1f, 0.5f, 0.45f));

        var combat = gm.Combat;
        float commanderDamage = gm.Commander?.Health.TotalTaken ?? 0f;

        _combat.Text = $"врагов на карте {combat.EnemiesAlive}   " +
                       $"уничтожено {combat.EnemiesDestroyed}   потеряно {combat.LossesTaken}\n" +
                       $"урон коммандеру {commanderDamage:0}";

        _selection.Text = SelectionLine(gm.Command);

        RefreshBuildbar(gm.Command);

        var pending = gm.Command?.Pending;
        _status.Text = pending != null
            ? $"ставим: {pending.DisplayName}\nЛКМ — поставить, ПКМ — отмена"
            : "ЛКМ — выделить или рамка, ПКМ — приказ по цели\n" +
              "Shift — дописать в очередь, WASD и колесо — камера";
    }

    // ── строительная панель ────────────────────────────────────────────────────

    /// <summary>
    /// Панели выделенных строителей. Тот, кто строить не умеет, панели не имеет вовсе,
    /// поэтому отдельной проверки «есть ли среди выделенных строитель» не нужно:
    /// пустой список и есть ответ.
    /// </summary>
    private static List<BuildbarDef> BarsOf(CommandSystem command)
    {
        var bars = new List<BuildbarDef>();

        if (command == null)
            return bars;

        foreach (var actor in command.Selected)
        {
            if (actor is not Unit { Def: { CanBuild: true } def })
                continue;

            var bar = Content.Catalog.Buildbar(def.Buildbar);

            if (bar != null && !bars.Contains(bar))
                bars.Add(bar);
        }

        return bars;
    }

    private void RefreshBuildbar(CommandSystem command)
    {
        var bars = BarsOf(command);
        string key = string.Join('|', bars.Select(bar => bar.Id).OrderBy(id => id));

        if (key == _buildbarKey)
            return;

        _buildbarKey = key;

        foreach (var child in _buildbar.GetChildren())
            child.QueueFree();

        if (bars.Count == 0)
        {
            _buildbar.Visible = false;
            return;
        }

        _buildbar.Visible = true;
        BuildSections(BuildbarLayout.Merge(bars));
    }

    private void BuildSections(BuildbarLayout layout)
    {
        _buildbar.AddChild(new HSeparator());

        foreach (var section in layout.Sections)
        {
            var title = new Label { Text = section.Title };
            title.AddThemeFontSizeOverride("font_size", 13);
            title.AddThemeColorOverride("font_color", new Color(0.75f, 0.78f, 0.85f));
            _buildbar.AddChild(title);

            // Снизу вверх в справочнике — сверху вниз на экране
            for (int y = section.Rows.Count - 1; y >= 0; y--)
                AddRow(section.Rows[y]);
        }
    }

    private void AddRow(BuildbarLayout.Row row)
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 4);
        _buildbar.AddChild(line);

        foreach (var buildableId in row.Cells)
        {
            // Пустая ячейка держит место: без распорки соседи съехали бы влево
            // и позиции разошлись бы с другими панелями
            if (buildableId == null)
            {
                line.AddChild(new Control { CustomMinimumSize = Cell });
                continue;
            }

            var def = Content.Catalog.Buildable(buildableId);

            if (def == null)
            {
                GD.PushWarning($"[Hud] в строительной панели неизвестная постройка: {buildableId}");
                line.AddChild(new Control { CustomMinimumSize = Cell });
                continue;
            }

            line.AddChild(BuildButton(def));
        }
    }

    private static Button BuildButton(BuildableDef def)
    {
        // Энергоцены у постройки нет: энергию тратит инструмент строителя, а не здание
        var button = new Button
        {
            Text = def.DisplayName,
            CustomMinimumSize = Cell,
            ClipText = true,
            TooltipText = $"{def.DisplayName}\n{def.CostMetal:0} метала",
        };

        button.AddThemeFontSizeOverride("font_size", 12);
        button.Pressed += () => GameManager.I?.Command?.BeginBuild(def);

        return button;
    }

    /// <summary>
    /// Что выделено и что этому можно приказать. Набор берём у самой сущности: он же
    /// и работает фильтром, поэтому показанное здесь и принятое на деле не разойдутся.
    /// </summary>
    private static string SelectionLine(CommandSystem command)
    {
        var selected = command?.Selected;

        if (selected == null || selected.Count == 0)
            return "не выделено никого — приказы идут коммандеру";

        if (selected.Count > 1)
            return $"выделено: {selected.Count}";

        var actor = selected[0];
        string kinds = string.Join(", ", actor.AllowedOrders.Kinds.Select(Order.Name));

        return $"{actor.DisplayName} — можно: {kinds}\nв очереди приказов: {actor.Orders.Count}";
    }

    /// <summary>
    /// Строка ресурса в духе PA: запас из потолка, а рядом приход и ЗАПРОШЕННЫЙ расход.
    ///
    /// Показываем именно спрос, а не то, сколько удалось влить. Ужатый расход всегда
    /// выглядит почти сведённым с доходом — по нему не понять, чего и насколько не хватает,
    /// а по спросу видно сразу: столько-то генераторов недостаёт.
    /// </summary>
    private static string Line(string name, StockpileProjection stockpile, ResourceKind kind,
        float income, float demand)
    {
        float net = income - demand;
        string sign = net >= 0f ? "+" : "−";

        return $"{name} {stockpile.Get(kind):0}/{stockpile.Capacity(kind):0}   " +
               $"{sign}{Mathf.Abs(net):0.#}/с   (+{income:0.#} −{demand:0.#})";
    }
}
