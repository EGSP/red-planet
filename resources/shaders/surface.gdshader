// Базовый слой поверхности: несколько тайлящихся покрытий из биомов, поделивших между
// собой одно поле шума и отобранных ещё по температуре и высоте.
//
// ПОЧЕМУ ПОКРЫТИЙ РОВНО ЧЕТЫРЕ. Выбрать текстуру по вычисляемому номеру в языке шейдеров
// нельзя, поэтому покрытия разбираются развёрнутым кодом. Четырёх достаточно: при большем
// числе переходы становятся неразличимы, а стоимость выборок растёт линейно.
//
// ПОКРЫТИЯ НАКЛАДЫВАЮТСЯ ПО ПОРЯДКУ, А НЕ УСРЕДНЯЮТСЯ ПО ВЕСАМ. Прежде каждое покрытие
// давало вес, веса возводились в степень и перенормировались, а цвет был их средним. У
// такого правила два неустранимых недостатка. Во-первых, принадлежность отрезку доходила
// до единицы и там останавливалась, поэтому внутри пересечения двух отрезков веса были
// равны и цвет застывал ровной смесью в половину каждого; переход выглядел то размытым
// пятном, то отрезанным по линии, и настройками это не правилось. Во-вторых, само правило
// симметрично: выразить «щебень поверх песка» было нечем, и художнику приходилось разводить
// отрезки, вместо того чтобы указать порядок.
//
// Теперь первое покрытие кроет область целиком и служит подложкой, а каждое следующее
// накладывается поверх уже собранного цвета с собственной степенью покрытия. Порядок списка
// и есть порядок наложения, пустого места не остаётся ни при каких отрезках, а ширина всех
// переходов задана одним числом местности (smoothness).
//
// ГЕКСАГОНАЛЬНАЯ УКЛАДКА разрушает видимый повтор тайла: пространство разбито на решётку
// равносторонних треугольников, точка получает три ближайших узла, текстура читается
// трижды со смещением, определяемым узлом псевдослучайно, и результаты смешиваются по
// барицентрическим весам. Швов не возникает, поскольку на границах веса обращаются в ноль.
// Приём стоит трёх выборок вместо одной, поэтому вынесен в переключатель.

shader_type canvas_item;

// Область мира, на которую натянут прямоугольник: UV переводится в мировые пиксели
uniform vec2 rect_origin;
uniform vec2 rect_size = vec2(1.0, 1.0);

// Поля, посчитанные при создании мира. Канал R, значение от нуля до единицы
uniform sampler2D field_noise : filter_linear, repeat_disable;
uniform sampler2D field_height : filter_linear, repeat_disable;
uniform sampler2D field_temperature : filter_linear, repeat_disable;
uniform bool use_height = false;

// Оверлеи полей для подбора в инспекторе. Смешиваются поверх покрытия с общей альфой
uniform bool show_noise = false;
uniform bool show_temperature = false;
uniform bool show_height = false;
uniform float overlay_alpha = 0.55;

uniform int tile_count = 0;
uniform bool hex_tiling = true;

// Ширина перехода в долях шкалы поля: одно число на все отрезки всех покрытий и биомов
uniform float smoothness = 0.06;
uniform vec4 ambient : source_color = vec4(1.0, 1.0, 1.0, 1.0);

uniform sampler2D tile_0 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tile_1 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tile_2 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tile_3 : source_color, filter_linear_mipmap, repeat_enable;

// x — начало отрезка шума, y — конец, z — сторона тайла в пикселях
uniform vec4 tile_range_0 = vec4(0.0, 1.0, 512.0, 0.0);
uniform vec4 tile_range_1 = vec4(0.0, 0.0, 512.0, 0.0);
uniform vec4 tile_range_2 = vec4(0.0, 0.0, 512.0, 0.0);
uniform vec4 tile_range_3 = vec4(0.0, 0.0, 512.0, 0.0);

// x — начало отрезка высот, y — конец. Действует только при use_height
uniform vec4 tile_height_0 = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec4 tile_height_1 = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec4 tile_height_2 = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec4 tile_height_3 = vec4(0.0, 1.0, 0.0, 0.0);

// x — начало отрезка температуры покрытия, y — конец
uniform vec4 tile_temp_0 = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec4 tile_temp_1 = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec4 tile_temp_2 = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec4 tile_temp_3 = vec4(0.0, 1.0, 0.0, 0.0);

// x — начало отрезка температуры биома, которому принадлежит покрытие, y — конец
uniform vec4 tile_biome_0 = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec4 tile_biome_1 = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec4 tile_biome_2 = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec4 tile_biome_3 = vec4(0.0, 1.0, 0.0, 0.0);

uniform vec4 tile_tint_0 : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 tile_tint_1 : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 tile_tint_2 : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 tile_tint_3 : source_color = vec4(1.0, 1.0, 1.0, 1.0);

vec2 hash22(vec2 p)
{
	p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
	return fract(sin(p) * 43758.5453);
}

