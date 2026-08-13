using System;
using System.Globalization;
using System.Linq;
using Godot;

/// <summary>
/// Таблица баланса по итоговому Catalog редактора (с учётом несохранённых черновиков).
///
/// КОЛОНКИ. Часть чисел лежит в UnitDefinition напрямую (HP, цена, скорость).
/// Остальные считаются здесь же: DPS = damage/fire_interval, время сборки =
/// cost/build_power, чистые потоки — производство минус расход инструмента и conversion.
/// Двойной щелчок шлёт OpenRequested — Main переключает режим и открывает вкладку.
/// </summary>
[Tool]
public partial class ContentEditorBalance : VBoxContainer
{
    private ContentEditorStore _store;
    private LineEdit _search;
    private OptionButton _filter;
    private Tree _tree;
    private int _sortColumn;
    private bool _sortAsc = true;

    public event Action<string> OpenRequested;

    public void Bind(ContentEditorStore store)
    {
        _store = store;
        EnsureUi();
        Refresh();
    }

    public void Refresh()
    {
        if (_store?.Catalog == null)
            return;

        EnsureUi();
        _tree.Clear();
        var root = _tree.CreateItem();

        string query = (_search?.Text ?? "").Trim().ToLowerInvariant();
        int filter = _filter?.Selected ?? 0;

        var rows = _store.Catalog.Units
            .Where(u => !string.IsNullOrEmpty(u.Id))
            .Select(BalanceRow.From)
            .Where(r => filter == 0
                        || (filter == 1 && !r.IsStructure)
                        || (filter == 2 && r.IsStructure)
                        || (filter == 3 && r.IsEnemy))
            .Where(r => query.Length == 0
                        || r.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || r.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        rows.Sort((a, b) =>
        {
            int cmp = Compare(a, b, _sortColumn);
            return _sortAsc ? cmp : -cmp;
        });

        foreach (var row in rows)
        {
            var item = _tree.CreateItem(root);
            item.SetText(0, row.Name);
            item.SetText(1, row.Id);
            item.SetText(2, row.Class);
            item.SetText(3, F(row.Health));
            item.SetText(4, F(row.Cost));
            item.SetText(5, F(row.Speed));
            item.SetText(6, F(row.Vision));
            item.SetText(7, row.Size);
            item.SetText(8, F(row.Dps));
            item.SetText(9, F(row.WeaponRange));
            item.SetText(10, F(row.HealthPerMetal));
            item.SetText(11, F(row.BuildTime));
            item.SetText(12, F(row.EnergyNet));
            item.SetText(13, F(row.MetalNet));
            item.SetText(14, F(row.ArmyPower));
            item.SetText(15, F(row.ExpansionPower));
            item.SetText(16, F(row.BuildPower));
            item.SetMetadata(0, row.Id);
        }
    }

    private void EnsureUi()
    {
        if (_tree != null)
            return;

        var bar = new HBoxContainer();
        AddChild(bar);

        _search = new LineEdit
        {
            PlaceholderText = "Поиск…",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _search.TextChanged += _ => Refresh();
        bar.AddChild(_search);

        _filter = new OptionButton();
        _filter.AddItem("Все", 0);
        _filter.AddItem("Подвижные", 1);
        _filter.AddItem("Постройки", 2);
        _filter.AddItem("Противник", 3);
        _filter.ItemSelected += _ => Refresh();
        bar.AddChild(_filter);

        _tree = new Tree
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Columns = 17,
            ColumnTitlesVisible = true,
            HideRoot = true,
            SelectMode = Tree.SelectModeEnum.Row,
        };

        string[] titles =
        {
            "Имя", "Id", "Класс", "HP", "Цена", "Скорость", "Обзор", "Размер",
            "DPS", "Дальность", "HP/металл", "Сборка, с", "Э энергия", "Э металл",
            "Армия", "Экспансия", "Мощность",
        };

        for (int i = 0; i < titles.Length; i++)
        {
            _tree.SetColumnTitle(i, titles[i]);
            _tree.SetColumnExpand(i, i < 2);
            _tree.SetColumnCustomMinimumWidth(i, i < 2 ? 100 : 70);
        }

        _tree.ColumnTitleClicked += (column, _mouse) =>
        {
            int col = (int)column;
            if (_sortColumn == col)
                _sortAsc = !_sortAsc;
            else
            {
                _sortColumn = col;
                _sortAsc = true;
            }

            Refresh();
        };

        _tree.ItemActivated += () =>
        {
            var item = _tree.GetSelected();
            if (item == null)
                return;

            string id = item.GetMetadata(0).AsString();
            OpenRequested?.Invoke(id);
        };

        AddChild(_tree);
    }

    private static int Compare(BalanceRow a, BalanceRow b, int column) => column switch
    {
        0 => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
        1 => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase),
        2 => string.Compare(a.Class, b.Class, StringComparison.OrdinalIgnoreCase),
        3 => a.Health.CompareTo(b.Health),
        4 => a.Cost.CompareTo(b.Cost),
        5 => a.Speed.CompareTo(b.Speed),
        6 => a.Vision.CompareTo(b.Vision),
        7 => string.Compare(a.Size, b.Size, StringComparison.Ordinal),
        8 => a.Dps.CompareTo(b.Dps),
        9 => a.WeaponRange.CompareTo(b.WeaponRange),
        10 => a.HealthPerMetal.CompareTo(b.HealthPerMetal),
        11 => a.BuildTime.CompareTo(b.BuildTime),
        12 => a.EnergyNet.CompareTo(b.EnergyNet),
        13 => a.MetalNet.CompareTo(b.MetalNet),
        14 => a.ArmyPower.CompareTo(b.ArmyPower),
        15 => a.ExpansionPower.CompareTo(b.ExpansionPower),
        16 => a.BuildPower.CompareTo(b.BuildPower),
        _ => 0,
    };

