using Godot;

/// <summary>Что именно изображает узел затенения.</summary>
public enum ModelShadeKind
{
	/// <summary>
	/// Тень, отброшенная корпусом на грунт: широкая и заметно отнесённая по направлению
	/// света. Ставится в дереве раньше корпуса.
	/// </summary>
	Cast,

	/// <summary>
	/// Контактная тень: узкая полоса под самым корпусом, отделяющая его от поверхности.
	/// Ставится в дереве раньше корпуса.
	/// </summary>
	Contact,

	/// <summary>
	/// Затемнение по внутренней кайме силуэта — упрощённая имитация ambient occlusion.
	/// Ставится в дереве позже корпуса, поскольку принадлежит самому корпусу.
	/// </summary>
	Rim,
}

/// <summary>
/// Слой затенения, выведенный из альфы другого спрайта модели.
///
/// ПОЧЕМУ УЗЛОМ, А НЕ ПОЛЯМИ В .toml. Раньше оба слоя рисовались тем же узлом, что и корпус,
/// а их сила задавалась ключами <c>ao_inner</c> и <c>ao_outer</c>. Такое устройство держалось
/// на предположении, что у сущности ровно одно изображение: слой мог быть только один на всю
/// сущность и только у корпуса. Со сцены модели предположение снято — частей сколько угодно,
/// и затенять можно любую, — а порядок наложения задаётся положением в дереве, поэтому
/// контактная тень ложится между площадкой и корпусом простым перемещением узла выше.
///
/// ЗАОДНО СНЯТА ПОДРОБНОСТЬ ЗАПЕКАНИЯ. Пока слой рисовался узлом корпуса, он наследовал
/// его фильтр текстур: ради резкости пиксельного рисунка там стоит ближайший сосед, при
/// котором размытая тень распадается на ступени, и гладкость приходилось покупать запеканием
/// в повышенном разрешении. Собственный узел имеет собственный фильтр, поэтому изображение
/// печётся в разрешении исходного рисунка и сглаживается при выводе.
///
/// СВЯЗЬ С ИСТОЧНИКОМ. Узел повторяет положение, поворот и масштаб <see cref="Source"/>
/// и потому обязан лежать под тем же родителем: сравниваются и назначаются местные
/// координаты, а не мировые. Единственное расхождение с источником — отход тени,
/// см. <see cref="Distance"/>.
///
/// Значения плотности, размытия и отхода по умолчанию не заданы и берутся из
/// <see cref="ShadingSettings"/>; собственные нужны там, где вид части расходится с общим
/// правилом.
/// </summary>
[Tool, GlobalClass]
public partial class ModelShade : Sprite2D
{
	/// <summary>Спрайт, из альфы которого выводится слой. Обязан лежать под тем же родителем.</summary>
	[Export] public Sprite2D Source { get; set; }

	/// <summary>Что изображает слой. От роли зависят и запекание, и набор настроек.</summary>
	[Export] public ModelShadeKind Kind { get; set; } = ModelShadeKind.Contact;

	/// <summary>
	/// Непрозрачность слоя. Отрицательное значение — взять общую из
	/// <see cref="ShadingSettings"/>.
	/// </summary>
	[Export(PropertyHint.Range, "-1,1,0.01")] public float Density { get; set; } = -1f;

	/// <summary>
	/// Радиус размытия долей меньшей стороны текстуры. Отрицательное значение — взять общий.
	/// </summary>
	[Export(PropertyHint.Range, "-1,0.5,0.005")] public float Blur { get; set; } = -1f;

	/// <summary>
	/// Отход слоя от корпуса долей меньшей стороны текстуры. Отрицательное значение — взять
	/// общий. Названием отличается от <see cref="Sprite2D.Offset"/>, которое означает сдвиг
	/// рисунка внутри узла и здесь копируется у источника.
	///
	/// НАПРАВЛЕНИЕ ЗАДАЁТ НЕ УЗЕЛ. Источник света на карте один, поэтому сторона отхода берётся
	/// из <see cref="ShadingSettings.LightAngleDegrees"/>, а узел объявляет лишь величину.
	/// Отход считается в мировых осях, отчего у повёрнутой сущности тень уходит в ту же
	/// сторону, что и у неповёрнутой; ради этого положение узла пересчитывается при повороте
	/// корпуса.
	/// </summary>
	[Export(PropertyHint.Range, "-1,0.5,0.005")] public float Distance { get; set; } = -1f;

	/// <summary>Угол родителя в мире, при котором в последний раз считался отход.</summary>
	private float _alignedTo = float.NaN;

	/// <summary>Мировой вектор отхода в местных единицах родителя. Ноль — отхода нет.</summary>
	private Vector2 _shift;

	/// <summary>
	/// Слепок настроек, при которых слой собран. Отладочная панель правит поля ресурса
	/// на ходу, поэтому расхождение слепка означает, что слой надо собрать заново. Сравнение
	/// нескольких чисел обходится дешевле безусловной пересборки и позволяет видеть правку
	/// сразу, не перезапуская партию.
	/// </summary>
	private (bool, float, float, float, Color)? _builtWith;

	private static ShadingSettings Config => GraphicsSettings.Shade;

	public override void _Ready()
	{
		// Размытому изображению нужна линейная интерполяция, тогда как корпусу — ближайший
		// сосед; собственный узел и позволяет им разойтись
		TextureFilter = TextureFilterEnum.Linear;

		Rebuild();
	}

