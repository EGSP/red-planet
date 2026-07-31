using System.Collections.Generic;

/// <summary>Род сущности с точки зрения выделения. Всё, что не боты, — постройки.</summary>
public enum SelectionGroup
{
    /// <summary>Всё ходячее: коммандер, фабрикаторы, копатели.</summary>
    Bots = 0,

    /// <summary>Постройки и каркасы.</summary>
    Structures = 1,
}

/// <summary>
/// Правило рамки: в выделение попадает только преобладающий род, а не всё подряд.
///
/// ЗАЧЕМ. Рамка почти всегда захватывает лишнее. Тянешь её по отряду, стоящему у базы, —
/// и вместе с ботами выделяются генераторы, после чего приказ «идти» уходит в пустоту,
/// а панель строительства складывается из двух разных. Игрок при этом хотел одного:
/// выделить отряд.
///
/// КАК. У каждого рода свой вес, и побеждает род с наибольшим произведением количества
/// на вес. Веса подобраны так, чтобы при равном числе побеждали боты: две постройки
/// перевешивают одного бота, но не двух.
///
/// Правило намеренно применяется ТОЛЬКО к рамке. Одиночный щелчок — это прямое указание
/// на сущность, и спорить с ним нельзя: щёлкнув по генератору, игрок выделит генератор,
/// сколько бы ботов вокруг ни стояло.
/// </summary>
public static class SelectionGroups
{
    /// <summary>
    /// Вес рода. Бот весит вдвое против постройки: постройки статичны, приказов у них мало,
    /// и при сомнении игрок почти всегда имеет в виду отряд.
    /// </summary>
    public static float Weight(this SelectionGroup group) => group switch
    {
        SelectionGroup.Bots => 1f,
        SelectionGroup.Structures => 0.5f,
        _ => 1f,
    };

    /// <summary>
    /// Оставить в списке только преобладающий род. Список правится на месте — он и так
    /// временный, собранный рамкой за этот щелчок.
    /// </summary>
    public static void KeepDominant(List<IOrderable> actors)
    {
        if (actors.Count < 2)
            return;

        var counts = new Dictionary<SelectionGroup, int>();

        foreach (var actor in actors)
        {
            counts.TryGetValue(actor.SelectionGroup, out int count);
            counts[actor.SelectionGroup] = count + 1;
        }

        if (counts.Count < 2)
            return;

        var winner = SelectionGroup.Bots;
        float best = -1f;

        foreach (var (group, count) in counts)
        {
            float score = count * group.Weight();

            // При равном счёте берёт верх более тяжёлый род: один бот против двух построек
            // даёт ничью, и разумнее оставить бота
            if (score < best || (score == best && group.Weight() <= winner.Weight()))
                continue;

            best = score;
            winner = group;
        }

        actors.RemoveAll(actor => actor.SelectionGroup != winner);
    }

    /// <summary>
    /// Рамка ПОВЕРХ уже выделенного: добираем только родню тому, что игрок держит.
    ///
    /// ЗАЧЕМ. Выделив генератор и потянув рамку по базе, игрок хочет генераторы, а не всё
    /// подряд. Держащий отряд ботов — ботов, а не постройки, мимо которых прошёл рамкой.
    /// Правило преобладания здесь не годится: оно смотрит только на улов и о намерении,
    /// уже высказанном первым щелчком, ничего не знает.
    ///
    /// ЛЕСТНИЦА ИЗ ТРЁХ СТУПЕНЕЙ, от точного к общему:
    /// 1. В выделении один тип — добираем буквально этот тип.
    /// 2. Разные типы одного рода — добираем весь род.
    /// 3. Разные роды — добираем эти роды, ничего больше не сужая.
    ///
    /// Тип опознаём по DisplayName — по той же строке, которой подписана панель выделения.
    /// Отдельного ключа заводить не стали: DefId у юнита намеренно пуст, он служит учёту,
    /// а совпадение показанного и отфильтрованного здесь как раз желательно.
    ///
    /// ЦЕНА ПРАВИЛА. Первая ступень теряется от одного случайного попадания: зацепив к трём
    /// генераторам турель, игрок переходит на вторую ступень, и дальше рамка гребёт все
    /// постройки. Возврат один — рамка без Shift, начинающая выделение заново. Это принято
    /// сознательно: обратная крайность, фильтр строго по набранным типам, не давала бы
    /// добрать рамкой ни одного нового типа вовсе.
    /// </summary>
    public static void KeepAkin(List<IOrderable> caught, IReadOnlyList<IOrderable> current)
    {
        if (caught.Count == 0 || current.Count == 0)
            return;

        var groups = new HashSet<SelectionGroup>();
        var names = new HashSet<string>();

        foreach (var actor in current)
        {
            groups.Add(actor.SelectionGroup);
            names.Add(actor.DisplayName);
        }

        // Ступень 3: родов несколько — сужаем до них и на этом останавливаемся
        if (groups.Count > 1)
        {
            caught.RemoveAll(actor => !groups.Contains(actor.SelectionGroup));
            return;
        }

        // Ступень 1: тип ровно один — добираем его же
        if (names.Count == 1)
        {
            caught.RemoveAll(actor => !names.Contains(actor.DisplayName));
            return;
        }

        // Ступень 2: типы разные, род один
        foreach (var group in groups)
        {
            caught.RemoveAll(actor => actor.SelectionGroup != group);
            return;
        }
    }
}
