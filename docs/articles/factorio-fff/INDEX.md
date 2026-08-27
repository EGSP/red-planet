# Factorio Friday Facts — подборка с разметкой

Шестьдесят две записи еженедельного блога Wube Software за 2013–2020 годы, отобранные в подборке [Seven Years of Factorio Friday Facts](https://spieswl.github.io/blog/2020/seven-years-of-factorio-friday-facts) Уильяма Спайса. Тексты сохранены полностью, изображения остались ссылками на сайт издателя, поэтому картинки открываются только при наличии сети.

Разметка своя: у каждой статьи указаны темы, оценка применимости к разработке стратегии с базостроением и одна фраза о том, что из статьи извлекается. Оценка применимости отвечает на вопрос об очерёдности чтения, а не о качестве статьи: записи про сетевой код и про издание игры отмечены низкой применимостью потому, что проект одиночный, а не потому, что записи слабые.

## Читать в первую очередь

Записи с высокой применимостью, в порядке номеров.

- [FFF #55](fff-55-mp-preview.md) — MP preview  
  Проверка детерминизма: состояния клиентов сравниваются по контрольным суммам, расхождение локализуется до конкретной подсистемы. Приём применим к воспроизведению записанных партий.
- [FFF #60](fff-60-tests-all-around.md) — Tests all around  
  Уровни автоматических проверок: модульные тесты, сценарные прогоны и тесты производительности; описано, что именно каждый уровень обнаруживает.
- [FFF #112](fff-112-better-noise.md) — Better noise  
  Оптимизация вычисления шума при генерации карты: значения считаются блоками, а не по отдельным точкам, что меняет стоимость на порядок.
- [FFF #115](fff-115-the-power-switch.md) — The power switch  
  Электрическая сеть как граф: рубильник соединяет и разделяет сети, поэтому требуется эффективный пересчёт связных компонент при изменении графа.
- [FFF #151](fff-151-the-plans-for-0-14.md) — The plans for 0.14  
  Разделение цикла обновления на потоки: какие подсистемы допускают параллельный расчёт, а какие обязаны остаться последовательными ради детерминизма.
- [FFF #176](fff-176-belts-optimization-for-0-15.md) — Belts optimization for 0.15  
  Переработка конвейеров: вместо обновления каждого предмета применяется сжатое представление линии, где движется только голова очереди. Основной приём оптимизации массовой симуляции.
- [FFF #194](fff-194-automated-combinator-pipeline.md) — Automated combinator pipeline  
  Полный конвейер подготовки графики: Blender, обработка кадров, собственная упаковка в атласы и генерация описаний на Lua. Пример автоматизации всех шагов между моделью и игрой.
- [FFF #200](fff-200-plans-for-0-16.md) — Plans for 0.16  
  Программируемый шум: генерация карты описывается выражениями в данных, а не кодом. Отдельная утилита предпросмотра карты без запуска игры.
- [FFF #201](fff-201-0-15-stable-but-not.md) — 0.15 Stable, but not really  
  Сокращение времени загрузки сохранения и запуска игры: разбор того, на что уходит время при чтении карты, подготовке атласов и обработке модов.
- [FFF #204](fff-204-another-day-another-optimisation.md) — Another day, another optimisation  
  Предварительная загрузка данных в кэш перед обходом сущностей: скорость обновления определяется доступом к памяти, а не вычислениями. Приведены замеры на нескольких фабриках.
- [FFF #209](fff-209-optimisation-is-a-way-of.md) — Optimisation is a way of life  
  Серия оптимизаций: электрическая сеть, дым, логистические боты; для каждой указаны приём и полученный выигрыш.
- [FFF #215](fff-215-multithreading-issues.md) — Multithreading issues  
  Разбор ошибки многопоточности: редко воспроизводимое состояние гонки и способ его локализации.
- [FFF #224](fff-224-bots-versus-belts.md) — Bots versus belts  
  Логистические боты обесценивают конвейеры: разбор случая, когда одна механика делает другую ненужной, и обсуждение вариантов исправления.
- [FFF #260](fff-260-new-fluid-system.md) — New fluid system  
  Модель течения жидкости: отдельный симулятор вне игры для проверки вариантов модели до её реализации.
- [FFF #264](fff-264-texture-streaming.md) — Texture streaming  
  Потоковая подгрузка текстур и виртуальная текстура: работа при нехватке видеопамяти без падения частоты кадров.
- [FFF #271](fff-271-fluid-optimisations-and-gui-style.md) — Fluid optimisations & GUI Style inspector  
  Оптимизация расчёта жидкостей и инспектор стилей интерфейса, показывающий устройство окна прямо в игре.
- [FFF #274](fff-274-new-fluid-system-2.md) — New fluid system 2  
  Итоговый алгоритм расчёта жидкостей: слияние соседних объёмов в общий сегмент, за счёт чего стоимость обновления перестаёт зависеть от длины трубы.
- [FFF #280](fff-280-visual-feedback-is-the-king.md) — Visual Feedback is the king  
  Визуальная обратная связь как основа понимания: показ пути, по которому пойдёт поезд, снимает необходимость догадываться о поведении системы.
- [FFF #296](fff-296-all-kinds-of-bugs.md) — All kinds of bugs  
  Правило: причина потери производительности почти никогда не там, где ожидается, поэтому измерение предшествует правке. Отдельно — довод против проверки на null вместо поиска причины.
- [FFF #317](fff-317-new-pathfinding-algorithm.md) — New pathfinding algorithm  
  Иерархический поиск пути: карта разбита на участки, между ними ищется грубый маршрут, внутри — точный алгоритмом A*. Основная запись серии по поиску пути.
- [FFF #331](fff-331-0-18-0-release-and.md) — 0.18.0 release & Train pathfinder changes  
  Разбор четырёх дефектов функции стоимости в поиске пути поездов: каждая ошибка в штрафах приводила к выбору заведомо худшего маршрута.
- [FFF #337](fff-337-statistics-gui-and-mod-debugger.md) — Statistics GUI and Mod Debugger  
  Интерфейс статистики производства: сбор показателей по времени и их подача графиками. Отдельно — отладчик модов с пошаговым исполнением.
- [FFF #349](fff-349-the-1-0-plan.md) — The 1.0 plan  
  Сокращение объёма работ ради срока выпуска. Отдельно — обозреватель прототипов, показывающий все описания сущностей игры прямо в интерфейсе.

## Тематические подборки

**Производительность и структуры данных:** [FFF #112](fff-112-better-noise.md), [FFF #115](fff-115-the-power-switch.md), [FFF #151](fff-151-the-plans-for-0-14.md), [FFF #176](fff-176-belts-optimization-for-0-15.md), [FFF #201](fff-201-0-15-stable-but-not.md), [FFF #204](fff-204-another-day-another-optimisation.md), [FFF #209](fff-209-optimisation-is-a-way-of.md), [FFF #260](fff-260-new-fluid-system.md), [FFF #264](fff-264-texture-streaming.md), [FFF #271](fff-271-fluid-optimisations-and-gui-style.md), [FFF #274](fff-274-new-fluid-system-2.md), [FFF #296](fff-296-all-kinds-of-bugs.md), [FFF #317](fff-317-new-pathfinding-algorithm.md), [FFF #182](fff-182-optimizations-always-more-optimizations.md), [FFF #184](fff-184-five-years-of-factorio.md)

**Многопоточность и детерминизм:** [FFF #55](fff-55-mp-preview.md), [FFF #151](fff-151-the-plans-for-0-14.md), [FFF #215](fff-215-multithreading-issues.md)

**Симуляция игровых систем:** [FFF #115](fff-115-the-power-switch.md), [FFF #176](fff-176-belts-optimization-for-0-15.md), [FFF #209](fff-209-optimisation-is-a-way-of.md), [FFF #260](fff-260-new-fluid-system.md), [FFF #271](fff-271-fluid-optimisations-and-gui-style.md), [FFF #274](fff-274-new-fluid-system-2.md), [FFF #331](fff-331-0-18-0-release-and.md), [FFF #81](fff-81-chain-signals.md), [FFF #312](fff-312-fluid-mixing-saga-and-landfill.md)

**Поиск пути:** [FFF #317](fff-317-new-pathfinding-algorithm.md), [FFF #331](fff-331-0-18-0-release-and.md)

**Генерация мира и шум:** [FFF #112](fff-112-better-noise.md), [FFF #200](fff-200-plans-for-0-16.md), [FFF #179](fff-179-new-resource-graphics-and-concrete.md)

**Графика и конвейер ассетов:** [FFF #194](fff-194-automated-combinator-pipeline.md), [FFF #264](fff-264-texture-streaming.md), [FFF #179](fff-179-new-resource-graphics-and-concrete.md), [FFF #348](fff-348-the-final-gui-update.md), [FFF #269](fff-269-roadmap-update-and-transport-belt.md)

**Интерфейс и удобство:** [FFF #280](fff-280-visual-feedback-is-the-king.md), [FFF #337](fff-337-statistics-gui-and-mod-debugger.md), [FFF #182](fff-182-optimizations-always-more-optimizations.md), [FFF #191](fff-191-gui-improvements.md), [FFF #212](fff-212-the-gui-update-part-1.md), [FFF #216](fff-216-paving-a-path-for-the.md), [FFF #238](fff-238-the-gui-update-part-ii.md), [FFF #243](fff-243-new-gui-tileset.md), [FFF #246](fff-246-the-gui-update-part-3.md), [FFF #277](fff-277-gui-progress-update.md), [FFF #279](fff-279-train-gui-and-modern-spitter.md), [FFF #348](fff-348-the-final-gui-update.md)

**Инструменты, отладка и тестирование:** [FFF #55](fff-55-mp-preview.md), [FFF #60](fff-60-tests-all-around.md), [FFF #194](fff-194-automated-combinator-pipeline.md), [FFF #200](fff-200-plans-for-0-16.md), [FFF #215](fff-215-multithreading-issues.md), [FFF #271](fff-271-fluid-optimisations-and-gui-style.md), [FFF #296](fff-296-all-kinds-of-bugs.md), [FFF #337](fff-337-statistics-gui-and-mod-debugger.md), [FFF #349](fff-349-the-1-0-plan.md), [FFF #206](fff-206-workflow-optimisation.md), [FFF #9](fff-9.md), [FFF #196](fff-196-back-on-track.md), [FFF #294](fff-294-blog-thoughts-and-lua-documentation.md)

**Геймдизайн и баланс:** [FFF #224](fff-224-bots-versus-belts.md), [FFF #1](fff-1.md), [FFF #81](fff-81-chain-signals.md), [FFF #191](fff-191-gui-improvements.md), [FFF #225](fff-225-bots-versus-belts-part-2.md), [FFF #238](fff-238-the-gui-update-part-ii.md), [FFF #277](fff-277-gui-progress-update.md), [FFF #309](fff-309-controversial-opinions.md), [FFF #312](fff-312-fluid-mixing-saga-and-landfill.md), [FFF #356](fff-356-blueprint-library-for-real.md), [FFF #69](fff-69-sympathy-for-the-creeper.md)

**Сеть и мультиплеер:** [FFF #55](fff-55-mp-preview.md), [FFF #151](fff-151-the-plans-for-0-14.md), [FFF #265](fff-265-nomenclature-and-steam-networking.md), [FFF #136](fff-136-map-transfers.md), [FFF #143](fff-143-matching-server-and-udp-nat.md), [FFF #196](fff-196-back-on-track.md), [FFF #302](fff-302-the-multiplayer-megapacket.md)

**Ввод и команды игрока:** [FFF #184](fff-184-five-years-of-factorio.md)

**Процесс разработки и команда:** [FFF #60](fff-60-tests-all-around.md), [FFF #296](fff-296-all-kinds-of-bugs.md), [FFF #349](fff-349-the-1-0-plan.md), [FFF #1](fff-1.md), [FFF #81](fff-81-chain-signals.md), [FFF #184](fff-184-five-years-of-factorio.md), [FFF #206](fff-206-workflow-optimisation.md), [FFF #212](fff-212-the-gui-update-part-1.md), [FFF #216](fff-216-paving-a-path-for-the.md), [FFF #265](fff-265-nomenclature-and-steam-networking.md), [FFF #356](fff-356-blueprint-library-for-real.md), [FFF #14](fff-14.md), [FFF #23](fff-23-year-after.md), [FFF #34](fff-34-sales-support-stress-and-steam.md), [FFF #46](fff-46-knowledge-sharing.md), [FFF #102](fff-102-getting-close.md), [FFF #135](fff-135-getting-organized.md), [FFF #223](fff-223-reflections-on-2017.md), [FFF #269](fff-269-roadmap-update-and-transport-belt.md), [FFF #294](fff-294-blog-thoughts-and-lua-documentation.md), [FFF #327](fff-327-2020-vision.md)

**Издание, сообщество, личный опыт:** [FFF #225](fff-225-bots-versus-belts-part-2.md), [FFF #356](fff-356-blueprint-library-for-real.md), [FFF #14](fff-14.md), [FFF #23](fff-23-year-after.md), [FFF #34](fff-34-sales-support-stress-and-steam.md), [FFF #192](fff-192-one-million.md), [FFF #352](fff-352-new-website.md), [FFF #360](fff-360-1-0-is-here.md)

## Полный список

| № | Дата | Название | Темы | Применимость |
|---|------|----------|------|--------------|
| [FFF #1](fff-1.md) | 2013-09-27 | Friday Facts #1 | геймдизайн, ИИ противника, процесс | средняя |
| [FFF #9](fff-9.md) | 2013-11-22 | Friday Facts #9 | сборка, инструменты, редактор | низкая |
| [FFF #14](fff-14.md) | 2013-12-27 | Friday Facts #14 | процесс, личный опыт | низкая |
| [FFF #23](fff-23-year-after.md) | 2014-02-28 | Year after | процесс, личный опыт | низкая |
| [FFF #34](fff-34-sales-support-stress-and-steam.md) | 2014-05-16 | Sales, Support, Stress and Steam | процесс, издание, личный опыт | низкая |
| [FFF #46](fff-46-knowledge-sharing.md) | 2014-08-08 | Knowledge sharing | процесс, команда | низкая |
| [FFF #55](fff-55-mp-preview.md) | 2014-10-10 | MP preview | детерминизм, тестирование, сеть | высокая |
| [FFF #60](fff-60-tests-all-around.md) | 2014-11-14 | Tests all around | тестирование, инструменты, процесс | высокая |
| [FFF #69](fff-69-sympathy-for-the-creeper.md) | 2015-01-17 | Sympathy for the creeper | геймдизайн, художественная концепция | низкая |
| [FFF #81](fff-81-chain-signals.md) | 2015-04-10 | Chain signals | геймдизайн, симуляция, процесс | средняя |
| [FFF #102](fff-102-getting-close.md) | 2015-09-04 | Getting close | инфраструктура, процесс | низкая |
| [FFF #112](fff-112-better-noise.md) | 2015-11-13 | Better noise | генерация мира, производительность, шум | высокая |
| [FFF #115](fff-115-the-power-switch.md) | 2015-12-08 | The power switch | производительность, симуляция, графы | высокая |
| [FFF #135](fff-135-getting-organized.md) | 2016-04-22 | Getting Organized | процесс, команда | низкая |
| [FFF #136](fff-136-map-transfers.md) | 2016-04-29 | Map Transfers | сеть, сериализация | низкая |
| [FFF #143](fff-143-matching-server-and-udp-nat.md) | 2016-06-17 | Matching server and UDP NAT punching | сеть | низкая |
| [FFF #151](fff-151-the-plans-for-0-14.md) | 2016-08-12 | The plans for 0.14 | многопоточность, производительность, сеть | высокая |
| [FFF #176](fff-176-belts-optimization-for-0-15.md) | 2017-02-03 | Belts optimization for 0.15 | производительность, структуры данных, симуляция | высокая |
| [FFF #179](fff-179-new-resource-graphics-and-concrete.md) | 2017-02-24 | New resource graphics & concrete | графика, конвейер ассетов, генерация мира | средняя |
| [FFF #182](fff-182-optimizations-always-more-optimizations.md) | 2017-03-17 | Optimizations, always more optimizations | производительность, интерфейс | средняя |
| [FFF #184](fff-184-five-years-of-factorio.md) | 2017-03-31 | Five years of Factorio | ввод, производительность, процесс | средняя |
| [FFF #191](fff-191-gui-improvements.md) | 2017-05-19 | Gui improvements | интерфейс, геймдизайн | средняя |
| [FFF #192](fff-192-one-million.md) | 2017-05-26 | One million | издание, сообщество | низкая |
| [FFF #194](fff-194-automated-combinator-pipeline.md) | 2017-06-09 | Automated combinator pipeline | графика, конвейер ассетов, инструменты | высокая |
| [FFF #196](fff-196-back-on-track.md) | 2017-06-23 | Back on track | сеть, инструменты | низкая |
| [FFF #200](fff-200-plans-for-0-16.md) | 2017-07-21 | Plans for 0.16 | генерация мира, инструменты | высокая |
| [FFF #201](fff-201-0-15-stable-but-not.md) | 2017-07-28 | 0.15 Stable, but not really | производительность, сериализация, загрузка | высокая |
| [FFF #204](fff-204-another-day-another-optimisation.md) | 2017-08-18 | Another day, another optimisation | производительность, работа с памятью | высокая |
| [FFF #206](fff-206-workflow-optimisation.md) | 2017-09-01 | Workflow optimisation | сборка, инструменты, процесс | средняя |
| [FFF #209](fff-209-optimisation-is-a-way-of.md) | 2017-09-21 | Optimisation is a way of life | производительность, симуляция | высокая |
| [FFF #212](fff-212-the-gui-update-part-1.md) | 2017-10-13 | The GUI update (Part 1) | интерфейс, процесс | средняя |
| [FFF #215](fff-215-multithreading-issues.md) | 2017-11-03 | Multithreading issues | многопоточность, отладка | высокая |
| [FFF #216](fff-216-paving-a-path-for-the.md) | 2017-11-10 | Paving a path for the GUI update | интерфейс, архитектура | средняя |
| [FFF #223](fff-223-reflections-on-2017.md) | 2017-12-29 | Reflections on 2017 | процесс | низкая |
| [FFF #224](fff-224-bots-versus-belts.md) | 2018-01-05 | Bots versus belts | геймдизайн, баланс | высокая |
| [FFF #225](fff-225-bots-versus-belts-part-2.md) | 2018-01-12 | Bots versus belts (part 2) | геймдизайн, сообщество | средняя |
| [FFF #238](fff-238-the-gui-update-part-ii.md) | 2018-04-13 | The GUI update (Part II) | интерфейс, геймдизайн | средняя |
| [FFF #243](fff-243-new-gui-tileset.md) | 2018-05-18 | New GUI tileset | интерфейс, стиль | средняя |
| [FFF #246](fff-246-the-gui-update-part-3.md) | 2018-06-08 | The GUI update (Part 3) | интерфейс | средняя |
| [FFF #260](fff-260-new-fluid-system.md) | 2018-09-14 | New fluid system | симуляция, производительность, прототипирование | высокая |
| [FFF #264](fff-264-texture-streaming.md) | 2018-10-12 | Texture streaming | графика, производительность, память | высокая |
| [FFF #265](fff-265-nomenclature-and-steam-networking.md) | 2018-10-19 | Nomenclature & Steam networking | терминология, процесс, сеть | средняя |
| [FFF #269](fff-269-roadmap-update-and-transport-belt.md) | 2018-11-16 | Roadmap update & Transport belt perspective | процесс, графика | низкая |
| [FFF #271](fff-271-fluid-optimisations-and-gui-style.md) | 2018-11-30 | Fluid optimisations & GUI Style inspector | производительность, симуляция, инструменты | высокая |
| [FFF #274](fff-274-new-fluid-system-2.md) | 2018-12-21 | New fluid system 2 | симуляция, производительность | высокая |
| [FFF #277](fff-277-gui-progress-update.md) | 2019-01-11 | GUI progress update | интерфейс, геймдизайн | средняя |
| [FFF #279](fff-279-train-gui-and-modern-spitter.md) | 2019-01-25 | Train GUI & Modern Spitter | интерфейс, UX | средняя |
| [FFF #280](fff-280-visual-feedback-is-the-king.md) | 2019-02-01 | Visual Feedback is the king | интерфейс, UX, обратная связь | высокая |
| [FFF #294](fff-294-blog-thoughts-and-lua-documentation.md) | 2019-05-10 | Blog thoughts & Lua documentation improvements | процесс, документация | низкая |
| [FFF #296](fff-296-all-kinds-of-bugs.md) | 2019-05-24 | All kinds of bugs | производительность, отладка, процесс | высокая |
| [FFF #302](fff-302-the-multiplayer-megapacket.md) | 2019-07-05 | The multiplayer megapacket | сеть | низкая |
| [FFF #309](fff-309-controversial-opinions.md) | 2019-08-23 | Controversial opinions | геймдизайн | средняя |
| [FFF #312](fff-312-fluid-mixing-saga-and-landfill.md) | 2019-09-13 | Fluid mixing saga & Landfill terrain | симуляция, геймдизайн | средняя |
| [FFF #317](fff-317-new-pathfinding-algorithm.md) | 2019-10-18 | New pathfinding algorithm | поиск пути, производительность | высокая |
| [FFF #327](fff-327-2020-vision.md) | 2019-12-27 | 2020 Vision | процесс | низкая |
| [FFF #331](fff-331-0-18-0-release-and.md) | 2020-01-24 | 0.18.0 release & Train pathfinder changes | поиск пути, симуляция | высокая |
| [FFF #337](fff-337-statistics-gui-and-mod-debugger.md) | 2020-03-06 | Statistics GUI and Mod Debugger | интерфейс, данные, инструменты | высокая |
| [FFF #348](fff-348-the-final-gui-update.md) | 2020-05-22 | The final GUI update | интерфейс, графика, звук | средняя |
| [FFF #349](fff-349-the-1-0-plan.md) | 2020-05-29 | The 1.0 plan | процесс, инструменты, данные | высокая |
| [FFF #352](fff-352-new-website.md) | 2020-06-19 | New website | издание, сообщество | низкая |
| [FFF #356](fff-356-blueprint-library-for-real.md) | 2020-07-17 | Blueprint library for real | личный опыт, геймдизайн, процесс | средняя |
| [FFF #360](fff-360-1-0-is-here.md) | 2020-08-14 | 1.0 is here! | издание, личный опыт | низкая |

## Разделы исходной подборки

Уильям Спайс сгруппировал записи иначе — по тому, о чём писали разработчики. Его разделы сохранены здесь для сверки с оригиналом.

**Getting Started:** [FFF #1](fff-1.md), [FFF #9](fff-9.md), [FFF #14](fff-14.md), [FFF #23](fff-23-year-after.md)

**On The Journey:** [FFF #34](fff-34-sales-support-stress-and-steam.md), [FFF #81](fff-81-chain-signals.md), [FFF #102](fff-102-getting-close.md), [FFF #135](fff-135-getting-organized.md), [FFF #184](fff-184-five-years-of-factorio.md), [FFF #192](fff-192-one-million.md), [FFF #200](fff-200-plans-for-0-16.md), [FFF #223](fff-223-reflections-on-2017.md), [FFF #269](fff-269-roadmap-update-and-transport-belt.md), [FFF #294](fff-294-blog-thoughts-and-lua-documentation.md), [FFF #309](fff-309-controversial-opinions.md), [FFF #327](fff-327-2020-vision.md), [FFF #352](fff-352-new-website.md)

**On Software Engineering:** [FFF #46](fff-46-knowledge-sharing.md), [FFF #55](fff-55-mp-preview.md), [FFF #60](fff-60-tests-all-around.md), [FFF #112](fff-112-better-noise.md), [FFF #215](fff-215-multithreading-issues.md), [FFF #265](fff-265-nomenclature-and-steam-networking.md), [FFF #274](fff-274-new-fluid-system-2.md), [FFF #296](fff-296-all-kinds-of-bugs.md), [FFF #312](fff-312-fluid-mixing-saga-and-landfill.md), [FFF #349](fff-349-the-1-0-plan.md)

**The Human Experience:** [FFF #69](fff-69-sympathy-for-the-creeper.md), [FFF #224](fff-224-bots-versus-belts.md), [FFF #225](fff-225-bots-versus-belts-part-2.md), [FFF #356](fff-356-blueprint-library-for-real.md), [FFF #360](fff-360-1-0-is-here.md)

**An Incredible Culture of Optimization:** [FFF #115](fff-115-the-power-switch.md), [FFF #151](fff-151-the-plans-for-0-14.md), [FFF #176](fff-176-belts-optimization-for-0-15.md), [FFF #182](fff-182-optimizations-always-more-optimizations.md), [FFF #201](fff-201-0-15-stable-but-not.md), [FFF #204](fff-204-another-day-another-optimisation.md), [FFF #206](fff-206-workflow-optimisation.md), [FFF #209](fff-209-optimisation-is-a-way-of.md), [FFF #260](fff-260-new-fluid-system.md), [FFF #264](fff-264-texture-streaming.md), [FFF #271](fff-271-fluid-optimisations-and-gui-style.md), [FFF #317](fff-317-new-pathfinding-algorithm.md), [FFF #331](fff-331-0-18-0-release-and.md)

**Crazy Times in Networking Troubleshooting:** [FFF #136](fff-136-map-transfers.md), [FFF #143](fff-143-matching-server-and-udp-nat.md), [FFF #196](fff-196-back-on-track.md), [FFF #302](fff-302-the-multiplayer-megapacket.md)

**Graphics and GUI Design:** [FFF #179](fff-179-new-resource-graphics-and-concrete.md), [FFF #191](fff-191-gui-improvements.md), [FFF #194](fff-194-automated-combinator-pipeline.md), [FFF #212](fff-212-the-gui-update-part-1.md), [FFF #216](fff-216-paving-a-path-for-the.md), [FFF #238](fff-238-the-gui-update-part-ii.md), [FFF #243](fff-243-new-gui-tileset.md), [FFF #246](fff-246-the-gui-update-part-3.md), [FFF #277](fff-277-gui-progress-update.md), [FFF #279](fff-279-train-gui-and-modern-spitter.md), [FFF #280](fff-280-visual-feedback-is-the-king.md), [FFF #337](fff-337-statistics-gui-and-mod-debugger.md), [FFF #348](fff-348-the-final-gui-update.md)
