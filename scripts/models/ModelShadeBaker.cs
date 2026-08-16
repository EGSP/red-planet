using System.Collections.Generic;
using Godot;

/// <summary>
/// Запекание изображений затенения из альфы исходной текстуры.
///
/// ЗАЧЕМ ЭТО НУЖНО. Спрайт лежит на поверхности без разрыва по глубине, поэтому читается
/// как наклейка. Разрыв создают две вещи: тень, отброшенная на грунт, и потемнение у края
/// корпуса, которое возникает от закрытого обзора неба. Второе есть упрощённая имитация
/// ambient occlusion — настоящее вычисление потребовало бы данных о рельефе, которых
/// у плоского изображения нет, поэтому берётся только полоса вдоль силуэта.
///
/// ПОЧЕМУ ВСЁ СЧИТАЕТСЯ ЗАРАНЕЕ, А НЕ ШЕЙДЕРОМ. Обе величины зависят только от альфы файла
/// и не меняются в течение партии. Размытие в шейдере пришлось бы выполнять каждый кадр для
/// каждой сущности, тогда как здесь на весь запуск приходится по одному размытию на текстуру
/// и роль, а в кадре добавляется лишь один узел-спрайт.
///
/// ЦВЕТ И ПЛОТНОСТЬ В ИЗОБРАЖЕНИЕ НЕ ВХОДЯТ. Запекается только альфа, а цвет и плотность
/// назначаются узлу через <c>self_modulate</c>. Отсюда правка цвета и плотности в отладочной
/// панели не требует пересчёта, и один и тот же результат запекания годится всем узлам,
/// сколько бы у них ни было своих значений плотности.
///
/// КЭШ ПО ЭКЗЕМПЛЯРУ ТЕКСТУРЫ, А НЕ ПО ПУТИ. Один рисунок, использованный двумя моделями,
/// приходит из <see cref="ResourceLoader"/> одним и тем же ресурсом, поэтому печётся один
/// раз. Повторный импорт файла в редакторе даёт новый ресурс и, следовательно, новый ключ,
/// отчего правка рисунка видна без ручной очистки.
/// </summary>
public static class ModelShadeBaker
{
    /// <summary>Ключ: экземпляр исходной текстуры, роль слоя и радиус размытия в пикселях.</summary>
    private static readonly Dictionary<(ulong, ModelShadeKind, int), Texture2D> Cache = new();

    /// <summary>
    /// Изображение затенения для этой текстуры. Возвращается белое изображение с посчитанной
    /// альфой; цвет и плотность накладывает вызывающий. Null означает, что печь нечего.
    ///
    /// <paramref name="blurFraction"/> — радиус размытия долей меньшей стороны текстуры.
    /// </summary>
    public static Texture2D Bake(Texture2D source, ModelShadeKind kind, float blurFraction)
    {
        if (source == null)
            return null;

        var image = Readable(source);

        if (image == null)
            return null;

        int width = image.GetWidth();
        int height = image.GetHeight();

        if (width <= 0 || height <= 0)
            return null;

        int side = Mathf.Min(width, height);
        int blur = Mathf.Max(1, Mathf.RoundToInt(side * Mathf.Max(blurFraction, 0f)));

        var key = (source.GetInstanceId(), kind, blur);

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var alpha = ReadAlpha(image, width, height);
        var baked = kind == ModelShadeKind.Rim
            ? BakeRim(alpha, width, height, blur)
            : BakeShadow(alpha, width, height, blur);

        Cache[key] = baked;
        return baked;
    }

    /// <summary>
    /// Насколько изображение тени шире исходной текстуры с каждой стороны, в пикселях.
    /// Величина нужна и при запекании, и узлу: размытая тень выходит за габарит рисунка,
    /// и запас поля обязан вместить это расширение, иначе тень упрётся в край изображения
    /// и оборвётся прямой линией.
    /// </summary>
    private static int Padding(int blur) => blur * 2 + 1;

