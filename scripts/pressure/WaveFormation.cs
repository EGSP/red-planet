using Godot;

/// <summary>
/// Геометрия наступления: где именно встанет юнит с этим порядковым номером внутри очага.
///
/// ПОЧЕМУ ЭТО ОТДЕЛЬНО ОТ <see cref="WaveSystem"/>. Форму нужно уметь показать в редакторе,
/// где ни мира, ни менеджера не существует. Оставь подсчёт внутри системы, предпросмотр
/// рисовал бы похожее построение, а не то же самое, — и подбирать по нему числа было бы
/// нельзя. Здесь чистая функция от формы и номера, ничего кроме.
/// </summary>
public static class WaveFormation
{
    /// <summary>Сколько рядов помещается в заданную глубину. Не меньше одного.</summary>
    public static int Rows(WaveShape shape) =>
        Mathf.Max(Mathf.FloorToInt(shape.DepthPx / shape.SpacingPx) + 1, 1);

    /// <summary>Направление очага с этим номером.</summary>
    public static float GroupAngle(WaveShape shape, float center, int group) =>
        center + group * Mathf.DegToRad(shape.GroupsArcDegrees);

    /// <summary>
    /// Место с порядковым номером внутри очага и номер ряда, в который оно попало.
    ///
    /// Ряды идут от ближнего к базе наружу с шагом в промежуток, мест в ряду столько,
    /// сколько их помещается по длине дуги на этой глубине. Когда состав не умещается
    /// в заданную глубину, ряды продолжаются за дальнюю дугу тем же шагом: заявленная
    /// плотность важнее заявленной глубины, поскольку слишком тесная расстановка приводит
    /// к расталкиванию в первые же секунды, тогда как лишний ряд позади просто отстаёт.
    /// Ширина у таких рядов остаётся как у дальней дуги — экстраполировать угол нельзя,
    /// иначе форма расходится тем сильнее, чем больше волна.
    /// </summary>
    public static (Vector2 Position, int Row) Slot(WaveShape shape, float center, int index)
    {
        int rows = Rows(shape);
        float step = shape.SpacingPx;
        int row = 0;

        while (true)
        {
            float t = rows > 1 ? Mathf.Min(row / (float)(rows - 1), 1f) : Mathf.Min(row, 1f);
            float radius = shape.NearRadiusPx + row * step;
            int places = Mathf.Max(Mathf.FloorToInt(shape.ArcAt(t) * radius / step) + 1, 1);

            if (index < places)
            {
                float offset = (index - (places - 1) * 0.5f) * step;
                float angle = center + offset / radius;

                return (Heading.Forward(angle) * radius, row);
            }

            index -= places;
            row++;
        }
    }
}
