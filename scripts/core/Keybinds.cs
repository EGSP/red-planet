using Godot;

/// <summary>
/// Переназначенные игроком клавиши: чтение, запись и сброс.
///
/// ПОЧЕМУ ОТДЕЛЬНО ОТ <see cref="InputActions"/>. Тот объявляет, какие действия в игре есть
/// и что они означают; это часть замысла, и меняется она правкой кода. Здесь же лежит
/// то, что игрок изменил под себя, и живёт оно в пользовательском файле, переживая
/// переустановку игры не дольше, чем сама папка настроек. Смешивать объявление с настройкой
/// значило бы, что забытая привязка молча подменяет замысел.
///
/// ХРАНИТСЯ ТОЛЬКО ОТЛИЧИЕ ОТ УМОЛЧАНИЯ. Действие, оставленное как есть, в файл не пишется:
/// иначе смена умолчания в коде не дошла бы до тех, кто однажды открывал настройки, —
/// у них навсегда осталась бы записанной старая клавиша.
/// </summary>
public static class Keybinds
{
    private const string Path = "user://input.cfg";
    private const string Section = "keys";

    /// <summary>
    /// Применить сохранённое поверх умолчаний. Зовётся из <see cref="InputActions.Ensure"/>,
    /// то есть после того, как все действия объявлены.
    ///
    /// Ошибка чтения не считается бедой: файла может не быть вовсе, и это обычное дело
    /// при первом запуске. Неизвестное действие пропускается молча — оно могло исчезнуть
    /// из игры после того, как файл был записан.
    /// </summary>
    public static void Load()
    {
        var file = new ConfigFile();

        if (file.Load(Path) != Error.Ok)
            return;

        foreach (string name in file.GetSectionKeys(Section))
        {
            if (InputActions.Find(name) == null)
                continue;

            // Пустая привязка тоже применяется: она означает, что игрок отдал клавишу
            // другому действию, и вернуть её обратно значило бы получить двух владельцев
            InputActions.Bind(name, InputBinding.Parse(file.GetValue(Section, name, "")));
        }
    }

    /// <summary>Назначить действию привязку и записать это на диск.</summary>
    public static void Rebind(string action, InputBinding binding)
    {
        Apply(action, binding);
        Save();
    }

    /// <summary>Вернуть всем действиям клавиши по умолчанию и стереть файл настроек.</summary>
    public static void ResetAll()
    {
        foreach (var action in InputActions.All)
            InputActions.Bind(action.Name, action.Default);

        if (FileAccess.FileExists(Path))
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(Path));
    }

    /// <summary>
    /// Назначить клавишу, отобрав её у тех, кто мог бы оказаться под тем же нажатием.
    ///
    /// КЛАВИША ПРИНАДЛЕЖИТ НЕСКОЛЬКИМ ДЕЙСТВИЯМ СРАЗУ, ЕСЛИ ОНИ ЧИТАЮТСЯ ПОРОЗНЬ. Приказ
    /// атаки читается при непустом выделении, отбор боевых машин — при пустом, и клавиша A
    /// служит обоим, не создавая двусмысленности. Спор возникает только между теми, кого
    /// одно и то же нажатие способно застать разом (<see cref="InputActions.Collide"/>);
    /// у такого спора отбирать клавишу необходимо, поскольку разбор берёт первое совпадение,
    /// и второй владелец не сработал бы никогда, а понять это по настройкам было бы нельзя.
    ///
    /// Прежний владелец остаётся без клавиши, и в настройках это видно пустой строкой.
    /// </summary>
    private static void Apply(string action, InputBinding binding)
    {
        if (InputActions.Find(action) is not { } target)
            return;

        if (!binding.IsEmpty)
            foreach (var other in InputActions.All)
                if (other.Name != action
                    && InputActions.Bound(other.Name) == binding
                    && InputActions.Collide(target.Scope, other.Scope))
                    InputActions.Bind(other.Name, InputBinding.None);

        InputActions.Bind(action, binding);
    }

    /// <summary>
    /// Записать отличия от умолчаний. Файл переписывается целиком: привязок два десятка,
    /// и правка отдельной строки не стоила бы того, чтобы держать состояние записи.
    /// </summary>
    private static void Save()
    {
        var file = new ConfigFile();

        foreach (var action in InputActions.All)
        {
            var binding = InputActions.Bound(action.Name);

            if (!binding.IsEmpty && binding != action.Default)
                file.SetValue(Section, action.Name, binding.Store());
        }

        file.Save(Path);
    }
}
