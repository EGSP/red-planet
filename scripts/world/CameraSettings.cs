using Godot;

/// <summary>
/// Настройка камеры: скорость панорамирования и пределы зума.
///
/// ПОЧЕМУ РЕСУРС. Те же основания, что у CloudSettings и FogSettings: подбор ведётся в одном
/// файле и подхватывается сценой партии, а не размазывается по полям узла в Session.tscn.
/// </summary>
[Tool]
[GlobalClass]
public partial class CameraSettings : Resource
{
    [ExportGroup("Панорамирование")]

    /// <summary>Скорость сдвига камеры клавишами, пикселей в секунду при зуме 1.</summary>
    [Export(PropertyHint.Range, "100,2000,10")] public float PanSpeed = 700f;

    [ExportGroup("Зум")]

    /// <summary>Множитель зума на один шаг колеса. Больше единица — крупнее шаг.</summary>
    [Export(PropertyHint.Range, "1.01,1.5,0.01")] public float ZoomStep = 1.12f;

    /// <summary>Дальний упор зума: меньшее значение — сильнее отдаление.</summary>
    [Export(PropertyHint.Range, "0.05,2,0.01")] public float ZoomMin = 0.25f;

    /// <summary>Ближний упор зума: большее значение — сильнее приближение.</summary>
    [Export(PropertyHint.Range, "0.1,4,0.01")] public float ZoomMax = 2.5f;
}
