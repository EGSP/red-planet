using System.Collections.Generic;
using Godot;

/// <summary>
/// Запечённое затенение спрайта: контактная тень под корпусом и затемнение по внутренней
/// кайме. Оба изображения выводятся из альфы самого спрайта и считаются один раз при первом
/// обращении, после чего лежат в кэше по пути к файлу.
///
/// ЗАЧЕМ ЭТО НУЖНО. Спрайт постройки лежит на поверхности без разрыва по глубине, поэтому
/// читается как наклейка. Разрыв создают две вещи: тень, отброшенная на грунт, и потемнение
/// у края корпуса, которое возникает от закрытого обзора неба. Второе есть упрощённая
/// имитация ambient occlusion — настоящее вычисление потребовало бы данных о рельефе,
/// которых у плоского изображения нет, поэтому берётся только кайма вдоль силуэта.
///
/// ПОЧЕМУ ВСЁ СЧИТАЕТСЯ ЗАРАНЕЕ, А НЕ ШЕЙДЕРОМ. Обе величины зависят только от альфы файла
/// и не меняются в течение партии. Размытие в шейдере пришлось бы выполнять каждый кадр для
/// каждой постройки, тогда как здесь на весь запуск приходится по два размытия на вид
/// постройки, а в кадре добавляются лишь два вызова отрисовки текстуры.
///
/// ПОЧЕМУ РАЗМЕР БЕРЁТСЯ ОТ СПРАЙТА. Радиус размытия и смещение тени заданы долями меньшей
/// стороны изображения, а не числом пикселей: спрайты имеют разное разрешение (64 и 128
/// пикселей у нынешних построек), и постоянная в пикселях дала бы у крупного изображения
/// вдвое более узкую тень.
///
/// ПОЧЕМУ ИЗОБРАЖЕНИЕ ЗАПЕКАЕТСЯ КРУПНЕЕ СПРАЙТА. Слои рисуются тем же узлом, что и корпус,
/// а фильтр текстур есть свойство узла и действует на все его команды кадра сразу. Корпусу
/// нужен ближайший сосед ради резкости пиксельного рисунка, и размытая тень при этом фильтре
/// распалась бы на ступени. Вынести слои на собственный узел с линейным фильтром нельзя:
/// дочерний узел рисуется после родителя целиком, а контактная тень обязана лечь между
/// площадкой и корпусом, то есть в середину его отрисовки; отрицательный ZIndex же выводит
/// узел из порядка WorldLayer и прячет под саму местность. Поэтому гладкость достигается
/// разрешением: слой запекается подробнее спрайта в <see cref="SpriteOcclusionSettings.Upscale"/>
/// раз, и ступень ближайшего соседа приходится на долю пикселя корпуса.
///
/// НАСТРОЙКИ ЛЕЖАТ В РЕСУРСЕ <see cref="SpriteOcclusionSettings"/>, а не в постоянных этого
/// класса: цвет и плотность затенения есть предмет правки на глаз, а не устройство приёма.
/// </summary>
public static class SpriteOcclusion
{
    /// <summary>
    /// Готовый слой затенения. Поле <see cref="Offset"/> задаёт отход изображения от центра
    /// корпуса в пикселях исходного спрайта и в мировых осях, а не в осях корпуса: свет
    /// падает на всю карту одинаково, и у повёрнутой постройки тень обязана уходить в ту же
    /// сторону, что и у неповёрнутой. Поэтому смещение не запекается в изображение, а
    /// применяется при отрисовке с поправкой на угол корпуса.
    /// </summary>
    public readonly record struct Layer(Texture2D Texture, Vector2 Offset)
    {
        public bool IsEmpty => Texture == null;
    }

    private static readonly Dictionary<string, Layer> ContactCache = new();
    private static readonly Dictionary<string, Layer> RimCache = new();

    /// <summary>Настройки подсистемы: цвет, доли размытия, плотности, подробность.</summary>
    private static SpriteOcclusionSettings Config => GraphicsSettings.Shading;

    /// <summary>
    /// Слепок настроек, при которых пёкся кэш. Правка полей в отладочной панели или
    /// в инспекторе обязана менять картинку, а изображения считаются один раз, поэтому
    /// расхождение слепка чистит кэш и заставляет пересчитать слои.
    /// </summary>
    private static (Color, int, float, float, float, float, float)? _bakedWith;

