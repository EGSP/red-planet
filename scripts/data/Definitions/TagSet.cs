using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// Набор тегов сущности, упакованный в битовую маску. Теги — это категории, которыми
/// определение помечает само себя: «постройка», «подвижный», «строитель», «t2».
///
/// ЗАЧЕМ БИТЫ, А НЕ СПИСОК СТРОК. Проверка принадлежности к категории нужна в отборах
/// («ближайший повреждённый, но только не постройка») и в правилах выделения, то есть
/// в местах, которые исполняются каждый кадр. Пересечение двух масок — одна инструкция,
/// сравнение списков строк — цикл со сравнением строк.
///
/// Ограничение в 64 тега осознанное: в моде на PA, откуда взята сама идея, набор
/// UNITTYPE_* насчитывает около сорока штук на игру втрое большего размера.
/// </summary>
public readonly struct TagSet
{
    private readonly ulong _bits;

    public TagSet(ulong bits) => _bits = bits;

    public static readonly TagSet Empty = new(0UL);

    public bool IsEmpty => _bits == 0UL;

    /// <summary>Есть ли ВСЕ теги набора. Пустой набор есть у кого угодно.</summary>
    public bool Has(TagSet tags) => (_bits & tags._bits) == tags._bits;

    /// <summary>Есть ли ХОТЯ БЫ ОДИН тег набора.</summary>
    public bool HasAny(TagSet tags) => (_bits & tags._bits) != 0UL;

    public static TagSet operator |(TagSet a, TagSet b) => new(a._bits | b._bits);

    public override string ToString() => _bits.ToString("X");
}

/// <summary>
/// Реестр тегов: соответствие имени и бита. Заполняется из resources/content/tags.toml
/// и после загрузки не меняется.
///
/// ПОЧЕМУ РЕЕСТР ОБЪЯВЛЕН ЯВНО, А НЕ СОБИРАЕТСЯ ИЗ ВСТРЕЧЕННЫХ ТЕГОВ. Если тег заводится
/// самим фактом употребления, то опечатка заводит новый тег: написал «structrue» — получил
/// сущность, которая ни под один отбор не подходит, и молча. Явный перечень превращает
/// опечатку в ошибку загрузки с указанием файла.
///
/// Часть тегов известна коду (см. свойства ниже): по ним выводятся род выделения и прочие
/// правила. Их отсутствие в перечне — тоже ошибка, потому что код на них уже опирается.
/// </summary>
public sealed class TagRegistry
{
    /// <summary>Сколько тегов помещается в маску.</summary>
    public const int Capacity = 64;

    private readonly Dictionary<string, TagSet> _byName = new();

    /// <summary>Постройка: занимает клетки, стоит на месте, попадает в свой род выделения.</summary>
    public TagSet Structure { get; private set; }

    /// <summary>Всё, что ходит по миру: боты игрока и противник.</summary>
    public TagSet Mobile { get; private set; }

    /// <summary>Сторона противника. По нему спавн набирает поток.</summary>
    public TagSet Enemy { get; private set; }

    /// <summary>Первый тир.</summary>
    public TagSet T1 { get; private set; }

    /// <summary>Второй тир.</summary>
    public TagSet T2 { get; private set; }

    /// <summary>Титан.</summary>
    public TagSet Titan { get; private set; }

    /// <summary>Любой из тегов тира.</summary>
    public TagSet AnyTier => T1 | T2 | Titan;

    public IReadOnlyCollection<string> Names => _byName.Keys;

    /// <summary>
    /// Завести теги. Порядок в перечне и есть порядок битов, но снаружи это не видно:
    /// сохранений тегами никто не пишет, поэтому перенумерация ничего не ломает.
    /// Возвращает false, если перечень не годится — тогда каталог грузить бессмысленно.
    /// </summary>
    public bool Declare(IReadOnlyList<string> names, string path)
    {
        _byName.Clear();

        if (names.Count > Capacity)
        {
            GD.PushError($"[Теги] объявлено {names.Count} тегов, помещается {Capacity}: {path}");
            return false;
        }

        for (int i = 0; i < names.Count; i++)
        {
            string name = names[i];

            if (string.IsNullOrWhiteSpace(name))
            {
                GD.PushError($"[Теги] пустое имя тега в позиции {i}: {path}");
                return false;
            }

            if (_byName.ContainsKey(name))
            {
                GD.PushError($"[Теги] тег «{name}» объявлен дважды: {path}");
                return false;
            }

            _byName[name] = new TagSet(1UL << i);
        }

        return Bind(path);
    }

    /// <summary>Разобрать список имён в маску. Незнакомое имя — ошибка, набор не собирается.</summary>
    public bool TryParse(IReadOnlyList<string> names, string owner, out TagSet result)
    {
        result = TagSet.Empty;
        bool ok = true;

        foreach (string name in names)
        {
            if (_byName.TryGetValue(name, out var tag))
            {
                result |= tag;
                continue;
            }

            GD.PushError($"[Теги] неизвестный тег «{name}» у {owner}. " +
                         "Допустимые перечислены в resources/content/tags.toml");
            ok = false;
        }

        return ok;
    }

    /// <summary>Имена набора — для сообщений об ошибках и отладочной печати.</summary>
    public string Describe(TagSet tags)
    {
        var text = new StringBuilder();

        foreach (var (name, tag) in _byName)
        {
            if (!tags.Has(tag))
                continue;

            if (text.Length > 0)
                text.Append(", ");

            text.Append(name);
        }

        return text.Length > 0 ? text.ToString() : "нет тегов";
    }

    /// <summary>Связать теги, на которые опирается код. Нет такого тега — нет и правила.</summary>
    private bool Bind(string path)
    {
        bool ok = true;

        Structure = Required("structure", path, ref ok);
        Mobile = Required("mobile", path, ref ok);
        Enemy = Required("enemy", path, ref ok);
        T1 = Required("t1", path, ref ok);
        T2 = Required("t2", path, ref ok);
        Titan = Required("titan", path, ref ok);

        return ok;
    }

    private TagSet Required(string name, string path, ref bool ok)
    {
        if (_byName.TryGetValue(name, out var tag))
            return tag;

        GD.PushError($"[Теги] обязательный тег «{name}» не объявлен, а код на него опирается: {path}");
        ok = false;
        return TagSet.Empty;
    }
}
