using System;

/// <summary>
/// Режим главного экрана: «Entities», «Balance» или «Waves».
///
/// ЗАЧЕМ ОБЩИЙ ДОГОВОР. Главный экран одинаково поступает со всеми режимами: связывает
/// их со Store, обновляет после его изменения, снимает и восстанавливает состояние
/// интерфейса. Пока каждый режим имел собственный набор методов, добавление четвёртого
/// требовало правки главного экрана в шести местах, а забытый вызов обнаруживался
/// только пропавшей настройкой.
/// </summary>
public interface IContentEditorMode
{
    /// <summary>Подпись вкладки режима.</summary>
    string ModeTitle { get; }

    /// <summary>Связать со Store. Вызывается до первой загрузки файлов.</summary>
    void Bind(ContentEditorStore store, ContentEditorIconCache icons);

    /// <summary>Перерисовать по текущему состоянию Store.</summary>
    void Refresh();

    /// <summary>
    /// Ресурсы движка на диске изменились: сцены моделей и текстуры следует перечитать.
    ///
    /// ОТДЕЛЬНО ОТ <see cref="Refresh"/>. Тот согласует режим с содержимым Store, а Store
    /// знает только о .toml: правка сцены модели его состояние не меняет, и по одному лишь
    /// Refresh изображение осталось бы разобранным до правки. Умолчание пустое — режимам,
    /// не показывающим ресурсы движка, перечитывать нечего.
    /// </summary>
    void ReloadResources()
    {
    }

    /// <summary>Снять состояние интерфейса в снимок рабочего пространства.</summary>
    void CaptureInto(ContentEditorWorkspace workspace);

    /// <summary>Восстановить состояние интерфейса из снимка.</summary>
    void RestoreFrom(ContentEditorWorkspace workspace);

    /// <summary>Настройка режима изменилась и снимок следует записать.</summary>
    event Action UiStateChanged;

    /// <summary>Сообщение для общей строки состояния.</summary>
    event Action<string> StatusReported;

    /// <summary>Открыть сущность в режиме «Entities»: щелчок по строке таблицы или по виду волны.</summary>
    event Action<string> EntityOpenRequested;
}