    /// <summary>
    /// Тень: размытый силуэт с запасом поля по краям. Смещение к изображению не примешивается —
    /// его задаёт положение узла, поскольку направление света мировое, а узел повёрнут вместе
    /// с корпусом.
    /// </summary>
    private static Texture2D BakeShadow(float[] alpha, int width, int height, int blur)
    {
        int padding = Padding(blur);
        int paddedWidth = width + padding * 2;
        int paddedHeight = height + padding * 2;

        var field = new float[paddedWidth * paddedHeight];

        for (int y = 0; y < height; y++)
        {
            int row = (y + padding) * paddedWidth + padding;

            for (int x = 0; x < width; x++)
                field[row + x] = alpha[y * width + x];
        }

        // Два прохода прямоугольным ядром дают спад, близкий к гауссову, и заметно дешевле
        // настоящего гауссова ядра того же радиуса
        Blur(field, paddedWidth, paddedHeight, blur);
        Blur(field, paddedWidth, paddedHeight, blur);

        return Compose(field, paddedWidth, paddedHeight);
    }

    /// <summary>
    /// Затемнение по внутренней кайме. Совпадает с корпусом по размеру и рисуется поверх него,
    /// поэтому запаса по краям не имеет.
    /// </summary>
    private static Texture2D BakeRim(float[] alpha, int width, int height, int blur)
    {
        var opened = (float[])alpha.Clone();
        Blur(opened, width, height, blur);

        // Размытая альфа равна единице только там, где вся окрестность лежит внутри силуэта,
        // и убывает по мере приближения к краю. Разность с исходной альфой и есть полоса
        // вдоль края: снаружи её гасит множитель alpha, в глубине — единичное значение
        // размытой альфы. На самом краю разность близка к половине, поэтому множитель удвоен —
        // так плотность в настройках задаёт непрозрачность именно у края
        var field = new float[alpha.Length];

        for (int i = 0; i < field.Length; i++)
            field[i] = Mathf.Clamp(alpha[i] * (1f - opened[i]) * 2f, 0f, 1f);

        return Compose(field, width, height);
    }

    /// <summary>Доступное процессору изображение текстуры, распакованное и в Rgba8.</summary>
    private static Image Readable(Texture2D source)
    {
        var image = source.GetImage();

        if (image == null || image.IsEmpty())
            return null;

        if (image.IsCompressed() && image.Decompress() != Error.Ok)
        {
            GD.PushWarning($"[ModelShadeBaker] текстура не распакована: {source.ResourcePath}");
            return null;
        }

        if (image.GetFormat() != Image.Format.Rgba8)
            image.Convert(Image.Format.Rgba8);

        return image;
    }

    private static float[] ReadAlpha(Image image, int width, int height)
    {
        var alpha = new float[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                alpha[y * width + x] = image.GetPixel(x, y).A;
        }

        return alpha;
    }

    /// <summary>Собрать текстуру: цвет белый на всё изображение, посчитанное поле идёт в альфу.</summary>
    private static Texture2D Compose(float[] field, int width, int height)
    {
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        var color = Colors.White;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                color.A = field[y * width + x];
                image.SetPixel(x, y, color);
            }
        }

        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// Размытие прямоугольным ядром в два прохода — по строкам и по столбцам. За пределами
    /// изображения значение считается нулевым, а не повторяющим край: край плотно набитого
    /// рисунка непрозрачен, и повтор оставил бы силуэт без затемнения там, где корпус вплотную
    /// подходит к границе файла.
    /// </summary>
    private static void Blur(float[] field, int width, int height, int radius)
    {
        var pass = new float[field.Length];
        float norm = 1f / (radius * 2 + 1);

        for (int y = 0; y < height; y++)
        {
            int row = y * width;

            for (int x = 0; x < width; x++)
            {
                float sum = 0f;

                for (int k = -radius; k <= radius; k++)
                {
                    int sample = x + k;

                    if (sample >= 0 && sample < width)
                        sum += field[row + sample];
                }

                pass[row + x] = sum * norm;
            }
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float sum = 0f;

                for (int k = -radius; k <= radius; k++)
                {
                    int sample = y + k;

                    if (sample >= 0 && sample < height)
                        sum += pass[sample * width + x];
                }

                field[y * width + x] = sum * norm;
            }
        }
    }
}
