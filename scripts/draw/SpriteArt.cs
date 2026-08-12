using System.Collections.Generic;
using Godot;

/// <summary>
/// Загрузка спрайтов с обрезкой прозрачных полей и кэшем по пути.
///
/// ПОЧЕМУ ОБРЕЗАЮТСЯ ПОЛЯ. Рисунок часто занимает лишь часть PNG, и непрозрачные пиксели
/// задают действительный габарит. Без обрезки спрайт масштабировался бы вместе с пустыми
/// полями и на карте выглядел бы мельче, чем задумано. Тот же приём уже применён к курсорам
/// в <see cref="CursorSystem"/>.
///
/// ПОЧЕМУ ФАЙЛ ЧИТАЕТСЯ НАПРЯМУЮ, А НЕ ЧЕРЕЗ ИМПОРТ. Импорт с <c>fix_alpha_border</c>
/// смягчает край альфы ещё до отрисовки. Для пиксельного корпуса нужна исходная сетка
/// пикселей файла.
///
/// ПОЧЕМУ ФИЛЬТР НЕ ВОССТАНАВЛИВАЕТСЯ ПОСЛЕ ОТРИСОВКИ. У <see cref="CanvasItem"/> фильтр
/// текстур — свойство узла на все его команды кадра, а не параметр одной. Выставить
/// <c>Nearest</c>, нарисовать и вернуть <c>Linear</c> означает, что к моменту показа
/// снова действует линейная интерполяция, и пиксели размыты. Поэтому <c>Nearest</c>
/// остаётся на узле; для площадки под постройкой это тоже приемлемо.
/// </summary>
public static class SpriteArt
{
    private static readonly Dictionary<string, Texture2D> Cache = new();
    private static readonly Dictionary<string, Texture2D> RawCache = new();
    private static readonly Dictionary<string, float> NativeExtentCache = new();

    /// <summary>
    /// Загрузить текстуру по пути, обрезав прозрачные поля. Повторный вызов с тем же путём
    /// возвращает уже подготовленную текстуру.
    /// </summary>
    public static Texture2D LoadTrimmed(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        if (Cache.TryGetValue(path, out var cached))
            return cached;

        var image = new Image();
        var error = image.Load(path);

        if (error != Error.Ok)
        {
            GD.PushWarning($"[SpriteArt] нет картинки: {path} ({error})");
            Cache[path] = null;
            return null;
        }

        image = TrimUsed(image);
        var texture = ImageTexture.CreateFromImage(image);
        Cache[path] = texture;
        return texture;
    }

    /// <summary>
    /// Загрузить текстуру без обрезки прозрачных полей. Исходная канва сохраняет общий
    /// центр слоёв корпуса и инструмента, поэтому их взаимное положение не меняется.
    /// </summary>
    public static Texture2D LoadRaw(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        if (RawCache.TryGetValue(path, out var cached))
            return cached;

        var image = new Image();
        var error = image.Load(path);

        if (error != Error.Ok)
        {
            GD.PushWarning($"[SpriteArt] нет картинки: {path} ({error})");
            RawCache[path] = null;
            return null;
        }

        var texture = ImageTexture.CreateFromImage(image);
        RawCache[path] = texture;
        return texture;
    }

    /// <summary>
    /// Обрезать прозрачные поля. Если непрозрачных пикселей нет — исходное изображение.
    /// </summary>
    public static Image TrimUsed(Image image)
    {
        if (image == null)
            return null;

        if (image.IsCompressed())
            image.Decompress();

        var used = image.GetUsedRect();

        if (used.Size.X > 0 && used.Size.Y > 0)
            return image.GetRegion(used);

        return image;
    }

    /// <summary>
    /// Нарисовать спрайт корпуса из определения: путь, масштаб и поворот берутся из
    /// <see cref="UnitDefinition"/>. <paramref name="baseRadians"/> и
    /// <paramref name="baseOrigin"/> — уже выставленный базис корпуса (например
    /// <c>BodyFacing</c> или трансформация иконки), к которому добавляется
    /// <see cref="UnitDefinition.SpriteRotationDegrees"/>.
    /// </summary>
    public static void DrawHull(CanvasItem canvas, UnitDefinition def, Rect2 bounds,
        Color? modulate = null, float baseRadians = 0f, Vector2 baseOrigin = default)
    {
        if (def == null || string.IsNullOrEmpty(def.Sprite))
            return;

        DrawFitted(canvas, LoadTrimmed(def.Sprite), bounds, modulate, def.SpriteScale,
            def.SpriteRotationDegrees, baseRadians, baseOrigin);
    }

    /// <summary>
    /// Во сколько раз пиксель текстуры крупнее пикселя канвы при вписывании в границы.
    /// Величина вынесена отдельно, поскольку слои затенения из <see cref="SpriteOcclusion"/>
    /// имеют собственный размер, но обязаны рисоваться в том же масштабе, что и корпус,
    /// иначе тень разойдётся с силуэтом. Ноль означает, что рисовать нечего.
    /// </summary>
    public static float FitFactor(Texture2D texture, Rect2 bounds, float scale)
    {
        if (texture == null || bounds.Size.X <= 0f || bounds.Size.Y <= 0f)
            return 0f;

        float width = texture.GetWidth();
        float height = texture.GetHeight();

        if (width <= 0f || height <= 0f)
            return 0f;

        float fit = Mathf.Min(bounds.Size.X / width, bounds.Size.Y / height);
        return fit * Mathf.Max(scale, 0.01f);
    }