    /// <summary>
    /// Нарисовать контактную тень. Вызывается после площадки и до корпуса, в том же базисе,
    /// что и корпус: <paramref name="baseRadians"/> есть угол корпуса, уже выставленный
    /// вызывающим через DrawSetTransform.
    /// </summary>
    public static void DrawContact(CanvasItem canvas, UnitDefinition def, Rect2 bounds,
        float baseRadians)
    {
        if (Config.ContactEnabled)
            Draw(canvas, def, bounds, baseRadians, Contact(def?.Sprite),
                def?.AmbientOcclusionOuter ?? 0f);
    }

    /// <summary>Нарисовать затемнение по кайме. Вызывается сразу после корпуса.</summary>
    public static void DrawRim(CanvasItem canvas, UnitDefinition def, Rect2 bounds,
        float baseRadians)
    {
        if (Config.RimEnabled)
            Draw(canvas, def, bounds, baseRadians, Rim(def?.Sprite),
                def?.AmbientOcclusionInner ?? 0f);
    }

    private static void Draw(CanvasItem canvas, UnitDefinition def, Rect2 bounds,
        float baseRadians, Layer layer, float strength)
    {
        if (def == null || layer.IsEmpty || strength <= 0f)
            return;

        // Масштаб берётся от корпуса, иначе слой разойдётся с силуэтом. Делитель — та самая
        // подробность запекания: изображение крупнее спрайта, а на экране обязано занять
        // ровно его место
        float factor = SpriteArt.FitFactor(SpriteArt.LoadTrimmed(def.Sprite), bounds,
            def.SpriteScale);

        if (factor <= 0f)
            return;

        // Смещение тени задано в мировых осях, а базис отрисовки повёрнут по корпусу:
        // обратное вращение оставляет тень на одной стороне у любой постройки
        var shift = layer.Offset.Rotated(-baseRadians) * factor;

        var modulate = new Color(1f, 1f, 1f, Mathf.Clamp(strength, 0f, 1f));
        SpriteArt.DrawScaled(canvas, layer.Texture, factor / Mathf.Max(Config.Upscale, 1),
            bounds.GetCenter() + shift, modulate, def.SpriteRotationDegrees, baseRadians);
    }

    /// <summary>
    /// Контактная тень: размытый и смещённый силуэт. Рисуется под корпусом, поэтому шире
    /// его и по краям выходит за габарит.
    /// </summary>
    public static Layer Contact(string path)
    {
        if (string.IsNullOrEmpty(path))
            return default;

        Revalidate();

        if (ContactCache.TryGetValue(path, out var cached))
            return cached;

        var layer = BakeContact(LoadAlpha(path, out int width, out int height), width, height);
        ContactCache[path] = layer;
        return layer;
    }

    /// <summary>
    /// Затемнение по внутренней кайме. Совпадает с корпусом по размеру и рисуется поверх
    /// него, поэтому запаса по краям не имеет.
    /// </summary>
    public static Layer Rim(string path)
    {
        if (string.IsNullOrEmpty(path))
            return default;

        Revalidate();

        if (RimCache.TryGetValue(path, out var cached))
            return cached;

        var layer = BakeRim(LoadAlpha(path, out int width, out int height), width, height);
        RimCache[path] = layer;
        return layer;
    }

    /// <summary>
    /// Сверить настройки со слепком, при котором пёкся кэш, и очистить его при расхождении.
    /// Пересчёт стоит нескольких миллисекунд на вид постройки и происходит только в ответ
    /// на правку, поэтому в обычном кадре проверка сводится к сравнению кортежа.
    /// </summary>
    private static void Revalidate()
    {
        var key = Config.BakeKey;

        if (_bakedWith is { } previous && previous.Equals(key))
            return;

        ContactCache.Clear();
        RimCache.Clear();
        _bakedWith = key;
    }

