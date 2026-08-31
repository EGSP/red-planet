using Godot;

/// <summary>
/// Запечённое изображение вида: всё, что сцена модели описывает, приведённое к виду,
/// пригодному для создания экземпляра без разбора дерева узлов.
///
/// ЗАЧЕМ ЗАПЕКАНИЕ. Сцена модели удобна художнику именно тем, чем неудобна игре: части
/// вложены друг в друга, у каждой свой узел, а у некоторых — своя обработка кадра. На карте
/// машин одного вида сотни, и каждая поднимала бы всё дерево целиком, включая узлы, которые
/// после рождения ничего не делают: спрайты, точки вылета, объявления эффектов. Запекание
/// проходит сцену ОДИН раз на вид и оставляет от неё две вещи: плоский перечень картинок
/// (<see cref="ModelSprite"/>) и образец из тех узлов, которые в игре действительно работают,
/// — частей-инструментов, частиц и лучей.
///
/// ГРАНИЦА С СИМУЛЯЦИЕЙ ПРЕЖНЯЯ. Здесь нет ни одной игровой величины: прочность, дальность
/// и стоимость принадлежат .toml, а запечённая модель отвечает лишь на вопрос, как вид
/// выглядит и где у него точки вылета.
///
/// ГДЕ ХРАНИТСЯ. В самом определении вида, полем <see cref="UnitDefinition.Bake"/>:
/// определение существует в единственном экземпляре на вид и живёт всю партию, поэтому
/// отдельный кэш по пути к сцене был бы вторым ключом к тем же данным.
/// </summary>
public sealed class ModelBake
{
    /// <summary>Подвижная часть: ствол либо рабочая рука.</summary>
    public sealed class Tool
    {
        /// <summary>Идентификатор инструмента из списка tools файла юнита. Пусто — связь по роли.</summary>
        public string ToolId = "";

        /// <summary>Чем часть является для игровых систем.</summary>
        public ModelToolRole Role;

        /// <summary>Поворачивается ли часть вслед за наведением.</summary>
        public bool FollowsAim = true;

        /// <summary>Где часть сидит на корпусе.</summary>
        public Transform2D Mount = Transform2D.Identity;

        /// <summary>Точка вылета в осях самой части.</summary>
        public Vector2 Muzzle;

        /// <summary>Собственные картинки части.</summary>
        public ModelSprite[] Pieces = System.Array.Empty<ModelSprite>();

        /// <summary>Вспышки попадания, объявленные внутри части.</summary>
        public Declaration[] Impacts = System.Array.Empty<Declaration>();

        /// <summary>
        /// Вспышки выстрела, снятые с частиц внутри части. Место и поворот заданы в осях
        /// самой части: узлов вспышки в игре не остаётся, и вычислять их положение
        /// приходится от преобразования ствола — см. <see cref="EffectSystem"/>.
        /// </summary>
        public Emitter[] Flashes = System.Array.Empty<Emitter>();

        /// <summary>Снаряд, объявленный внутри части. Null — берётся общий.</summary>
        public Declaration Projectile;
    }

    /// <summary>
    /// Объявленная сцена и её размер — запечённый вид того, что в сцене модели задаётся
    /// узлом <see cref="EffectDeclaration"/>. Вспышка попадания и снаряд объявляются
    /// одинаково, ссылкой и множителем размера, поэтому и тип у них один; различает их
    /// поле части, в котором объявление лежит.
    /// </summary>
    public sealed class Declaration
    {
        public PackedScene Effect;
        public float Size = 1f;
    }

    /// <summary>
    /// Запечённый эффект частиц, лежавший прямо в сцене модели, вместе с его местом.
    ///
    /// ЗАЧЕМ СНИМАТЬ ЕГО С ДЕРЕВА. Узел выдачи обходится движку в отдельный вычислительный
    /// вызов независимо от числа частиц, а пыль хода и вспышка выстрела лежат при каждой
    /// машине и при каждом стволе. Запекание оставляет от них числа, а частицы выпускает
    /// общее поле — см. <see cref="ParticleYard"/>.
    /// </summary>
    public sealed class Emitter
    {
        public EffectBake Effect;
        public Vector2 Offset;
        public float Angle;
    }

    /// <summary>
    /// Объявленный взрыв гибели. Место и поворот заданы в осях корпуса: в миг гибели
    /// спрашивать преобразование будет уже не у кого — см. <see cref="EffectSystem"/>.
    /// </summary>
    public sealed class Wreck
    {
        public PackedScene Effect;
        public Vector2 Offset;
        public float Angle;
        public float Size = 1f;
        public float Delay;
    }

    /// <summary>Путь к сцене, из которой модель испечена.</summary>
    public string Source = "";

