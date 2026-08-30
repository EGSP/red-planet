using Godot;

/// <summary>
/// Настройка затенения, накладываемого узлами <see cref="ModelShade"/>: отброшенной тени,
/// контактной тени и затемнения по внутренней кайме. Подсистема графики; устройство приёма
/// описано в <see cref="ModelShade"/> и <see cref="ModelShadeBaker"/>.
///
/// ПОЧЕМУ ДОЛИ, А НЕ ПИКСЕЛИ. Радиус размытия и отход тени заданы долями меньшей стороны
/// текстуры: рисунки имеют разное разрешение, и постоянная в пикселях дала бы у крупного
/// изображения вдвое более узкую тень.
///
/// ЗДЕСЬ ЛЕЖИТ ОБЩЕЕ, А НЕ ЧАСТНОЕ. Значения этого ресурса действуют на все узлы затенения
/// сразу; отдельный узел вправе назначить своё через поля <c>Density</c>, <c>Blur</c>
/// и <c>Offset</c>. Направление света общее и частному переопределению не подлежит:
/// источник на карте один, и тень, уходящая у одной постройки в другую сторону, читалась бы
/// как ошибка рисунка.
/// </summary>
[Tool]
[GlobalClass]
public partial class ShadingSettings : Resource
{
    [ExportGroup("Отображение")]

    /// <summary>Рисовать ли тень, отброшенную корпусом на грунт.</summary>
    [Export] public bool CastEnabled = true;

    /// <summary>Рисовать ли контактную тень под корпусом.</summary>
    [Export] public bool ContactEnabled = true;

    /// <summary>Рисовать ли затемнение по внутренней кайме силуэта.</summary>
    [Export] public bool RimEnabled = true;

    [ExportGroup("Общее")]

    /// <summary>
    /// Цвет затенения. Не чистый чёрный: на охристом грунте чёрная тень выглядит провалом,
    /// а слегка холодный тёмный тон читается как затенённая поверхность. Альфа поля
    /// не используется — плотность задаётся отдельно для каждой роли.
    /// </summary>
    [Export] public Color Shade = new(0.05f, 0.04f, 0.07f);

    /// <summary>
    /// Куда уходят тени, в градусах от оси +X по часовой стрелке (ось Y в координатах канвы
    /// направлена вниз). Значение около 60 означает свет сверху и слева, отчего тень уходит
    /// вниз и вправо.
    ///
    /// Направление мировое, а не в осях сущности: свет падает на всю карту одинаково,
    /// и у повёрнутой постройки тень обязана уходить в ту же сторону, что и у неповёрнутой.
    /// </summary>
    [Export(PropertyHint.Range, "0,360,1")] public float LightAngleDegrees = 59.5f;

    [ExportGroup("Отброшенная тень")]

    /// <summary>Непрозрачность отброшенной тени.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float CastOpacity = 0.4f;

    /// <summary>Радиус размытия, доля меньшей стороны текстуры.</summary>
    [Export(PropertyHint.Range, "0,0.3,0.005")] public float CastBlur = 0.05f;

    /// <summary>Отход тени от корпуса, доля меньшей стороны текстуры.</summary>
    [Export(PropertyHint.Range, "0,0.5,0.005")] public float CastOffset = 0.14f;

    [ExportGroup("Контактная тень")]

    /// <summary>Непрозрачность тени вплотную к корпусу.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float ContactOpacity = 0.6f;

    /// <summary>Радиус размытия, доля меньшей стороны текстуры.</summary>
    [Export(PropertyHint.Range, "0,0.3,0.005")] public float ContactBlur = 0.08f;

    /// <summary>Отход тени от корпуса, доля меньшей стороны текстуры.</summary>
    [Export(PropertyHint.Range, "0,0.3,0.005")] public float ContactOffset;

    [ExportGroup("Затемнение по кайме")]

    /// <summary>Непрозрачность затемнения на самом краю силуэта.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float RimOpacity = 0.2f;

    /// <summary>Ширина полосы затемнения, доля меньшей стороны текстуры.</summary>
    [Export(PropertyHint.Range, "0,0.3,0.005")] public float RimBlur = 0.045f;

    /// <summary>Показывать ли слой этой роли. Признак гасит слой у всех сущностей сразу.</summary>
    public bool Enabled(ModelShadeKind kind) => kind switch
    {
        ModelShadeKind.Cast => CastEnabled,
        ModelShadeKind.Contact => ContactEnabled,
        _ => RimEnabled,
    };

    /// <summary>Общая непрозрачность слоя этой роли.</summary>
    public float Opacity(ModelShadeKind kind) => kind switch
    {
        ModelShadeKind.Cast => CastOpacity,
        ModelShadeKind.Contact => ContactOpacity,
        _ => RimOpacity,
    };

    /// <summary>Общий радиус размытия слоя этой роли, доля меньшей стороны текстуры.</summary>
    public float Blur(ModelShadeKind kind) => kind switch
    {
        ModelShadeKind.Cast => CastBlur,
        ModelShadeKind.Contact => ContactBlur,
        _ => RimBlur,
    };

    /// <summary>
    /// Слепок значений, влияющих на запечённые слои затенения. Расхождение слепка означает,
    /// что отладочная панель изменила настройки на ходу и тени пора печь заново —
    /// см. <see cref="ModelBake"/>.
    ///
    /// Числом, а не перечнем полей: сверка идёт у каждой модели, а перечислять четырнадцать
    /// величин ради сравнения значило бы повторять здесь список настроек второй раз.
    /// </summary>
    public int Stamp() => System.HashCode.Combine(
        System.HashCode.Combine(CastEnabled, ContactEnabled, RimEnabled, CastOpacity,
            ContactOpacity, RimOpacity, CastBlur, ContactBlur),
        System.HashCode.Combine(RimBlur, CastOffset, ContactOffset, Shade,
            LightAngleDegrees));

    /// <summary>
    /// Общий отход слоя этой роли от корпуса. У затемнения по кайме отхода нет по устройству:
    /// оно принадлежит самому корпусу, а не поверхности под ним.
    /// </summary>
    public float Offset(ModelShadeKind kind) => kind switch
    {
        ModelShadeKind.Cast => CastOffset,
        ModelShadeKind.Contact => ContactOffset,
        _ => 0f,
    };
}