    /// <summary>
    /// Прочитать альфу спрайта в массив. Поля обрезаются тем же способом, что и у корпуса
    /// в <see cref="SpriteArt.LoadTrimmed"/>: иначе слой затенения оказался бы шире корпуса
    /// на величину пустых полей и не совпал бы с ним.
    /// </summary>
    private static float[] LoadAlpha(string path, out int width, out int height)
    {
        width = 0;
        height = 0;

        var image = new Image();
        if (image.Load(path) != Error.Ok)
        {
            GD.PushWarning($"[SpriteOcclusion] нет картинки: {path}");
            return null;
        }

        image = SpriteArt.TrimUsed(image);

        if (image.GetFormat() != Image.Format.Rgba8)
            image.Convert(Image.Format.Rgba8);

        width = image.GetWidth();
        height = image.GetHeight();

        if (width <= 0 || height <= 0)
            return null;

        var alpha = new float[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                alpha[y * width + x] = image.GetPixel(x, y).A;
        }

        return alpha;
    }

    private static Layer BakeContact(float[] alpha, int width, int height)
    {
        if (alpha == null)
            return default;

        int side = Mathf.Min(width, height);
        int blur = Mathf.Max(2, Mathf.RoundToInt(side * Config.ContactBlur));

        // Два прохода размытия расширяют пятно вдвое против радиуса, и запас поля обязан
        // вместить это расширение: иначе тень упрётся в край изображения и оборвётся
        // прямой линией
        int padding = blur * 2 + 1;
        int paddedWidth = width + padding * 2;
        int paddedHeight = height + padding * 2;

        var field = new float[paddedWidth * paddedHeight];
        for (int y = 0; y < height; y++)
        {
            int row = (y + padding) * paddedWidth + padding;
            for (int x = 0; x < width; x++)
                field[row + x] = alpha[y * width + x];
        }

        Blur(field, paddedWidth, paddedHeight, blur);
        Blur(field, paddedWidth, paddedHeight, blur);

        for (int i = 0; i < field.Length; i++)
            field[i] = Mathf.Min(field[i] * Config.ContactOpacity, 1f);

        var direction = Vector2.Right.Rotated(Mathf.DegToRad(Config.ContactAngleDegrees));
        return new Layer(Compose(field, paddedWidth, paddedHeight),
            direction * side * Config.ContactOffset);
    }

    private static Layer BakeRim(float[] alpha, int width, int height)
    {
        if (alpha == null)
            return default;

        int side = Mathf.Min(width, height);
        int blur = Mathf.Max(2, Mathf.RoundToInt(side * Config.RimBlur));

        var opened = (float[])alpha.Clone();
        Blur(opened, width, height, blur);

        // Размытая альфа равна единице только там, где вся окрестность лежит внутри
        // силуэта, и убывает по мере приближения к краю. Разность с исходной альфой и есть
        // полоса вдоль края: снаружи её гасит множитель alpha, в глубине — единичное
        // значение размытой альфы. На самом краю разность близка к половине, поэтому
        // множитель удвоен — так RimOpacity задаёт непрозрачность именно у края
        var field = new float[alpha.Length];
        for (int i = 0; i < field.Length; i++)
            field[i] = Mathf.Clamp(alpha[i] * (1f - opened[i]) * 2f * Config.RimOpacity, 0f, 1f);

        return new Layer(Compose(field, width, height), Vector2.Zero);
    }

    /// <summary>
    /// Собрать текстуру: цвет один на всё изображение, посчитанное поле идёт в альфу.
    ///
    /// Растяжение делается уже по готовому полю, а не размытием в высоком разрешении:
    /// поле после размытия меняется плавно, и билинейная интерполяция даёт практически тот
    /// же результат, тогда как размытие вчетверо более крупного изображения обошлось бы
    /// в шестнадцать раз дороже. Цвет всюду одинаков, поэтому интерполяция каналов цвета
    /// ничего не смешивает и работает только на альфе.
    /// </summary>
    private static Texture2D Compose(float[] field, int width, int height)
    {
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        var color = Config.Shade;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                color.A = field[y * width + x];
                image.SetPixel(x, y, color);
            }
        }

        int upscale = Mathf.Clamp(Config.Upscale, 1, 8);

        if (upscale > 1)
            image.Resize(width * upscale, height * upscale, Image.Interpolation.Bilinear);

        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// Размытие прямоугольным ядром в два прохода — по строкам и по столбцам. За пределами
    /// изображения значение считается нулевым, а не повторяющим край: край обрезанного
    /// спрайта непрозрачен, и повтор оставил бы силуэт без затемнения там, где корпус
    /// вплотную подходит к границе файла.
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
