using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;

/// <summary>
/// Параметры экземпляра шейдера: величины, принадлежащие отдельной сущности, но передаваемые
/// без создания собственного вещества для неё.
///
/// ЗАЧЕМ ЭТО ЗАВЕДЕНО ОТДЕЛЬНЫМ ИНСТРУМЕНТОМ. Прежде цвет команды и доля прочности писались
/// в вещество, а чтобы запись не окрасила разом всех носителей вида, каждой машине выдавалась
/// копия <see cref="ShaderMaterial"/>. Godot объединяет двумерные картинки в один вызов
/// отрисовки лишь при совпадении вещества, поэтому копии отменяли объединение полностью:
/// при трёх сотнях машин выходило свыше шестисот вызовов. Параметр экземпляра хранится
/// у самого объекта отрисовки, а вещество остаётся одно на вид.
///
/// ПРАВИЛО ВЫБОРА. Величина принадлежит виду — она живёт обычным <c>uniform</c> в веществе
/// сцены. Величина принадлежит экземпляру, а экземпляров в кадре десятки и более — она
/// объявляется в шейдере как <c>instance uniform</c> и пишется отсюда. Копия вещества
/// на экземпляр не заводится никогда.
///
/// ОГРАНИЧЕНИЯ ПАРАМЕТРОВ ЭКЗЕМПЛЯРА. Скалярные и векторные типы, но не текстуры;
/// число объявлений на шейдер невелико (порядок полутора десятков). Отсюда следует, что
/// текстура внутренностей и карта порядка вскрытия остаются обычными uniform, — они и так
/// принадлежат виду.
///
/// ОБЪЯВЛЕНИЯ НЕ ВИДНЫ ЧЕРЕЗ <c>Shader.GetShaderUniformList</c>: перечень возвращает только
/// обычные uniform. Поэтому наличие объявления определяется разбором исходного текста шейдера,
/// однократным на шейдер — см. <see cref="Names"/>.
/// </summary>
public static class InstanceParam
{
    /// <summary>Цвет команды-владельца. Объявлен в <c>unit_body.gdshader</c>.</summary>
    public static readonly StringName TeamColor = "team_color";

    /// <summary>Доля прочности: 1 — целая сущность, 0 — вскрытая целиком.</summary>
    public static readonly StringName Health = "health";

    /// <summary>Записать величину объекту отрисовки, созданному через RenderingServer.</summary>
    public static void Write(Rid item, StringName parameter, Variant value) =>
        RenderingServer.CanvasItemSetInstanceShaderParameter(item, parameter, value);

    /// <summary>Записать величину узлу, пережившему запекание модели.</summary>
    public static void Write(CanvasItem item, StringName parameter, Variant value) =>
        item.SetInstanceShaderParameter(parameter, value);

    /// <summary>
    /// Записать ЦВЕТ. Отдельно от прочих величин из-за пространства цвета.
    ///
    /// ЧТО ИЗМЕРЕНО. Цвет, записанный в вещество, доходит до экрана без изменений: движок
    /// переводит его из sRGB в линейное пространство при записи, а вывод переводит обратно.
    /// Записанный параметром экземпляра, тот же цвет выходит заметно темнее — перевод при
    /// записи не делается, и обратный перевод при выводе оказывается лишним. Проверено
    /// на стенде: 0,42 через вещество даёт на экране 0,42, а через параметр экземпляра — 0,149,
    /// то есть ровно 0,42 в степени 2,2.
    ///
    /// Отсюда поправка: цвет переводится в sRGB перед записью, и оба пути дают одно и то же.
    /// </summary>
    public static void WriteColor(Rid item, StringName parameter, Color color) =>
        Write(item, parameter, color.LinearToSrgb());

    /// <summary>
    /// Объявляет ли шейдер вещества такой параметр экземпляра. Ложь для вещества без шейдера
    /// и для встроенного <see cref="CanvasItemMaterial"/>: запись им ничего не даст.
    /// </summary>
    public static bool Declares(Material material, StringName parameter) =>
        material is ShaderMaterial { Shader: { } shader }
        && System.Array.IndexOf(Names(shader), (string)parameter) >= 0;

    /// <summary>
    /// Имена параметров экземпляра, объявленных шейдером. Разбор идёт один раз на шейдер:
    /// текст после загрузки не меняется, а шейдеров в игре порядка десятка.
    ///
    /// Ключ — идентификатор объекта, а не сам шейдер: держать ссылку значило бы не давать
    /// выгрузить ресурс, который больше никому не нужен.
    /// </summary>
    private static string[] Names(Shader shader)
    {
        ulong id = shader.GetInstanceId();

        if (Cache.TryGetValue(id, out var names))
            return names;

        var found = new List<string>();

        foreach (Match match in Declaration.Matches(shader.Code ?? ""))
            found.Add(match.Groups[1].Value);

        names = found.ToArray();
        Cache[id] = names;

        return names;
    }

    private static readonly Dictionary<ulong, string[]> Cache = new();

    /// <summary>
    /// Объявление параметра экземпляра: слово <c>instance</c>, слово <c>uniform</c>, тип
    /// и имя. Дальше строки идут подсказки и значение по умолчанию, и они не разбираются.
    /// </summary>
    private static readonly Regex Declaration = new(
        @"^[ \t]*instance[ \t]+uniform[ \t]+\w+[ \t]+(\w+)",
        RegexOptions.Multiline | RegexOptions.Compiled);
}
