using System.Collections.Generic;
using Godot;

/// <summary>
/// Разбор сцены модели в <see cref="ModelBake"/>: единственное место, где дерево узлов
/// превращается в плоские данные.
///
/// ЧТО ПРОИСХОДИТ С УЗЛАМИ. Сцена поднимается один раз на вид и обходится сверху вниз.
/// Спрайты, точки вылета и объявления эффектов снимаются с дерева и записываются числами;
/// части-инструменты, частицы и лучи остаются узлами, поскольку в игре они работают —
/// первые поворачиваются, вторые проигрываются, третьи рисуют. То, что осталось, и есть
/// образец <see cref="ModelBake.Template"/>, копией которого рождается каждая машина.
///
/// ПЕРЕНОС УЗЛОВ. Снятый узел мог держать под собой оставшиеся: вспышка выстрела лежит
/// внутри точки вылета, а та снимается. Такие узлы переносятся к ближайшему уцелевшему
/// предку с накопленным преобразованием, отчего место их в модели не меняется.
///
/// ПОРЯДОК ОТРИСОВКИ СНИМАЕТСЯ ЗДЕСЬ ЖЕ. Авторский <c>z_index</c> в игру не переносится:
/// из него выводится ТРЕБОВАНИЕ к порядку — последовательность ключей вида, — а сами уровни
/// раздаёт <see cref="MaterialOrderBalancer"/>, согласуя требования всех разобранных видов.
/// Так одинаковые картинки разных машин попадают на общий уровень и складываются в один
/// вызов отрисовки, а порядок наложения внутри вида остаётся авторским.
///
/// СЛОИ ЗАТЕНЕНИЯ ПЕЧЁТСЯ ЗДЕСЬ ЖЕ. Раньше каждый слой собирал себя сам при вводе в дерево
/// и следил за настройками в собственной обработке кадра, то есть работа шла у каждой машины
/// в отдельности. Теперь изображение слоя выводится из альфы источника один раз на вид,
/// а зависимость тени от угла корпуса выражена вектором <see cref="ModelSprite.Shift"/>.
/// </summary>
public static class ModelBaker
{
    /// <summary>
    /// Требования к порядку по видам: путь к сцене — очередь ключей её картинок. Хранятся
    /// потому, что раскладка согласует ВСЕ виды разом, а виды разбираются по одному, при
    /// первом рождении машины.
    /// </summary>
    private static readonly Dictionary<string, MaterialOrderBalancer.Key[]> Demands = new();

    /// <summary>
    /// Действующая раскладка уровней. Пересобирается, когда разобран новый вид: уровень
    /// зависит от всех требований сразу, и добавление вида сдвигает номера у прочих.
    /// </summary>
    public static MaterialOrderBalancer Order { get; private set; } =
        new(System.Array.Empty<MaterialOrderBalancer.Key[]>(), Playground.Span);

    /// <summary>
    /// Номер раскладки. Растёт при всякой пересборке: показанные модели обязаны переспросить
    /// уровни своих картинок — см. <see cref="UnitModel"/>.
    /// </summary>
    public static int Revision { get; private set; }

    /// <summary>
    /// Пометка на узле образца, хранящая его постоянный номер. Копия машины получает пометку
    /// вместе с узлом и по ней узнаёт свой уровень: сам узел у каждой машины свой, а уровень
    /// у них общий.
    /// </summary>
    public const string LevelMeta = "draw_key";

    /// <summary>
    /// Постоянный номер уцелевшего узла: вид и порядковое место узла в обходе. Обход сцены
    /// повторяем, поэтому один и тот же узел получает один и тот же номер при всяком разборе.
    ///
    /// Свёртка FNV-1a, а не <c>GetHashCode</c>: тот не обещает одинакового ответа между
    /// запусками, а номер обязан совпадать с записанным в пометке.
    /// </summary>
    private static ulong Stamp(string source, int index)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;

        ulong hash = offset;

        foreach (char c in source)
            hash = (hash ^ c) * prime;

        hash = (hash ^ (ulong)(index + 1)) * prime;

