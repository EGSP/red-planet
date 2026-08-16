using Godot;

/// <summary>
/// Доступ к иконкам темы редактора Godot.
///
/// ПОЧЕМУ НЕ СВОИ ФАЙЛЫ. Набор EditorIcons уже содержит опознаваемые знаки для всех
/// действий редактора контента (сохранение, отмена, перечитывание, закрытие) и сам
/// перекрашивается под светлую и тёмную тему. Собственные .svg пришлось бы держать
/// в двух вариантах и следить за их размером при разном масштабе интерфейса.
///
/// ИМЯ ПРОВЕРЯЕТСЯ. Состав EditorIcons меняется между версиями Godot, поэтому запрос
/// несуществующего имени не должен приводить ни к ошибке, ни к знаку вопроса на кнопке:
/// метод возвращает null, и кнопка остаётся с одним текстом.
/// </summary>
public static class ContentEditorTheme
{
    private const string IconTheme = "EditorIcons";
    private const string BaseColorSetting = "interface/theme/base_color";

    public static Texture2D Icon(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        var baseControl = EditorInterface.Singleton?.GetBaseControl();
        if (!GodotObject.IsInstanceValid(baseControl))
            return null;

        return baseControl.HasThemeIcon(name, IconTheme)
            ? baseControl.GetThemeIcon(name, IconTheme)
            : null;
    }

    /// <summary>Первое существующее имя из списка: имена иконок разнятся по версиям.</summary>
    public static Texture2D IconAny(params string[] names)
    {
        foreach (string name in names)
        {
            Texture2D icon = Icon(name);
            if (icon != null)
                return icon;
        }

        return null;
    }

    /// <summary>
    /// Фон панели. Берётся из базового цвета темы редактора, а не назначается числами:
    /// собственный оттенок выделялся синевой на сером интерфейсе Godot и не следовал
    /// за выбранной пользователем темой.
    /// </summary>
    public static Color PanelBackground()
    {
        Color baseColor = EditorBaseColor();
        return new Color(baseColor.R * 0.88f, baseColor.G * 0.88f, baseColor.B * 0.88f);
    }

    /// <summary>Рамка панели: осветление фона, поэтому собственного тона не вносит.</summary>
    public static Color PanelBorder() => new(1f, 1f, 1f, 0.11f);

    /// <summary>Цвет заголовка панели — нейтральный, чтобы не спорить с акцентом темы.</summary>
    public static Color PanelTitle() => new(1f, 1f, 1f, 0.62f);

    /// <summary>Готовый стиль панели редактора.</summary>
    public static StyleBoxFlat PanelStyle() => new()
    {
        BgColor = PanelBackground(),
        BorderColor = PanelBorder(),
        BorderWidthLeft = 1,
        BorderWidthRight = 1,
        BorderWidthTop = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 5,
        CornerRadiusTopRight = 5,
        CornerRadiusBottomLeft = 5,
        CornerRadiusBottomRight = 5,
        ContentMarginLeft = 6,
        ContentMarginRight = 6,
        ContentMarginTop = 5,
        ContentMarginBottom = 6,
    };

    private static Color EditorBaseColor()
    {
        var settings = EditorInterface.Singleton?.GetEditorSettings();
        if (settings != null && settings.HasSetting(BaseColorSetting))
        {
            Variant value = settings.Get(BaseColorSetting);
            if (value.VariantType == Variant.Type.Color)
                return value.AsColor();
        }

        // Тема недоступна вне редактора: нейтральный серый ближе к умолчанию Godot,
        // чем любой оттенок.
        return new Color(0.16f, 0.16f, 0.17f);
    }

    /// <summary>
    /// Назначить кнопке иконку и состояние. Отдельный метод затем, что выключенная
    /// кнопка должна объяснять причину: подсказка меняется вместе с Disabled.
    /// </summary>
    public static void SetAction(Button button, bool enabled, string enabledTooltip, string disabledReason)
    {
        if (!GodotObject.IsInstanceValid(button))
            return;

        button.Disabled = !enabled;
        button.TooltipText = enabled ? enabledTooltip : disabledReason;
    }
}