    /// <summary>
    /// Определение, которому модель принадлежит. Нужно пересборке: при правке настроек
    /// затенения обновлённая модель обязана заменить прежнюю в том же поле, иначе
    /// определение продолжило бы раздавать устаревшую.
    /// </summary>
    public UnitDefinition Owner;

    /// <summary>Картинки, закреплённые на корпусе.</summary>
    public ModelSprite[] Body = System.Array.Empty<ModelSprite>();

    /// <summary>Подвижные части в порядке обхода сцены — том же, что у UnitModel.Tools.</summary>
    public Tool[] Tools = System.Array.Empty<Tool>();

    /// <summary>Объявленные взрывы гибели.</summary>
    public Wreck[] Deaths = System.Array.Empty<Wreck>();

    /// <summary>Пыль хода, снятая с корпуса. Место и поворот заданы в осях корпуса.</summary>
    public Emitter[] Trails = System.Array.Empty<Emitter>();

    /// <summary>
    /// Габарит изображения в осях модели без слоёв затенения. Считается при запекании:
    /// иконка панели и поле редактора контента вписывают им изображение в свою площадь.
    /// </summary>
    public Rect2 Bounds = new(-Const.Unit * 0.5f, -Const.Unit * 0.5f, Const.Unit, Const.Unit);

    /// <summary>Наибольшее удаление угла габарита от начала координат.</summary>
    public float Extent = Const.Unit * 0.70710678f;

    /// <summary>
    /// Образец узлов, переживших запекание: корень модели, части-инструменты, частицы
    /// и лучи. В дерево сцены не входит никогда — экземпляры получаются его копированием.
    /// </summary>
    public Node2D Template;

    /// <summary>
    /// Слепок настроек затенения, при которых модель испечена. Отладочная панель правит
    /// их на ходу, и расхождение слепка означает, что тени надо испечь заново.
    /// </summary>
    public int Stamp;

    /// <summary>
    /// Слепок настроек, снятый один раз за кадр: сверяет его каждая модель, а меняются
    /// настройки разве что рукой в отладочной панели.
    /// </summary>
    private static int _stamp;
    private static ulong _stampFrame = ulong.MaxValue;

    /// <summary>
    /// Забыть слепок, снятый в этом кадре. Нужно правке настроек: она приходит тем же кадром,
    /// в котором слепок уже снят, и без сброса сверка вернула бы величину, снятую до правки.
    /// </summary>
    public static void Restamp() => _stampFrame = ulong.MaxValue;

    private static int Stamped()
    {
        ulong frame = Engine.GetProcessFrames();

        if (_stampFrame != frame)
        {
            _stampFrame = frame;
            _stamp = GraphicsSettings.Shade.Stamp();
        }

        return _stamp;
    }

    /// <summary>
    /// Запечённая модель вида. Печётся при первом обращении и хранится в самом определении.
    ///
    /// В РЕДАКТОРЕ ПЕЧЁТСЯ ЗАНОВО ВСЯКИЙ РАЗ: сцену правят прямо сейчас, и запомненный
    /// разбор показывал бы поле редактора контента таким, каким оно было до правки.
    /// </summary>
    public static ModelBake For(UnitDefinition def)
    {
        if (def is not { HasModel: true })
            return null;

        if (Engine.IsEditorHint())
            return Of(def.Model);

        if (def.Bake != null && def.Bake.Stamp == Stamped())
            return def.Bake;

        var baked = Of(def.Model);

        if (baked == null)
            return null;

        baked.Owner = def;
        def.Bake?.Discard();

        return def.Bake = baked;
    }

    /// <summary>
    /// Испечь модель по пути к сцене. Кэша здесь нет намеренно: постоянное хранилище —
    /// определение вида, а по пути модель просят те, у кого определения под рукой нет,
    /// то есть поле редактора контента.
    /// </summary>
    public static ModelBake Of(string path) => ModelBaker.Build(path);

    /// <summary>
    /// Действующая модель взамен устаревшей. Пока настройки затенения не менялись,
    /// возвращается та же самая; изменились — за моделью идёт обращение к её определению,
    /// и оно печёт замену один раз на вид, а не на каждую машину.
    /// </summary>
    public static ModelBake Refresh(ModelBake stale) =>
        stale == null || !stale.Stale ? stale
        : stale.Owner != null ? For(stale.Owner) : Of(stale.Source);

    /// <summary>Устарела ли модель относительно действующих настроек затенения.</summary>
    public bool Stale => Stamp != Stamped();

    /// <summary>Освободить образец. Зовётся при замене модели испечённой заново.</summary>
    public void Discard()
    {
        if (Alive.Is(Template))
            Template.Free();

        Template = null;
    }
}
