using System;
using System.Collections.Generic;
using Godot;
using Tomlyn;
using Tomlyn.Model;

/// <summary>
/// Разбор .toml с учётом того, что ключи израсходованы.
///
/// ЗАЧЕМ СЛЕДИТЬ ЗА ИЗРАСХОДОВАННЫМИ КЛЮЧАМИ. Обычное чтение по ключу с запасным значением
/// молча прощает опечатку: написал в файле max_helth — получил сущность с прочностью
/// по умолчанию и ищешь потом, отчего она гибнет с двух выстрелов. Здесь каждое обращение
/// помечает ключ израсходованным, а Done сообщает обо всех, до которых никто не добрался.
/// Опечатка становится ошибкой загрузки с именем файла и секции.
///
/// Разбор ручной, а не отображением на классы: файлы пишет либо человек, либо агент,
/// и внятное сообщение о том, что именно не так и где, важнее краткости этого кода.
///
/// Ошибки не бросаются исключением, а считаются: одна опечатка не должна прятать остальные.
/// Итог смотрят по Errors, и решение «грузить или нет» принимает вызывающий.
/// </summary>
public sealed class TomlDocument
{
    private readonly TomlTable _table;
    private readonly HashSet<string> _used = new();
    private readonly List<TomlDocument> _children = new();

    /// <summary>Путь к файлу и место внутри него — всё, что нужно для сообщения об ошибке.</summary>
    public string Where { get; }

    /// <summary>Сколько ошибок нашлось в этом документе и во всех его секциях.</summary>
    public int Errors { get; private set; }

    public bool Failed => Errors > 0;

    private TomlDocument(TomlTable table, string where)
    {
        _table = table;
        Where = where;
    }

    /// <summary>Прочитать файл. Возвращает null, если файл не открылся или не разобрался.</summary>
    public static TomlDocument Load(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);

        if (file == null)
        {
            GD.PushError($"[Контент] файл не открывается: {path}");
            return null;
        }

        var syntax = Toml.Parse(file.GetAsText(), path);

        if (!syntax.HasErrors)
            return new TomlDocument(syntax.ToModel(), path);

        foreach (var error in syntax.Diagnostics)
            GD.PushError($"[Контент] {error}");