        // Ноль означал бы «пометки нет», и узел ушёл бы на чужой уровень
        return hash == 0 ? prime : hash;
    }

    /// <summary>
    /// Принять требование вида и пересобрать раскладку. Повторный разбор того же вида
    /// вытесняет прежнее требование: после правки настроек затенения слои печутся заново,
    /// и старые ключи не значат ничего.
    /// </summary>
    private static void Demand(string path, MaterialOrderBalancer.Key[] sequence)
    {
        Demands[path] = sequence;
        Order = new MaterialOrderBalancer(Demands.Values, Playground.Span);
        Revision++;
    }

    /// <summary>
    /// Испечь модель по пути к сцене. Пустой путь и незагруженная сцена дают null:
    /// вызывающий обязан обойтись без модели, а не считать её отсутствие ошибкой.
    /// </summary>
    public static ModelBake Build(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        // В редакторе сцену правят прямо сейчас, поэтому разобранный ранее вариант
        // из общего набора ресурсов не годится
        var scene = Engine.IsEditorHint()
            ? ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.Replace)
            : ResourceLoader.Load<PackedScene>(path);

        if (scene == null)
        {
            GD.PushWarning($"[ModelBaker] сцена модели не загружена: {path}");
            return null;
        }

        if (scene.Instantiate() is not UnitModel root)
        {
            GD.PushWarning($"[ModelBaker] корень сцены модели не UnitModel: {path}");
            return null;
        }

        var bake = new ModelBake
        {
            Source = path,
            Stamp = GraphicsSettings.Shade.Stamp(),
            Template = root,
        };

        var sweep = new Sweep(bake);
        sweep.Walk(root, Sweep.Spot.Root(root, sweep.Body));

        // Требование к порядку снимается ПОСЛЕ обхода: очередь вывода видна лишь тогда, когда
        // собраны все картинки вида, а картинки корпуса и частей-инструментов копятся врозь
        Demand(path, sweep.Sequence());

        // Ссылка на спрайт корпуса указывала бы на снятый узел. Она значима лишь в открытой
        // сцене редактора, где слои затенения выводят себя из его альфы
        root.Body = null;

        // Освобождение снятых узлов идёт ПОСЛЕ всего разбора. Слой затенения выводит себя
        // из альфы другого спрайта, и в turret.tscn кайма объявлена позже корпуса: освободи
        // корпус сразу — и кайме нечего было бы читать
        foreach (var node in sweep.Dropped)
            if (Alive.Is(node))
                node.Free();

        bake.Body = sweep.Body.ToArray();
        bake.Tools = sweep.Tools.ToArray();
        bake.Deaths = sweep.Deaths.ToArray();

        Measure(bake);

        return bake;
    }

    /// <summary>
    /// Габарит изображения без слоёв затенения. Изображение тени шире корпуса на запас
    /// размытия и отнесено по направлению света; включи его в габарит — и корпус в ячейке
    /// панели стал бы мельче ровно на величину этого запаса.
    /// </summary>
    private static void Measure(ModelBake bake)
    {
        var box = new Rect2();
        bool any = false;

        foreach (var piece in bake.Body)
            Include(piece, Transform2D.Identity, ref box, ref any);

        foreach (var tool in bake.Tools)
            foreach (var piece in tool.Pieces)
                Include(piece, tool.Mount, ref box, ref any);

        if (!any)
            return;

        bake.Bounds = box;

        // Вписывают не прямоугольник, а круг: изображение поворачивают, и по осям
        // оно заняло бы не тот прямоугольник, что был измерен
        bake.Extent = Mathf.Max(
            Mathf.Max(box.Position.Length(), box.End.Length()),
            Mathf.Max(new Vector2(box.Position.X, box.End.Y).Length(),
                new Vector2(box.End.X, box.Position.Y).Length()));
    }

    /// <summary>Расширить габарит четырьмя углами картинки после её поворота.</summary>
    private static void Include(in ModelSprite piece, Transform2D mount, ref Rect2 box,
        ref bool any)
    {
        if (piece.Shade)
            return;

        var basis = mount * piece.Local;
        var rect = piece.Rect.Abs();

        System.Span<Vector2> corners =
        [
            basis * rect.Position,
            basis * new Vector2(rect.End.X, rect.Position.Y),
            basis * rect.End,
            basis * new Vector2(rect.Position.X, rect.End.Y),
        ];

        foreach (var corner in corners)
        {
            if (!any)
            {
                box = new Rect2(corner, Vector2.Zero);
                any = true;
                continue;
            }

            box = box.Expand(corner);
        }
    }

    /// <summary>
    /// Обход сцены. Состояние собрано в объект потому, что за один проход накапливаются
    /// три перечня, а на каждом шаге ведутся три системы отсчёта разом.
    /// </summary>
    private sealed class Sweep
    {
        private readonly ModelBake _bake;

        public readonly List<ModelSprite> Body = new();
        public readonly List<ModelBake.Tool> Tools = new();
        public readonly List<ModelBake.Wreck> Deaths = new();

        /// <summary>
        /// Всё, что рисуется, вместе со сквозным <c>z_index</c> и номером в обходе дерева.
        /// Собирается ради одного — очереди вывода, см. <see cref="Sequence"/>.
        /// </summary>
        private readonly List<Slot> _order = new();

        /// <summary>Картинки частей: массивы собираются после раскладки, а не по ходу обхода.</summary>
        private readonly List<(ModelBake.Tool Tool, List<ModelSprite> Pieces)> _parts = new();

        /// <summary>Номер в обходе дерева. Различает картинки с одинаковым <c>z_index</c>.</summary>
        private int _seq;

        /// <summary>
        /// Место в очереди вывода. Либо картинка — тогда заполнены перечень и номер в нём,
        /// либо уцелевший узел — тогда заполнен он сам.
        /// </summary>
        private readonly struct Slot
        {
            public readonly List<ModelSprite> Bucket;
            public readonly int Index;
            public readonly CanvasItem Node;
            public readonly int Z;
            public readonly int Seq;

            public Slot(List<ModelSprite> bucket, int index, CanvasItem node, int z, int seq)
            {
                Bucket = bucket;
                Index = index;
                Node = node;
                Z = z;
                Seq = seq;
            }
        }

        /// <summary>
        /// Очередь вывода вида: ключи в том порядке, в каком движок выводил бы картинки
        /// по авторской сцене — сперва по сквозному <c>z_index</c>, затем по порядку дерева.
        /// Это и есть требование к порядку, подаваемое раскладке.
        ///
        /// УЗЛЫ, ПЕРЕЖИВШИЕ ЗАПЕКАНИЕ, стоят в очереди наравне с картинками, и каждому даётся
        /// ключ, не совпадающий ни с чем. Нужно это не им: узел, оказавшийся на общем уровне
        /// с картинками, разделил бы их собой и отменил объединение. Свой ключ узел уносит
        /// пометкой, поскольку копия узла у каждой машины своя, а уровень у них общий.
        /// </summary>
        public MaterialOrderBalancer.Key[] Sequence()
        {
            _order.Sort((a, b) => a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.Seq.CompareTo(b.Seq));

            var keys = new List<MaterialOrderBalancer.Key>(_order.Count);
            int staged = 0;

            foreach (var slot in _order)
            {
                if (slot.Bucket != null)
                {
                    keys.Add(slot.Bucket[slot.Index].Key);
                    continue;
                }

                if (!Alive.Is(slot.Node))
                    continue;

                // Номер выводится из вида и порядкового места узла, а НЕ из самого узла:
                // при повторном разборе вида образец создаётся заново, а копии узлов
                // у машин остаются прежними и помнят номер пометкой. Номер по образцу
                // после пересборки перестал бы существовать, и узел ушёл бы на нулевой
                // уровень — в середину теней, разделяя их собой
                ulong id = Stamp(_bake.Source, staged++);

                slot.Node.SetMeta(LevelMeta, id);
                slot.Node.ZAsRelative = true;

                keys.Add(MaterialOrderBalancer.Key.Single(id));
            }

            foreach (var (tool, pieces) in _parts)
                tool.Pieces = pieces.ToArray();

            return keys.ToArray();
        }

        /// <summary>Узлы, снятые с дерева. Освобождаются одним разом по окончании разбора.</summary>
        public readonly List<Node> Dropped = new();

        public Sweep(ModelBake bake) => _bake = bake;

        /// <summary>
        /// Место обхода: где узел стоит в остатке дерева и в трёх системах отсчёта.
        ///
        /// <see cref="Survivor"/> — ближайший уцелевший предок; к нему переносится то,
        /// что переживёт снятие своего родителя. <see cref="Anchor"/> — корень модели либо
        /// часть-инструмент, владелец картинок. <see cref="Node"/> и <see cref="Baked"/> —
        /// часть, внутри которой идёт разбор, узлом и данными; у корпуса пусты.
        /// </summary>
        public readonly struct Spot
        {
            public readonly Node2D Survivor;
            public readonly Node2D Anchor;
            public readonly ModelTool Node;
            public readonly ModelBake.Tool Baked;
            public readonly List<ModelSprite> Bucket;

            /// <summary>Преобразование от уцелевшего предка к разбираемому узлу.</summary>
            public readonly Transform2D FromSurvivor;

            /// <summary>Преобразование от якоря картинок к разбираемому узлу.</summary>
            public readonly Transform2D FromAnchor;

            /// <summary>Преобразование от корня модели к разбираемому узлу.</summary>
            public readonly Transform2D FromRoot;

            /// <summary>
            /// Сквозной <c>z_index</c>, накопленный по цепочке предков. Именно его движок
            /// и сложил бы, выводя сцену: <c>z</c> у холста складывается по родителям, пока
            /// узел не объявит своё значение безотносительным.
            /// </summary>
            public readonly int Z;

            public Spot(Node2D survivor, Node2D anchor, ModelTool node, ModelBake.Tool baked,
                List<ModelSprite> bucket, Transform2D fromSurvivor, Transform2D fromAnchor,
                Transform2D fromRoot, int z)
            {
                Survivor = survivor;
                Anchor = anchor;
                Node = node;
                Baked = baked;
                Bucket = bucket;
                FromSurvivor = fromSurvivor;
                FromAnchor = fromAnchor;
                FromRoot = fromRoot;
                Z = z;
            }

            public static Spot Root(UnitModel root, List<ModelSprite> body) =>
                new(root, root, null, null, body, Transform2D.Identity, Transform2D.Identity,
                    Transform2D.Identity, 0);

            /// <summary>Шаг к потомку: три системы отсчёта сдвигаются на его преобразование.</summary>
            public Spot Step(Node child)
            {
                var step = child is Node2D placed ? placed.Transform : Transform2D.Identity;
                int z = Z;

                if (child is CanvasItem item)
                    z = item.ZAsRelative ? z + item.ZIndex : item.ZIndex;

                return new Spot(Survivor, Anchor, Node, Baked, Bucket, FromSurvivor * step,
                    FromAnchor * step, FromRoot * step, z);
            }

            /// <summary>Разбор внутри уцелевшего узла: он становится точкой отсчёта переноса.</summary>
            public Spot Inside(Node2D survivor) =>
                new(survivor, Anchor, Node, Baked, Bucket, Transform2D.Identity, FromAnchor,
                    FromRoot, Z);

            /// <summary>Разбор внутри части-инструмента: она сама себе якорь и точка отсчёта.</summary>
            public Spot Inside(ModelTool part, ModelBake.Tool baked, List<ModelSprite> pieces) =>
                new(part, part, part, baked, pieces, Transform2D.Identity,
                    Transform2D.Identity, FromRoot, Z);
        }

        /// <summary>Разобрать потомков узла.</summary>
        public void Walk(Node node, Spot at)
        {
            foreach (var child in node.GetChildren())
            {
                var here = at.Step(child);

                // Вложенная сцена не разбирается: частицы, лучи и прочее собранное отдельно
                // есть законченное целое, и снятие спрайта изнутри такой сцены оставило бы
                // её собственный код со ссылкой на снятый узел
                if (!string.IsNullOrEmpty(child.SceneFilePath))
                {
                    Keep(child, here);

                    // Вложенная сцена рисует сама и потому занимает своё место в очереди:
                    // оставь её на общем уровне с картинками — и она разделила бы их собой
                    if (child is CanvasItem drawn)
                        _order.Add(new Slot(null, 0, drawn, here.Z, _seq++));

                    continue;
                }

                switch (child)
                {
                    case ModelTool part:
                        Descend(part, here);
                        break;

                    case Sprite2D sprite:
                        if (Describe(sprite, here.FromAnchor, out var piece))
                        {
                            here.Bucket.Add(piece);
                            _order.Add(new Slot(here.Bucket, here.Bucket.Count - 1, null,
                                here.Z, _seq++));
                        }

                        Drop(sprite, here);
                        break;

                    case DeathEffect death:
                        Remember(death, here.FromRoot);
                        Drop(death, here);
                        break;

                    case ImpactEffect impact:
                        Remember(impact, here.Baked);
                        Drop(impact, here);
                        break;

                    case ProjectileDeclaration shot:
                        Remember(shot, here.Baked);
                        Drop(shot, here);
                        break;

                    // Точка вылета снимается вместе со своим узлом: в игре она есть число,
                    // а не место в дереве. Вспышка выстрела, лежащая внутри неё, переносится
                    // к самой части
                    case Marker2D marker when here.Node != null && marker == here.Node.Muzzle:
                        here.Baked.Muzzle = here.FromAnchor.Origin;
                        Drop(marker, here);
                        break;

                    default:
                        Keep(child, here);

                        // Уцелевший узел-держатель сам ничего не рисует, а его собственный
                        // <c>z</c> сложился бы с уровнями потомков и сбил бы раскладку
                        if (child is CanvasItem holder)
                            holder.ZIndex = 0;

                        Walk(child, child is Node2D placed ? here.Inside(placed) : here);
                        break;
                }
            }
        }

        /// <summary>
        /// Разобрать часть-инструмент: узел остаётся якорем своих картинок, а поддерево
        /// разбирается в его собственной системе отсчёта.
        /// </summary>
        private void Descend(ModelTool part, Spot at)
        {
            var baked = new ModelBake.Tool
            {
                ToolId = part.ToolId,
                Role = part.Role,
                FollowsAim = part.FollowsAim,
                Mount = at.FromRoot,
            };

            Tools.Add(baked);

            var pieces = new List<ModelSprite>();
            _parts.Add((baked, pieces));
            Walk(part, at.Inside(part, baked, pieces));

            // Точка вылета уже снята числом, а узла, на который поле ссылалось, больше нет
            part.Muzzle = null;

            Keep(part, at);

            // Часть-инструмент есть держатель картинок и сама не рисует: её <c>z</c> сложился
            // бы с уровнями своих картинок и вывел бы их из раскладки
            part.ZIndex = 0;
        }

        /// <summary>
        /// Снять узел с дерева, перенеся к уцелевшему предку всё, что под ним оставалось.
        /// </summary>
        private void Drop(Node2D dropped, Spot at)
        {
            Walk(dropped, at);

            dropped.GetParent()?.RemoveChild(dropped);
            Dropped.Add(dropped);
        }

        /// <summary>
        /// Оставить узел в образце. Переносится он лишь тогда, когда родитель снят:
        /// у прочих место в дереве и преобразование уже верны.
        /// </summary>
        private static void Keep(Node child, Spot at)
        {
            if (child.GetParent() == at.Survivor)
                return;

            child.GetParent()?.RemoveChild(child);

            // Владелец снимается: он значим для сохранения сцены, а перенос узла под другого
            // родителя делает запись о владении противоречивой, о чём движок и предупреждает
            child.Owner = null;

            at.Survivor.AddChild(child);

            if (child is Node2D placed)
                placed.Transform = at.FromSurvivor;
        }

        private void Remember(DeathEffect death, Transform2D toRoot)
        {
            if (death.Effect == null)
                return;

            Deaths.Add(new ModelBake.Wreck
            {
                Effect = death.Effect,
                Offset = toRoot.Origin,
                Angle = toRoot.Rotation,
                Size = death.Size,
                Delay = death.Delay,
            });
        }

        private static void Remember(ImpactEffect impact, ModelBake.Tool tool)
        {
            if (tool == null || impact.Effect == null)
                return;

            var list = new List<ModelBake.Declaration>(tool.Impacts)
            {
                new() { Effect = impact.Effect, Size = impact.Size },
            };

            tool.Impacts = list.ToArray();
        }

        private static void Remember(ProjectileDeclaration shot, ModelBake.Tool tool)
        {
            if (tool == null || shot.Effect == null)
                return;

            tool.Projectile ??= new ModelBake.Declaration
            {
                Effect = shot.Effect,
                Size = shot.Size,
            };
        }

        /// <summary>
        /// Описать спрайт числами. Ложь означает, что рисовать нечего: нет изображения,
        /// узел скрыт либо слой затенения выключен настройками.
        /// </summary>
        private bool Describe(Sprite2D sprite, Transform2D toAnchor, out ModelSprite piece)
        {
            piece = default;

            if (sprite is ModelShade shade)
                return Describe(shade, toAnchor, out piece);

            if (!sprite.Visible || sprite.Texture == null)
                return false;

            if (sprite.RegionEnabled || sprite.Hframes > 1 || sprite.Vframes > 1)
                GD.PushWarning($"[ModelBaker] {_bake.Source}: у спрайта {sprite.Name} задана " +
                               "область или раскадровка — запекается вся текстура целиком");

            piece = new ModelSprite(sprite.Texture, sprite.Material, toAnchor,
                Frame(sprite, sprite.Texture.GetSize()),
                sprite.Modulate * sprite.SelfModulate, Filter(sprite),
                Vector2.Zero, false);

            return true;
        }

        /// <summary>
        /// Описать слой затенения. Изображение выводится из альфы источника и накладывается
        /// в его системе координат: расхождение поворота или масштаба развело бы тень
        /// с силуэтом. Отход же задан в мировых осях и потому остаётся вектором
        /// <see cref="ModelSprite.Shift"/>, а не входит в место.
        ///
        /// Изображение, цвет и отход считает сам <see cref="ModelShade"/>: те же величины
        /// нужны узлу в открытой сцене редактора, и второй расчёт разошёлся бы с первым.
        /// </summary>
        private static bool Describe(ModelShade shade, Transform2D toAnchor,
            out ModelSprite piece)
        {
            piece = default;

            var source = shade.Source;
            var config = GraphicsSettings.Shade;

            if (source?.Texture == null || !shade.Visible || !config.Enabled(shade.Kind))
                return false;

            var texture = shade.Image(config);

            if (texture == null)
                return false;

            // Место слоя есть место источника: собственное преобразование узла затенения
            // в сцене не значит ничего и при правке источника разошлось бы с ним
            var parent = toAnchor * shade.Transform.AffineInverse();

            piece = new ModelSprite(texture, shade.Material, parent * source.Transform,
                Frame(source, texture.GetSize()), shade.Modulate * shade.Tint(config),
                CanvasItem.TextureFilterEnum.Linear,
                shade.Shift(config) * parent.Scale.X, true);

            return true;
        }

        /// <summary>
        /// Прямоугольник вывода в осях спрайта. Отражение задаётся отрицательным размером:
        /// у сервера отрисовки для картинки есть только прямоугольник.
        /// </summary>
        private static Rect2 Frame(Sprite2D sprite, Vector2 size)
        {
            var origin = sprite.Centered ? sprite.Offset - size * 0.5f : sprite.Offset;
            var rect = new Rect2(origin, size);

            if (sprite.FlipH)
                rect = new Rect2(rect.Position + new Vector2(rect.Size.X, 0f),
                    new Vector2(-rect.Size.X, rect.Size.Y));

            if (sprite.FlipV)
                rect = new Rect2(rect.Position + new Vector2(0f, rect.Size.Y),
                    new Vector2(rect.Size.X, -rect.Size.Y));

            return rect;
        }

        /// <summary>
        /// Способ увеличения текстуры. Наследование от родителя разрешается при запекании:
        /// родителем картинки становится корень модели, и цепочка наследования, на которую
        /// спрайт рассчитывал в сцене, до неё не доходит.
        /// </summary>
        private static CanvasItem.TextureFilterEnum Filter(CanvasItem item)
        {
            var filter = item.TextureFilter;

            while (filter == CanvasItem.TextureFilterEnum.ParentNode
                   && item.GetParent() is CanvasItem parent)
            {
                filter = parent.TextureFilter;
                item = parent;
            }

            return filter == CanvasItem.TextureFilterEnum.ParentNode
                ? CanvasItem.TextureFilterEnum.Nearest
                : filter;
        }
    }
}
