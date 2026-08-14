using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

/// <summary>
/// Построение виджета правки по описанию поля <see cref="ContentFieldSpec"/>.
///
/// ЗАЧЕМ ОТДЕЛЬНО ОТ ФОРМЫ. Форма свойств и инспектор волн правят одни и те же типы
/// значений, и вторая копия разбора чисел и списков разошлась бы с первой при первом же
/// исправлении. Здесь остаётся только соответствие «тип значения — виджет»; куда записать
/// результат, решает вызывающая сторона через обратный вызов.
///
/// РАЗБОР ВВОДА. Числа читаются в инвариантной культуре, а затем в текущей: в русской
/// раскладке запятая набирается чаще точки. Неразобранный ввод не записывается вовсе,
/// то есть поле возвращается к прежнему значению при следующей перерисовке.
/// </summary>
public static class EditorFieldEditor
{
    /// <summary>Длинное значение занимает всю ширину строки и переносится под заголовок.</summary>
    public static bool BelowTitle(ContentFieldSpec field) =>
        field.Type is ContentFieldType.StringList
            or ContentFieldType.Vector2List
            or ContentFieldType.FloatList;

    public static Control Build(
        ContentFieldSpec field, object value, bool editable, Action<object> commit)
    {
        switch (field.Type)
        {
            case ContentFieldType.Bool:
            {
                var box = new CheckBox
                {
                    ButtonPressed = value is true,
                    Disabled = !editable,
                    Text = "",
                };
                box.Toggled += on => commit(on);
                return box;
            }

            case ContentFieldType.Enum when field.EnumType != null:
            {
                var option = new OptionButton { Disabled = !editable };
                string current = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                int selected = 0;
                int i = 0;
                foreach (string name in Enum.GetNames(field.EnumType))
                {
                    option.AddItem(name);
                    option.SetItemMetadata(i, name);
                    if (string.Equals(name, current, StringComparison.OrdinalIgnoreCase))
                        selected = i;
                    i++;
                }

                option.Selected = selected;
                option.ItemSelected += index =>
                    commit(option.GetItemMetadata((int)index).AsString());
                return option;
            }

            case ContentFieldType.Color:
            {
                var picker = new ColorPickerButton
                {
                    Color = value is Color color ? color : Colors.White,
                    Disabled = !editable,
                    CustomMinimumSize = new Vector2(80, 28),
                };
                Color pending = picker.Color;
                picker.ColorChanged += color => pending = color;
                // Во время перетаскивания маркера ColorChanged приходит каждый пиксель.
                // Пересборка каталога выполняется один раз при закрытии.
                picker.PopupClosed += () => commit(pending);
                return picker;
            }

            case ContentFieldType.StringList:
            {
                var edit = TextField(Format(value), editable);
                Commit(edit, editable, text => commit(SplitList(text)));
                return edit;
            }

            case ContentFieldType.FloatList:
            {
                var edit = TextField(Format(value), editable);
                edit.PlaceholderText = "10, -1";
                Commit(edit, editable, text =>
                {
                    var numbers = ParseFloats(text);
                    if (numbers != null)
                        commit(numbers);
                });
                return edit;
            }

            case ContentFieldType.Vector2List:
            {
                var edit = TextField(Format(value), editable);
                edit.PlaceholderText = "1,0; 0,1; -1,0";
                edit.TooltipText = "X,Y pairs separated by semicolons";
                Commit(edit, editable, text =>
                {
                    var vectors = ParseVectors(text);
                    if (vectors != null)
                        commit(vectors);
                });
                return edit;
            }

            default:
            {
                var edit = TextField(Format(value), editable);
                Commit(edit, editable, text =>
                {
                    object parsed = ParseScalar(field, text);
                    if (parsed != null)
                        commit(parsed);
                });
                return edit;
            }
        }
    }

    public static string Format(object value) => value switch
    {
        null => "",
        float f => f.ToString("0.###", CultureInfo.InvariantCulture),
        double d => d.ToString("0.###", CultureInfo.InvariantCulture),
        Color c => $"{c.R:0.##}, {c.G:0.##}, {c.B:0.##}",
        IEnumerable<string> list => string.Join(", ", list),
        IEnumerable<float> floats =>
            string.Join(", ", floats.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture))),
        Vector2[] vectors => string.Join("; ", vectors.Select(v => $"{v.X:0.##},{v.Y:0.##}")),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    public static object ParseScalar(ContentFieldSpec field, string text) => field.Type switch
    {
        ContentFieldType.Float or ContentFieldType.NullableFloat or ContentFieldType.Scale =>
            ParseFloat(text),
        ContentFieldType.Int =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                ? n
                : null,
        _ => text,
    };

    public static List<string> SplitList(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    private static object ParseFloat(string text)
    {
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            return value;

        return float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            ? value
            : null;
    }

    private static List<float> ParseFloats(string text)
    {
        var result = new List<float>();
        foreach (string part in text.Split(
                     ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (ParseFloat(part) is not float number)
                return null;

            result.Add(number);
        }

        return result;
    }

    private static List<Vector2> ParseVectors(string text)
    {
        var vectors = new List<Vector2>();
        foreach (string pair in text.Split(
                     ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] coordinates = pair.Split(
                ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (coordinates.Length != 2
                || ParseFloat(coordinates[0]) is not float x
                || ParseFloat(coordinates[1]) is not float y)
            {
                return null;
            }

            vectors.Add(new Vector2(x, y));
        }

        return vectors;
    }

    private static LineEdit TextField(string text, bool editable) => new()
    {
        Text = text,
        Editable = editable,
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
    };

    /// <summary>
    /// Ввод принимается и по Enter, и по потере фокуса: правка, после которой мышь ушла
    /// на другое поле, иначе терялась бы.
    /// </summary>
    private static void Commit(LineEdit edit, bool editable, Action<string> apply)
    {
        edit.TextSubmitted += text => apply(text);
        edit.FocusExited += () =>
        {
            if (editable)
                apply(edit.Text);
        };
    }
}