        return null;
    }

    public bool Has(string key) => _table.ContainsKey(key);

    /// <summary>Строка. Нет ключа — запасное значение.</summary>
    public string String(string key, string fallback = "")
    {
        if (!Take(key, out object value))
            return fallback;

        if (value is string text)
            return text;

        return Fail<string>(key, "строка") ?? fallback;
    }

    /// <summary>Строка, без которой определение бессмысленно.</summary>
    public string RequiredString(string key)
    {
        if (!_table.ContainsKey(key))
        {
            Error($"не задан обязательный ключ «{key}»");
            return "";
        }

        string text = String(key);

        if (string.IsNullOrWhiteSpace(text))
            Error($"пустое значение обязательного ключа «{key}»");

        return text;
    }

    /// <summary>Число. TOML различает целые и дробные, определению эта разница безразлична.</summary>
    public float Float(string key, float fallback = 0f)
    {
        if (!Take(key, out object value))
            return fallback;

        return value switch
        {
            double number => (float)number,
            long number => number,
            _ => Fail(key, "число", fallback),
        };
    }

    /// <summary>
    /// Число либо именованная ступень: pattern_step = "narrow" и pattern_step = 2.5 значат
    /// одно и то же поле. Имена ступеней передаёт вызывающий, потому что принадлежат они
    /// смыслу поля, а не разбору файла.
    ///
    /// ЗАЧЕМ ДВА СПОСОБА ЗАПИСИ. Обиходных значений у такого поля два-три, и называть их
    /// словом понятнее, чем числом; но всё промежуточное словом не выразить, а заводить
    /// ради него второй ключ значит спрашивать, какой из двух главнее.
    /// </summary>
    public float Scale(string key, float fallback, params (string Name, float Value)[] steps)
    {
        if (!Take(key, out object value))
            return fallback;

        switch (value)
        {
            case double number:
                return (float)number;

            case long number:
                return number;

            case string text:
                foreach (var (name, number) in steps)
                    if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
                        return number;

                Error($"ключ «{key}»: неизвестная ступень «{text}». " +
                      $"Допустимые: {string.Join(", ", Names(steps))} либо число");

                return fallback;

            default:
                return Fail(key, "число либо имя ступени строкой", fallback);
        }
    }

    private static IEnumerable<string> Names((string Name, float Value)[] steps)
    {
        foreach (var (name, _) in steps)
            yield return name;
    }

    public int Int(string key, int fallback = 0)
    {
        if (!Take(key, out object value))
            return fallback;

        return value switch
        {
            long number => (int)number,
            double number => (int)number,
            _ => Fail(key, "целое число", fallback),
        };
    }

    public bool Bool(string key, bool fallback = false)
    {
        if (!Take(key, out object value))
            return fallback;

        return value is bool flag ? flag : Fail(key, "true или false", fallback);
    }

    /// <summary>Список строк. Нет ключа — пустой список, это не ошибка.</summary>
    public string[] Strings(string key)
    {
        if (!Take(key, out object value))
            return Array.Empty<string>();

        if (value is not TomlArray array)
            return Fail(key, "список строк", Array.Empty<string>());

        var result = new string[array.Count];

        for (int i = 0; i < array.Count; i++)
        {
            if (array[i] is string text)
            {
                result[i] = text;
                continue;
            }

            Error($"в списке «{key}» элемент {i} не строка");
            result[i] = "";
        }

        return result;
    }

    /// <summary>
    /// Цвет списком из трёх или четырёх долей: [0.4, 0.9, 0.5]. Именно долями, а не байтами —
    /// так же, как цвет задаётся в коде движка, и переводить в уме ничего не приходится.
    /// </summary>
    public Color Color(string key, Color fallback)
    {
        if (!Take(key, out object value))
            return fallback;

        if (value is not TomlArray array || array.Count is < 3 or > 4)
            return Fail(key, "список из трёх или четырёх долей цвета", fallback);

        var channels = new float[4] { 0f, 0f, 0f, 1f };

        for (int i = 0; i < array.Count; i++)
            channels[i] = array[i] switch
            {
                double number => (float)number,
                long number => number,
                _ => Fail($"{key}[{i}]", "доля цвета числом", 0f),
            };

        return new Color(channels[0], channels[1], channels[2], channels[3]);
    }

    /// <summary>Значение перечисления по имени, без учёта регистра.</summary>
    public T Enum<T>(string key, T fallback) where T : struct, Enum
    {
        if (!Take(key, out object value))
            return fallback;

        if (value is not string text)
            return Fail(key, "имя из перечисления строкой", fallback);

        // Подчёркивания снимаются: в файлах принята запись metal_area, а в перечислении
        // то же значение называется MetalArea, и заставлять файл повторять регистр кода
        // значило бы вносить в него чужое соглашение об именовании
        if (System.Enum.TryParse<T>(text.Replace("_", ""), ignoreCase: true, out var parsed))
            return parsed;

        Error($"ключ «{key}»: неизвестное значение «{text}». " +
              $"Допустимые: {string.Join(", ", System.Enum.GetNames<T>()).ToLowerInvariant()}");

        return fallback;
    }

    /// <summary>Вложенная секция. Нет секции — null, и это не ошибка: секции необязательны.</summary>
    public TomlDocument Section(string key)
    {
        if (!Take(key, out object value))
            return null;

        if (value is not TomlTable table)
        {
            Error($"«{key}» должен быть секцией [{key}]");
            return null;
        }

        var child = new TomlDocument(table, $"{Where} → [{key}]");
        _children.Add(child);
        return child;
    }

    /// <summary>Список секций: и [[key]], и key = [{ ... }] читаются одинаково.</summary>
    public List<TomlDocument> Sections(string key)
    {
        var result = new List<TomlDocument>();

        if (!Take(key, out object value))
            return result;

        switch (value)
        {
            case TomlTableArray tables:
                foreach (var table in tables)
                    result.Add(Child(table, key, result.Count));
                return result;

            case TomlArray array:
                for (int i = 0; i < array.Count; i++)
                {
                    if (array[i] is TomlTable table)
                    {
                        result.Add(Child(table, key, i));
                        continue;
                    }

                    Error($"в списке «{key}» элемент {i} не таблица");
                }

                return result;

            default:
                Error($"«{key}» должен быть списком секций [[{key}]]");
                return result;
        }
    }

    /// <summary>
    /// Закончить разбор: пожаловаться на всё, к чему никто не обратился. Вызывать после
    /// того, как прочитаны все нужные ключи, — иначе живые ключи сойдут за лишние.
    /// </summary>
    public void Done()
    {
        foreach (var child in _children)
        {
            child.Done();
            Errors += child.Errors;
        }

        foreach (string key in _table.Keys)
        {
            if (_used.Contains(key))
                continue;

            Error($"неизвестный ключ «{key}». Проверьте написание");
        }
    }

    /// <summary>Пожаловаться от имени этого документа. Считается в общий итог.</summary>
    public void Error(string message)
    {
        Errors++;
        GD.PushError($"[Контент] {Where}: {message}");
    }

    private TomlDocument Child(TomlTable table, string key, int index)
    {
        var child = new TomlDocument(table, $"{Where} → [[{key}]] #{index}");
        _children.Add(child);
        return child;
    }

    private bool Take(string key, out object value)
    {
        _used.Add(key);
        return _table.TryGetValue(key, out value);
    }

    private T Fail<T>(string key, string expected, T fallback = default)
    {
        Error($"ключ «{key}» должен быть: {expected}");
        return fallback;
    }
}