// Одна выборка покрытия: обычная либо по треугольной решётке
vec3 sample_tile(sampler2D tex, vec2 uv)
{
	vec2 dx = dFdx(uv);
	vec2 dy = dFdy(uv);

	if (!hex_tiling)
		return textureGrad(tex, uv, dx, dy).rgb;

	// Переход в косоугольные координаты равносторонней решётки
	vec2 skew = vec2(uv.x - uv.y * 0.57735027, uv.y * 1.15470054);
	vec2 cell = floor(skew);
	vec2 frac = fract(skew);

	vec2 n1;
	vec2 n2 = cell + vec2(1.0, 0.0);
	vec2 n3 = cell + vec2(0.0, 1.0);
	vec3 w;

	if (frac.x + frac.y < 1.0)
	{
		n1 = cell;
		w = vec3(1.0 - frac.x - frac.y, frac.x, frac.y);
	}
	else
	{
		n1 = cell + vec2(1.0, 1.0);
		w = vec3(frac.x + frac.y - 1.0, 1.0 - frac.y, 1.0 - frac.x);
	}

	// Сужение переходной полосы тем же приёмом, что и у покрытий
	w = pow(w, vec3(3.0));
	w /= max(w.x + w.y + w.z, 0.0001);

	return textureGrad(tex, uv + hash22(n1), dx, dy).rgb * w.x
		+ textureGrad(tex, uv + hash22(n2), dx, dy).rgb * w.y
		+ textureGrad(tex, uv + hash22(n3), dx, dy).rgb * w.z;
}

// Принадлежность отрезку. Переход положен посередине объявленной границы и растянут на
// smoothness в обе стороны, поэтому на самой границе покрытие проявлено ровно наполовину,
// а объявленный отрезок не съезжает при изменении ширины перехода.
//
// ГРАНИЦА, СОВПАДАЮЩАЯ С КРАЕМ ШКАЛЫ, ОГРАНИЧЕНИЕМ НЕ СЧИТАЕТСЯ. Отрезок во всю ширину
// означает отсутствие условия; будь край шкалы обыкновенной границей, покрытие затухало бы
// у нуля и единицы, где ему затухать не от чего.
float band(float value, vec2 range)
{
	float low = min(range.x, range.y);
	float high = max(range.x, range.y);
	float s = max(smoothness, 0.0001);

	float from = low > 0.0 ? smoothstep(low - s, low + s, value) : 1.0;
	float to = high < 1.0 ? 1.0 - smoothstep(high - s, high + s, value) : 1.0;

	return from * to;
}

// Степень покрытия пикселя одним тайлом: произведение принадлежностей всем его условиям.
// Отрезок биома здесь такое же условие, как собственные отрезки покрытия, и смягчается тем
// же smoothness: граница биома в базовом слое ничем не отличается от прочих границ.
float tile_cover(vec4 range, vec4 height, vec4 tile_temp, vec4 biome, float noise, float level, float temp)
{
	float a = band(noise, range.xy) * band(temp, biome.xy) * band(temp, tile_temp.xy);

	if (use_height)
		a *= band(level, height.xy);

	return a;
}

void fragment()
{
	vec2 world = rect_origin + UV * rect_size;
	float noise = texture(field_noise, UV).r;
	float level = use_height ? texture(field_height, UV).r : 0.5;
	float temp = texture(field_temperature, UV).r;

	// Первое покрытие кроет область целиком: собственные отрезки на него не действуют,
	// поскольку под ним ничего нет и проявляться ему не на чем. Отсюда невозможность
	// пустого места при любых настройках прочих покрытий — прежняя проверка суммы весов
	// и вызванная ею чёрная полоса не имеют теперь причины возникнуть.
	vec3 color = sample_tile(tile_0, world / tile_range_0.z) * tile_tint_0.rgb;

	// Порог отсекает выборки, вклад которых меньше половины деления восьмибитного цвета.
	// Он безопасен именно потому, что подложка уже собрана: пропуск верхнего покрытия
	// оставляет видимым нижнее, а не пустоту.
	if (tile_count > 1)
	{
		float a = tile_cover(tile_range_1, tile_height_1, tile_temp_1, tile_biome_1, noise, level, temp);

		if (a > 0.002)
			color = mix(color, sample_tile(tile_1, world / tile_range_1.z) * tile_tint_1.rgb, a);
	}

	if (tile_count > 2)
	{
		float a = tile_cover(tile_range_2, tile_height_2, tile_temp_2, tile_biome_2, noise, level, temp);

		if (a > 0.002)
			color = mix(color, sample_tile(tile_2, world / tile_range_2.z) * tile_tint_2.rgb, a);
	}

	if (tile_count > 3)
	{
		float a = tile_cover(tile_range_3, tile_height_3, tile_temp_3, tile_biome_3, noise, level, temp);

		if (a > 0.002)
			color = mix(color, sample_tile(tile_3, world / tile_range_3.z) * tile_tint_3.rgb, a);
	}

	color *= ambient.rgb;

	// Оверлеи полей: под поверхностью остаётся читаемая местность
	float a = clamp(overlay_alpha, 0.0, 1.0);

	if (show_noise)
		color = mix(color, vec3(noise), a);

	if (show_temperature)
	{
		// Синий (холодно) → красный (жарко)
		vec3 ramp = mix(vec3(0.12, 0.28, 0.92), vec3(0.95, 0.18, 0.12), temp);
		color = mix(color, ramp, a);
	}

	if (show_height && use_height)
	{
		// Зелёный (низ) → розовый (верх)
		vec3 ramp = mix(vec3(0.12, 0.72, 0.28), vec3(0.95, 0.38, 0.68), level);
		color = mix(color, ramp, a);
	}

	COLOR = vec4(color, 1.0);
}
