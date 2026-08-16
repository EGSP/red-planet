using System;
using Godot;

/// <summary>
/// Масштаб интерфейса: набор допустимых значений, текущее и его хранение.
///
/// ПОЧЕМУ СТУПЕНИ, А НЕ НЕПРЕРЫВНЫЙ ОТРЕЗОК. Интерфейс рисуется растром — глифы шрифта,
/// рамки панелей, полосы разделителей. Множитель, не кратный четверти, переводит границы
/// элементов на дробные координаты, и край, приходящийся на половину пикселя, размазывается
/// фильтрацией. Ступень 0.25 выбрана потому, что размеры интерфейса в проекте кратны
/// четырём пикселям: при таком множителе они остаются целыми числами.
///
/// ПОЧЕМУ НЕ <c>Window.ContentScaleFactor</c>. Тот множитель применяется ко всему двумерному
/// слою разом, то есть и к миру, отчего вместе с интерфейсом менялся бы видимый размер карты.
/// Масштабом мира ведает камера, и второй источник того же правила разошёлся бы с нею.
/// Поэтому множитель применяется к слоям интерфейса поимённо — см. <see cref="UiFrame"/>.
///
/// ХРАНИТСЯ ОТДЕЛЬНО ОТ КЛАВИШ (<see cref="Keybinds"/>): это настройка изображения, а не
/// ввода, и общего файла у них нет ровно по той же причине, по какой разведены разделы
/// в самом экране настроек.
/// </summary>
public static class UiScale
{
    private const string Path = "user://display.cfg";
    private const string Section = "display";
    private const string Key = "ui_scale";

    /// <summary>
    /// Допустимые множители по возрастанию. Промежуточных значений не существует:
    /// ползунок ходит по индексам этого набора, а не по вещественному отрезку.
    /// </summary>
    public static readonly float[] Steps = { 0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f };

    public const float Default = 1f;

    /// <summary>
    /// Наименьший логический размер экрана, при котором разметка ещё собирается целиком.
    /// Ниже него экран настроек — самое высокое из окон игры — перестаёт помещаться,
    /// поэтому крупные ступени на малом окне недоступны (см. <see cref="MaxFor"/>).
    ///
    /// Величина взята по фактической высоте этого экрана, а не с запасом: прежний порог
    /// 960 × 600 отсекал ступень 1.25 уже в окне 1152 × 648, то есть при обычном размере
    /// окна набор сводился к двум значениям.
    /// </summary>
    private static readonly Vector2 MinLogicalSize = new(620, 470);

    /// <summary>Выбранный игроком множитель. К слою может быть применён меньший — <see cref="Effective"/>.</summary>
    public static float Current { get; private set; } = Default;

    /// <summary>Множитель изменился. Слушают каркасы интерфейса, чтобы пересобрать разметку.</summary>
    public static event Action Changed;

    /// <summary>
    /// Прочитать сохранённое. Зовётся из <see cref="Root"/> до сборки первого интерфейса.
    /// Отсутствие файла — обычное дело при первом запуске, ошибкой не считается.
    /// </summary>
    public static void Load()
    {
        var file = new ConfigFile();

        if (file.Load(Path) != Error.Ok)
            return;

        Current = Snap((float)file.GetValue(Section, Key, Default));
    }

    /// <summary>
    /// Задать множитель. Значение притягивается к ближайшей ступени: набор ступеней есть
    /// правило, и хранить величину, которой ползунок показать не сможет, незачем.
    /// </summary>
    public static void Set(float value)
    {
        float snapped = Snap(value);

        if (Mathf.IsEqualApprox(snapped, Current))
            return;

        Current = snapped;
        Save();
        Changed?.Invoke();
    }

    public static void Reset() => Set(Default);

    /// <summary>Ближайшая допустимая ступень.</summary>
    public static float Snap(float value)
    {
        float best = Steps[0];

        foreach (float step in Steps)
            if (Mathf.Abs(step - value) < Mathf.Abs(best - value))
                best = step;

        return best;
    }

    /// <summary>Номер ступени, ближайшей к значению. Ползунок ходит именно по номерам.</summary>
    public static int IndexOf(float value)
    {
        float snapped = Snap(value);

        for (int i = 0; i < Steps.Length; i++)
            if (Mathf.IsEqualApprox(Steps[i], snapped))
                return i;

        return 0;
    }

    /// <summary>
    /// Наибольшая ступень, при которой окно указанного размера ещё вмещает разметку.
    /// Нужна потому, что множитель хранится один на все окна: выбранный на большом мониторе,
    /// он не должен разламывать интерфейс после перехода в маленькое окно.
    /// Первая ступень возвращается всегда — интерфейс без масштаба лучше, чем без интерфейса.
    /// </summary>
    public static float MaxFor(Vector2 viewport)
    {
        float allowed = Steps[0];

        foreach (float step in Steps)
            if (viewport.X / step >= MinLogicalSize.X && viewport.Y / step >= MinLogicalSize.Y)
                allowed = Mathf.Max(allowed, step);

        return allowed;
    }

    /// <summary>Множитель, применяемый к слою при данном размере окна.</summary>
    public static float Effective(Vector2 viewport) => Mathf.Min(Current, MaxFor(viewport));

    /// <summary>
    /// Согласовать растеризацию шрифтов с масштабом слоёв интерфейса.
    ///
    /// ЗАЧЕМ ЭТО НУЖНО. Масштаб слоя — преобразование уже нарисованного: глифы растеризованы
    /// под свой кегль, и увеличение слоя растягивает готовые изображения букв, отчего текст
    /// расплывается. Движок умеет растеризовать шрифты крупнее нужного и выводить их
    /// уменьшением (переоверсэмплирование), но коэффициент выводит сам — из масштаба
    /// содержимого окна, а тот здесь равен единице, поскольку масштабируются слои, а не окно.
    /// Поэтому коэффициент задаётся явно и равен масштабу слоя: глифы растеризуются ровно
    /// в том размере, в каком будут показаны, и растягивать нечего.
    ///
    /// Ноль означает возврат к автоматическому выводу, и при единичном масштабе ставится
    /// именно он: своё значение движка не хуже, а лишнее переопределение пришлось бы
    /// поддерживать наравне с ним.
    /// </summary>
    public static void ApplyOversampling(Viewport viewport, float scale)
    {
        if (viewport == null)
            return;

        viewport.OversamplingOverride = Mathf.IsEqualApprox(scale, 1f) ? 0f : scale;
    }

    /// <summary>Подпись ступени для настроек: доля в процентах, всегда целая.</summary>
    public static string Label(float value) => $"{Mathf.RoundToInt(value * 100f)} %";

    /// <summary>
    /// Записать отличие от умолчания. Значение по умолчанию файл не занимает: иначе смена
    /// умолчания в коде не дошла бы до тех, кто однажды открывал настройки, — правило то же,
    /// что и у привязок клавиш.
    /// </summary>
    private static void Save()
    {
        if (Mathf.IsEqualApprox(Current, Default))
        {
            if (FileAccess.FileExists(Path))
                DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(Path));

            return;
        }

        var file = new ConfigFile();
        file.SetValue(Section, Key, Current);
        file.Save(Path);
    }
}
