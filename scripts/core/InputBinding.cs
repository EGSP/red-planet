using Godot;

/// <summary>
/// Привязка действия: либо физическая клавиша, либо кнопка мыши. Пустая привязка означает,
/// что действию сейчас ничего не назначено.
///
/// ПОЧЕМУ ОДИН ТИП НА ДВА УСТРОЙСТВА, А НЕ ДВА ПОЛЯ У ДЕЙСТВИЯ. Назначение всегда
/// единственно: у действия ровно одна привязка, и показанное в настройках обязано совпадать
/// с тем, что срабатывает. Два поля допускали бы сочетание «клавиша и кнопка разом»,
/// которого система назначения не поддерживает, и каждое место разбора обязано было бы
/// решать, что делать при таком сочетании.
///
/// КЛАВИША ЗАДАЁТСЯ ФИЗИЧЕСКАЯ, А НЕ ПО РАСКЛАДКЕ, — по той же причине, что и в
/// <see cref="InputActions"/>: на русской раскладке движок возвращает в
/// <see cref="InputEventKey.Keycode"/> букву раскладки, а не клавишу под пальцем.
/// </summary>
public readonly record struct InputBinding(Key Key, MouseButton Button)
{
    /// <summary>Приставка клавиатурной записи в пользовательском файле настроек.</summary>
    private const string KeyTag = "key:";

    /// <summary>Приставка записи кнопки мыши в пользовательском файле настроек.</summary>
    private const string MouseTag = "mouse:";

    /// <summary>Действие без привязки: законное состояние, см. <see cref="InputActions.Bind"/>.</summary>
    public static readonly InputBinding None = new(Key.None, MouseButton.None);

    public static InputBinding Of(Key key) => new(key, MouseButton.None);

    public static InputBinding Of(MouseButton button) => new(Key.None, button);

    /// <summary>
    /// Клавиша обращается в привязку сама собой: карта действий объявляет умолчания
    /// перечнем, и требование писать <c>InputBinding.Of(Key.A)</c> в каждой строке
    /// удлинило бы объявление, ничего не прояснив.
    /// </summary>
    public static implicit operator InputBinding(Key key) => Of(key);

    public bool IsEmpty => Key == Key.None && Button == MouseButton.None;

    /// <summary>
    /// Переназначать можно только те кнопки мыши, которых не касается разбор жестов.
    ///
    /// Левая и правая заняты выделением и приказами, причём читаются они не через карту
    /// действий, а разбором нажатия, протаскивания и отпускания порознь (см.
    /// <c>CommandSystem</c>). Отдать их действию карты означало бы, что настройки обещают
    /// переназначение, которого разбор жестов не заметит. Колесо отвергается потому,
    /// что события прокрутки не имеют отпускания, и удержание на них не выражается.
    /// </summary>
    public static bool CanBind(MouseButton button) => button is MouseButton.Middle
        or MouseButton.Xbutton1 or MouseButton.Xbutton2;

    /// <summary>Подпись для показа игроку, либо пустая строка у пустой привязки.</summary>
    public string Label()
    {
        if (Button != MouseButton.None)
            return Button switch
            {
                MouseButton.Left => "ЛКМ",
                MouseButton.Right => "ПКМ",
                MouseButton.Middle => "СКМ",
                MouseButton.WheelUp => "Колесо вверх",
                MouseButton.WheelDown => "Колесо вниз",
                MouseButton.Xbutton1 => "Мышь 4",
                MouseButton.Xbutton2 => "Мышь 5",
                _ => $"Мышь {(int)Button}",
            };

        return Key == Key.None ? "" : OS.GetKeycodeString(Key);
    }

    /// <summary>Событие карты действий, отвечающее привязке, либо null у пустой.</summary>
    public InputEvent ToEvent()
    {
        if (Button != MouseButton.None)
            return new InputEventMouseButton { ButtonIndex = Button };

        return Key == Key.None ? null : new InputEventKey { PhysicalKeycode = Key };
    }

    /// <summary>Привязка, отвечающая событию карты, либо пустая при событии иного вида.</summary>
    public static InputBinding Of(InputEvent @event) => @event switch
    {
        InputEventKey key => Of(key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode),
        InputEventMouseButton mouse => Of(mouse.ButtonIndex),
        _ => None,
    };

    /// <summary>
    /// Запись для пользовательского файла настроек. Строка, а не число: устройство
    /// приходится различать, а числовые коды клавиш и кнопок пересекаются, и запись,
    /// прочитанная не тем способом, дала бы правдоподобную, но чужую привязку.
    /// </summary>
    public string Store() =>
        Button != MouseButton.None ? MouseTag + (int)Button : KeyTag + (int)Key;

    /// <summary>
    /// Разбор записи из файла настроек. Число без приставки понимается как код клавиши:
    /// так писались настройки до того, как появились кнопки мыши, и терять уже сделанные
    /// игроком переназначения ради смены формата незачем.
    /// </summary>
    public static InputBinding Parse(Variant value)
    {
        if (value.VariantType == Variant.Type.Int)
            return Of((Key)(int)value);

        string text = value.AsString();

        if (text.StartsWith(MouseTag) && int.TryParse(text[MouseTag.Length..], out int button))
            return Of((MouseButton)button);

        if (text.StartsWith(KeyTag) && int.TryParse(text[KeyTag.Length..], out int key))
            return Of((Key)key);

        return None;
    }
}
