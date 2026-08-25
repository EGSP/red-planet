using Godot;

/// <summary>
/// Сверка кэшированного положения сущностей с положением их узлов.
///
/// ЗАЧЕМ. <see cref="Entity"/> держит положение полем и пишет его в узел сам. Порядок этот
/// нарушается ровно двумя способами: записью через ссылку типа <see cref="Node2D"/> в обход
/// класса и движением родительского слоя. Оба дают расхождение, которое в игре читается
/// как дрожание сущности либо как выстрел мимо цели, и оба ищутся долго, потому что видимое
/// следствие отстоит от причины.
///
/// Поэтому расхождение ищется не глазами, а проходом: под признаком отладки, раз в кадр,
/// с сообщением в журнал о первом найденном. Сверка стоит одного обращения к движку
/// на сущность — то есть ровно того, ради устранения чего кэш и заводился, — и потому
/// в обычной работе выключена.
/// </summary>
public static class EntityAudit
{
    /// <summary>Насколько положения вправе разойтись. Округление до float даёт доли пикселя.</summary>
    private const float Tolerance = 0.01f;

    /// <summary>О скольких расхождениях сообщать за проход: список одинаковых строк не нужен.</summary>
    private const int Reported = 3;

    public static void Check(GameManager gm)
    {
        if (gm?.Index == null)
            return;

        int found = 0;

        foreach (var entity in gm.Index.All<Entity>())
        {
            var cached = entity.GlobalPosition;
            var actual = entity.NodeSpot;

            if (cached.DistanceSquaredTo(actual) <= Tolerance * Tolerance)
                continue;

            if (++found > Reported)
                break;

            GD.PushWarning($"[EntityAudit] {entity.Name}: поле {cached}, узел {actual} — " +
                           "положение писали мимо Entity либо двигали родительский слой");

            // Приводим к тому, что видит движок: разошедшееся поле хуже, чем медленное чтение
            entity.Sync();
        }
    }
}