    private static string F(float value) =>
        value == 0f ? "—" : value.ToString("0.##", CultureInfo.InvariantCulture);

    private readonly struct BalanceRow
    {
        public string Id { get; init; }
        public string Name { get; init; }
        public string Class { get; init; }
        public bool IsStructure { get; init; }
        public bool IsEnemy { get; init; }
        public float Health { get; init; }
        public float Cost { get; init; }
        public float Speed { get; init; }
        public float Vision { get; init; }
        public string Size { get; init; }
        public float Dps { get; init; }
        public float WeaponRange { get; init; }
        public float HealthPerMetal { get; init; }
        public float BuildTime { get; init; }
        public float EnergyNet { get; init; }
        public float MetalNet { get; init; }
        public float ArmyPower { get; init; }
        public float ExpansionPower { get; init; }
        public float BuildPower { get; init; }

        public static BalanceRow From(UnitDefinition def)
        {
            float dps = 0f;
            float range = 0f;
            if (def.Weapon != null && def.Weapon.FireInterval > 0f)
            {
                dps = def.Weapon.Damage / def.Weapon.FireInterval;
                range = def.Weapon.Range;
            }

            float cost = def.Assembly?.CostMetal ?? 0f;
            float buildPower = def.BuildTool?.Power ?? def.Plant?.BuildPower ?? 0f;
            float buildTime = buildPower > 0f && cost > 0f ? cost / buildPower : 0f;

            float energyDrain = (def.Conversion?.EnergyDrain ?? 0f)
                                + (def.BuildTool?.EnergyDrain ?? 0f)
                                + ((def.Plant?.BuildPower ?? 0f) * (def.Plant?.EnergyPerPower ?? 0f));

            return new BalanceRow
            {
                Id = def.Id,
                Name = string.IsNullOrEmpty(def.DisplayName) ? def.Id : def.DisplayName,
                Class = def.Class.ToString(),
                IsStructure = def.IsStructure,
                IsEnemy = def.Class == UnitClass.Enemy,
                Health = def.MaxHealth,
                Cost = cost,
                Speed = def.Speed,
                Vision = def.VisionRange,
                Size = def.IsStructure ? $"{def.Width}×{def.Height}" : $"r={def.Radius:0.##}",
                Dps = dps,
                WeaponRange = range,
                HealthPerMetal = def.HealthPerMetal,
                BuildTime = buildTime,
                EnergyNet = def.EnergyProduction - energyDrain,
                MetalNet = def.MetalProduction + (def.Conversion?.MetalOutput ?? 0f),
                ArmyPower = def.ArmyPower,
                ExpansionPower = def.ExpansionPower,
                BuildPower = buildPower,
            };
        }
    }
}
