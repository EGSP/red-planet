using System.Collections.Generic;
using Godot;

/// <summary>
/// Хранилище общих полей частиц: заводит поле при первом обращении к признаку и вбрасывает
/// в него части запечённых эффектов.
///
/// ПОЛЕ ЗАВОДИТСЯ ЛЕНИВО. Партия может пройти без единого выстрела фугасным оружием либо
/// без потерь вовсе, и держать наготове поля под всё объявленное значило бы платить
/// за то, чего не случилось.
///
/// ПОЛЯ НЕ ОСВОБОЖДАЮТСЯ ПО ХОДУ ПАРТИИ. Их число ограничено сверху числом различных
/// материалов частиц, помноженным на число различных множителей размера, то есть двумя
/// десятками, а не числом событий. Освобождать поле, чтобы завести его тем же кадром
/// заново, значило бы менять постоянный расход на переменный без выигрыша.
/// </summary>
public sealed class ParticleYard
{
    private readonly Dictionary<ParticleKey, ParticleField> _fields = new();
    private readonly GameManager _game;
    private readonly int _capacity;

    /// <summary>
    /// Ссылка на управляющий узел нужна ради слоёв мира: поле ставится на тот слой,
    /// которому принадлежит его род эффекта, — пыль под юнитами, вспышки над ними.
    /// </summary>
    public ParticleYard(GameManager game, int capacity)
    {
        _game = game;
        _capacity = Mathf.Max(capacity, 16);
    }

    /// <summary>Сколько полей заведено. Читается отладочной панелью.</summary>
    public int Count => _fields.Count;

    /// <summary>
    /// Проиграть одноразовый эффект целиком: каждая часть выпускает весь свой запас разом,
    /// как это делала взрывная выдача прежних узлов.
    /// </summary>
    public void Play(EffectBake bake, Vector2 spot, float facing, float size)
    {
        if (bake == null)
            return;

        foreach (var part in bake.Parts)
            Cast(part, spot, facing, size, part.Count);
    }

    /// <summary>
    /// Продвинуть непрерывный поток одной части.
    ///
    /// ДОЛГ НУЖЕН ПОТОМУ, ЧТО ЗА КАДР ЧАСТИЦ ВЫХОДИТ МЕНЬШЕ ОДНОЙ. Пыль даёт около шести
    /// частиц в секунду, то есть примерно одну десятую за кадр; округление такой доли
    /// до целого дало бы либо нулевой поток, либо десятикратный. Дробный остаток копится
    /// между кадрами и обращается в частицу, когда наберётся целая.
    /// </summary>
    public void Stream(EmissionProfile part, Vector2 spot, float facing, float size,
        float density, double dt, ref float debt)
    {
        if (part == null || density <= 0f)
            return;

        debt += part.Rate * density * (float)dt;

        int count = Mathf.FloorToInt(debt);

        if (count <= 0)
            return;

        debt -= count;
        Cast(part, spot, facing, size, count);
    }

    /// <summary>Освободить все поля. Зовётся при уходе системы из мира.</summary>
    public void Clear()
    {
        foreach (var field in _fields.Values)
            if (Alive.Is(field))
                field.QueueFree();

        _fields.Clear();
    }

    private void Cast(EmissionProfile part, Vector2 spot, float facing, float size, int count)
    {
        var field = Field(part.Key.Resized(size));

        // Множитель размера учтён материалом поля, поэтому вбросу остаётся растить
        // только разлёт и смещение части — см. ParticleField.Cast
        field?.Cast(part, spot, facing, size, count);
    }

    private ParticleField Field(in ParticleKey key)
    {
        // Поле, освобождённое помимо хранилища, заводится заново: связка с признаком
        // переживает узел, и оставить её указывающей на освобождённый узел значило бы
        // потерять весь этот род частиц до конца партии
        if (_fields.TryGetValue(key, out var known) && Alive.Is(known))
            return known;

        var field = ParticleField.Make(key, _capacity);
        _game.Playground.Add(key.Layer, field);
        _fields[key] = field;

        return field;
    }
}
