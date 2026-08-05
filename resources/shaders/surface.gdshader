// Базовый слой поверхности: несколько тайлящихся покрытий, поделивших между собой одно
// поле шума.
//
// ПОЧЕМУ ПОКРЫТИЙ РОВНО ЧЕТЫРЕ. Выбрать текстуру по вычисляемому номеру в языке шейдеров
// нельзя, поэтому покрытия разбираются развёрнутым кодом. Четырёх достаточно: при большем
// числе переходы становятся неразличимы, а стоимость выборок растёт линейно.
//
// ПОЧЕМУ ВЕСА ВОЗВОДЯТСЯ В СТЕПЕНЬ. Прямое смешивание по весам даёт широкую полосу, где
// смешаны все покрытия сразу, и поверхность выглядит размытой. Возведение в степень с
// последующей перенормировкой оставляет почти везде одно господствующее покрытие и сужает
// переход. Тот же приём применён к весам гексагональной укладки.
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
uniform bool use_height = false;

uniform int tile_count = 0;
uniform bool hex_tiling = true;
uniform float sharpness = 4.0;
uniform vec4 ambient : source_color = vec4(1.0, 1.0, 1.0, 1.0);

uniform sampler2D tile_0 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tile_1 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tile_2 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tile_3 : source_color, filter_linear_mipmap, repeat_enable;

// x — начало отрезка шума, y — конец, z — ширина перехода, w — сторона тайла в пикселях
uniform vec4 tile_range_0 = vec4(0.0, 1.0, 0.08, 512.0);
uniform vec4 tile_range_1 = vec4(0.0, 0.0, 0.08, 512.0);
uniform vec4 tile_range_2 = vec4(0.0, 0.0, 0.08, 512.0);
uniform vec4 tile_range_3 = vec4(0.0, 0.0, 0.08, 512.0);

// x — начало отрезка высот, y — конец. Действует только при use_height
uniform vec4 tile_height_0 = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec4 tile_height_1 = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec4 tile_height_2 = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec4 tile_height_3 = vec4(0.0, 1.0, 0.0, 0.0);

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

// Принадлежность отрезку с размытыми краями
float band(float value, vec2 range, float falloff)
{
	float low = min(range.x, range.y);
	float high = max(range.x, range.y);
	float outside = max(low - value, value - high);

	if (falloff <= 0.0)
		return outside <= 0.0 ? 1.0 : 0.0;

	return 1.0 - smoothstep(0.0, falloff, outside);
}

float tile_weight(vec4 range, vec4 height, float noise, float level)
{
	float w = band(noise, range.xy, range.z);

	if (use_height)
		w *= band(level, height.xy, 0.05);

	return pow(w, sharpness);
}

void fragment()
{
	vec2 world = rect_origin + UV * rect_size;
	float noise = texture(field_noise, UV).r;
	float level = use_height ? texture(field_height, UV).r : 0.5;

	float w0 = tile_weight(tile_range_0, tile_height_0, noise, level);
	float w1 = tile_count > 1 ? tile_weight(tile_range_1, tile_height_1, noise, level) : 0.0;
	float w2 = tile_count > 2 ? tile_weight(tile_range_2, tile_height_2, noise, level) : 0.0;
	float w3 = tile_count > 3 ? tile_weight(tile_range_3, tile_height_3, noise, level) : 0.0;

	float total = w0 + w1 + w2 + w3;

	// Пустого места на поверхности быть не должно: там, где не проявилось ни одно
	// покрытие, берётся первое из списка
	if (total < 0.0001)
	{
		w0 = 1.0;
		total = 1.0;
	}

	vec3 color = vec3(0.0);

	if (w0 > 0.001)
		color += sample_tile(tile_0, world / tile_range_0.w) * tile_tint_0.rgb * w0;

	if (w1 > 0.001)
		color += sample_tile(tile_1, world / tile_range_1.w) * tile_tint_1.rgb * w1;

	if (w2 > 0.001)
		color += sample_tile(tile_2, world / tile_range_2.w) * tile_tint_2.rgb * w2;

	if (w3 > 0.001)
		color += sample_tile(tile_3, world / tile_range_3.w) * tile_tint_3.rgb * w3;

	COLOR = vec4(color / total * ambient.rgb, 1.0);
}