	public override void _Process(double delta)
	{
		// В редакторе сцену правят прямо сейчас: и источник, и величины меняются на глазах,
		// поэтому слой собирается заново каждый кадр
		if (Engine.IsEditorHint())
		{
			Rebuild();
			return;
		}

		if (_builtWith is not { } applied || !applied.Equals(Snapshot(Config)))
			Rebuild();

		// Отход задан в мировых осях и потому зависит от угла корпуса; изображение, цвет
		// и масштаб от угла не зависят
		if (_shift != Vector2.Zero)
			Align();
	}

	/// <summary>Величины настроек, влияющие на собранный слой.</summary>
	private (bool, float, float, float, Color) Snapshot(ShadingSettings config) =>
		(config.Enabled(Kind),
			Density >= 0f ? Density : config.Opacity(Kind),
			Blur >= 0f ? Blur : config.Blur(Kind),
			Distance >= 0f ? Distance : config.Offset(Kind),
			config.Shade);

	/// <summary>
	/// Собрать слой целиком: изображение, цвет, положение. Вызывается при вводе в дерево
	/// и всякий раз, когда настройки разошлись со слепком.
	/// </summary>
	private void Rebuild()
	{
		if (Source == null || Source.Texture == null)
		{
			Texture = null;
			Visible = false;
			_builtWith = null;
			return;
		}

		var config = Config;
		_builtWith = Snapshot(config);

		if (!config.Enabled(Kind))
		{
			Visible = false;
			return;
		}

		Visible = true;

		float blur = Blur >= 0f ? Blur : config.Blur(Kind);
		var baked = ModelShadeBaker.Bake(Source.Texture, Kind, blur);

		if (Texture != baked)
			Texture = baked;

		SelfModulate = config.Shade with
		{
			A = Mathf.Clamp(Density >= 0f ? Density : config.Opacity(Kind), 0f, 1f),
		};

		// Изображение выведено из текстуры источника, поэтому и накладывается в его системе
		// координат: любое расхождение поворота или масштаба развело бы тень с силуэтом
		Rotation = Source.Rotation;
		Scale = Source.Scale;
		Skew = Source.Skew;
		Centered = Source.Centered;
		Offset = Source.Offset;
		FlipH = Source.FlipH;
		FlipV = Source.FlipV;

		_shift = ShiftOf(config);
		_alignedTo = float.NaN;
		Align();
	}

	/// <summary>
	/// Мировой вектор отхода, выраженный в единицах родителя. Величина берётся от меньшей
	/// стороны текстуры и умножается на масштаб источника: отход задан долей рисунка,
	/// а положение назначается в координатах родителя.
	/// </summary>
	private Vector2 ShiftOf(ShadingSettings config)
	{
		float fraction = Distance >= 0f ? Distance : config.Offset(Kind);

		if (fraction <= 0f)
			return Vector2.Zero;

		var size = Source.Texture.GetSize();
		float side = Mathf.Min(size.X, size.Y) * Mathf.Abs(Source.Scale.X);

		return Vector2.Right.Rotated(Mathf.DegToRad(config.LightAngleDegrees))
			   * side * fraction;
	}

	/// <summary>
	/// Поставить слой относительно источника с поправкой на поворот корпуса. Поворот
	/// родителя в мире служит признаком того, что пересчёт нужен: у неподвижной сущности
	/// он не меняется никогда, и работа сводится к сравнению двух чисел.
	/// </summary>
	private void Align()
	{
		float owner = GetParent() is Node2D parent ? parent.GlobalRotation : 0f;

		if (Mathf.IsEqualApprox(owner, _alignedTo))
			return;

		_alignedTo = owner;
		Position = Source.Position + _shift.Rotated(-owner);
	}

	/// <summary>
	/// Убрать испечённое изображение из сохраняемой сцены.
	///
	/// ЗАЧЕМ. Слой печётся из альфы источника при вводе в дерево — и в игре, и в редакторе,
	/// — а <see cref="Sprite2D.Texture"/> есть обыкновенное сохраняемое свойство. Поэтому
	/// редактор, сохраняя сцену модели, записывал в неё готовое изображение целиком,
	/// в виде PackedByteArray на несколько сотен килобайт: у коммандера с четырьмя частями
	/// текстовая сцена разрослась до без малого мегабайта, и Godot сам предупреждал
	/// о медленном чтении и записи.
	///
	/// Данные при этом бесполезны: <see cref="Rebuild"/> печёт слой заново при каждом
	/// вводе в дерево и при всякой правке настроек затенения, то есть сохранённое
	/// изображение перезаписывается прежде, чем его успевают показать.
	/// </summary>
	public override void _ValidateProperty(Godot.Collections.Dictionary property)
	{
		if (property["name"].AsStringName() != Sprite2D.PropertyName.Texture)
			return;

		var usage = (PropertyUsageFlags)(int)property["usage"];
		property["usage"] = (int)(usage & ~PropertyUsageFlags.Storage);
	}

	public override string[] _GetConfigurationWarnings()
	{
		if (Source == null)
			return ["Не задан спрайт-источник: слой затенения выводится из его альфы."];

		if (Source.GetParent() != GetParent())
		{
			return
			[
				"Спрайт-источник лежит под другим родителем: узел повторяет его местные " +
				"координаты, и при разных родителях тень разойдётся с силуэтом.",
			];
		}

		return [];
	}
}