    /// <summary>
    /// Вписать текстуру в прямоугольник с сохранением пропорций (contain): рисунок целиком
    /// помещается в границы и центрируется. <paramref name="scale"/> умножает этот размер;
    /// <paramref name="rotationDegrees"/> поворачивает рисунок вокруг центра габарита
    /// поверх <paramref name="baseRadians"/>.
    ///
    /// На узле выставляется ближайший сосед, чтобы при увеличении пиксели оставались резкими.
    /// </summary>
    public static void DrawFitted(CanvasItem canvas, Texture2D texture, Rect2 bounds,
        Color? modulate = null, float scale = 1f, float rotationDegrees = 0f,
        float baseRadians = 0f, Vector2 baseOrigin = default)
    {
        float factor = FitFactor(texture, bounds, scale);
        if (factor <= 0f)
            return;

        if (canvas.TextureFilter != CanvasItem.TextureFilterEnum.Nearest)
            canvas.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;

        DrawScaled(canvas, texture, factor, bounds.GetCenter(), modulate, rotationDegrees,
            baseRadians, baseOrigin);
    }

    /// <summary>
    /// Нарисовать спрайт в исходном размере вокруг заданной точки. Один пиксель текстуры
    /// соответствует одному пикселю канвы; <paramref name="presentationScale"/> применяется
    /// только потребителем, который обязан вписать изображение в интерфейсную область.
    /// </summary>
    public static void DrawNative(CanvasItem canvas, string path, Vector2 center,
        float rotationDegrees = 0f, float baseRadians = 0f, Vector2 baseOrigin = default,
        float presentationScale = 1f, Color? modulate = null)
    {
        if (canvas == null || string.IsNullOrEmpty(path))
            return;

        if (canvas.TextureFilter != CanvasItem.TextureFilterEnum.Nearest)
            canvas.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;

        canvas.DrawSetTransform(baseOrigin, baseRadians, Vector2.One);
        DrawScaled(canvas, LoadRaw(path), Mathf.Max(presentationScale, 0.01f), center,
            modulate, rotationDegrees, baseRadians, baseOrigin);
    }

    /// <summary>
    /// Наибольшее удаление угла непрозрачной области от её центра. Значение нужно
    /// интерфейсным иконкам для отдельного уменьшения спрайта до размера ячейки.
    /// </summary>
    public static float NativeExtent(string path)
    {
        if (string.IsNullOrEmpty(path))
            return 0f;

        if (NativeExtentCache.TryGetValue(path, out float cached))
            return cached;

        var image = new Image();
        if (image.Load(path) != Error.Ok)
        {
            NativeExtentCache[path] = 0f;
            return 0f;
        }

        if (image.IsCompressed())
            image.Decompress();

        var used = image.GetUsedRect();
        if (used.Size.X <= 0 || used.Size.Y <= 0)
        {
            NativeExtentCache[path] = 0f;
            return 0f;
        }

        var center = new Vector2(image.GetWidth(), image.GetHeight()) * 0.5f;
        var start = new Vector2(used.Position.X, used.Position.Y);
        var end = new Vector2(used.End.X, used.End.Y);
        float extent = Mathf.Max(
            Mathf.Max((start - center).Length(), (new Vector2(end.X, start.Y) - center).Length()),
            Mathf.Max((new Vector2(start.X, end.Y) - center).Length(), (end - center).Length()));

        NativeExtentCache[path] = extent;
        return extent;
    }

    /// <summary>
    /// Нарисовать текстуру в заданном масштабе вокруг точки. В отличие от
    /// <see cref="DrawFitted"/> размер не выводится из границ, а задаётся множителем: так
    /// слой затенения, который шире корпуса на запас <see cref="SpriteOcclusion.Layer"/>,
    /// ложится ровно поверх него.
    ///
    /// Фильтр текстур узел здесь не трогает: слои затенения размыты, и им нужна линейная
    /// интерполяция, тогда как корпусу — ближайший сосед. Выбор остаётся за вызывающим.
    /// </summary>
    public static void DrawScaled(CanvasItem canvas, Texture2D texture, float factor,
        Vector2 center, Color? modulate = null, float rotationDegrees = 0f,
        float baseRadians = 0f, Vector2 baseOrigin = default)
    {
        if (texture == null || factor <= 0f)
            return;

        float width = texture.GetWidth();
        float height = texture.GetHeight();

        if (width <= 0f || height <= 0f)
            return;

        var size = new Vector2(width, height) * factor;
        var color = modulate ?? Colors.White;

        if (Mathf.IsZeroApprox(rotationDegrees))
        {
            // В базисе иконки координаты center уже локальные относительно origin;
            // смещение dest на origin нужно, когда DrawSetTransform перенёс начало
            canvas.DrawTextureRect(texture, new Rect2(center - size * 0.5f, size), false, color);
            return;
        }

        // DrawSetTransform задаёт абсолютный поворот относительно узла, а не накапливает
        // его: поэтому базис корпуса передаётся явно и восстанавливается после
        float angle = baseRadians + Mathf.DegToRad(rotationDegrees);
        var pivot = baseOrigin + center.Rotated(baseRadians);
        canvas.DrawSetTransform(pivot, angle, Vector2.One);
        canvas.DrawTextureRect(texture, new Rect2(-size * 0.5f, size), false, color);
        canvas.DrawSetTransform(baseOrigin, baseRadians, Vector2.One);
    }
}
