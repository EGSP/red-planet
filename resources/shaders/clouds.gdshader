// Облака и их тень: два слоя одного поля плотности, прочитанные со смещением.
//
// ОДНО ПОЛЕ НА ТЕНЬ И НА ТЕЛО. Тень берётся в самой точке, тело облака — в точке,
// сдвинутой на вектор lift. Отсюда согласованность: тень принадлежит ровно тому облаку,
// которое её отбрасывает, и разойтись они не могут по устройству. В трёхмерных решениях
// это же смещение получают пересечением луча солнца с плоскостью земли; при неподвижной
// камере вида сверху пересечение вырождается в постоянный вектор, и считать его незачем.
//
// ДВА СЛОЯ, А НЕ ОДИН. Одиночный слой шума, снесённый ветром, читается как наклейка,
// ползущая по карте: форма пятна не меняется вовсе. Второй слой идёт быстрее, мельче и под
// углом к ветру, поэтому сумма непрерывно перестраивается, и облака клубятся. Тот же второй
// слой выгрызает край первого (erosion) — приём взят из объёмного рендеринга облаков, где
// базовую форму размывают шумом Уорли. Здесь достаточно того же поля в другом масштабе.
//
// ФОРМА ЗАДАЁТСЯ МАСШТАБОМ В СИСТЕМЕ ВЕТРА. Координата поворачивается в систему ветра и
// делится на zoom * (scale_x, scale_y): вдоль ветра — scale_x, поперёк — scale_y. Кучевые
// облака получаются при scale_x, близком к единице, перистые — при трёх и более.
//
// ЦВЕТ БЕРЁТСЯ ИЗ ГРАДИЕНТОВ. Шейдер приводит плотность к отрезку от нуля до единицы, где
// ноль есть край облака, а единица — его ядро, и выбирает цвет из запечённой шкалы. Ни
// мягкости края, ни прозрачности он не знает: и то, и другое задано расстановкой точек
// шкалы, то есть остаётся целиком в ресурсе CloudSettings.
shader_type canvas_item;

// Карта плотности. Процедурный шум либо нарисованная бесшовная текстура — шейдеру всё
// равно, поэтому замена одного другим не требует правки кода
uniform sampler2D density : filter_linear_mipmap, repeat_enable;

// Шкалы «плотность → цвет». repeat_disable обязателен по той же причине, что и у тени
// препятствий: с повторением выборка на краю отрезка перескакивала бы к другому его концу
uniform sampler2D shade_ramp : source_color, filter_linear, repeat_disable;
uniform sampler2D body_ramp : source_color, filter_linear, repeat_disable;

uniform bool has_shade = true;
uniform bool has_body = true;

// Множители непрозрачности слоёв. Задаются снаружи (инспектор или сигнал зума) и не
// входят в шкалы CloudSettings: шкалы описывают вид, множители — долю проявления
uniform float shade_amount = 1.0;
uniform float body_amount = 1.0;

// Область мира, на которую натянут прямоугольник: UV переводится в мировые пиксели
uniform vec2 rect_origin;
uniform vec2 rect_size = vec2(1.0, 1.0);

// Направление ветра единичным вектором и путь, пройденный по нему, в мировых пикселях
uniform vec2 wind_dir = vec2(1.0, 0.0);
uniform float travel = 0.0;

// Базовый размер пятна плотности, мировых пикселей
uniform float zoom = 1200.0;

// Масштаб вдоль ветра и поперёк него относительно zoom
uniform float scale_x = 3.0;
uniform float scale_y = 1.0;

// Доля закрытого неба
uniform float coverage = 0.35;

// Насколько мелкий слой выгрызает край крупного
uniform float erosion = 0.35;

// Смещение тела облака относительно его тени, мировых пикселей
uniform vec2 lift = vec2(48.0, 64.0);

// Масштаб мелкого слоя относительно крупного
const float detail_size = 0.37;

// Во сколько раз быстрее сносит мелкий слой
const float detail_speed = 1.9;

// Поворот его хода относительно ветра, радиан. Несовпадение направлений и есть то, отчего
// сумма слоёв перестраивается, а не просто ползёт
const float detail_turn = 0.42;

// Расширение размаха поля. Бесшовный шум сшит из наложенных половин, отчего его значения
// сжаты к середине отрезка, и порог по покрытию срабатывал бы в узкой полоске настроек
const float field_contrast = 1.9;

// Координата в системе ветра с масштабом осей: X — вдоль ветра, Y — поперёк
vec2 aligned(vec2 world, vec2 dir)
{
	vec2 side = vec2(-dir.y, dir.x);
	vec2 extent = max(zoom * vec2(scale_x, scale_y), vec2(0.001));
	return vec2(dot(world, dir), dot(world, side)) / extent;
}

vec2 turn(vec2 dir, float angle)
{
	float c = cos(angle);
	float s = sin(angle);
	return vec2(dir.x * c - dir.y * s, dir.x * s + dir.y * c);
}

float expand(float value)
{
	return clamp((value - 0.5) * field_contrast + 0.5, 0.0, 1.0);
}

// Плотность облака в точке: ноль вне облака, единица в его ядре
float cloud_at(vec2 world)
{
	vec2 fine_dir = turn(wind_dir, detail_turn);

	vec2 base_uv = aligned(world - wind_dir * travel, wind_dir);
	vec2 fine_uv = aligned(world - fine_dir * (travel * detail_speed), fine_dir) / detail_size;

	float base = expand(texture(density, base_uv).r);
	float fine = expand(texture(density, fine_uv).r);

	// Край крупного слоя съедается там, где мелкий слой пуст. Остаток растягивается обратно
	// на весь отрезок: без этого эрозия опускала бы всё поле разом и работала бы вторым,
	// скрытым порогом, отчего правка её силы сбивала бы подобранное покрытие
	float bite = erosion * (1.0 - fine);
	float shape = (base - bite) / max(1.0 - bite, 0.001);

	// Порог по доле закрытого неба. Ширина перехода не задаётся: она вся в шкале
	float edge = 1.0 - coverage;
	return clamp((shape - edge) / max(coverage, 0.001), 0.0, 1.0);
}

void fragment()
{
	vec2 world = rect_origin + UV * rect_size;

	vec4 shade = has_shade ? texture(shade_ramp, vec2(cloud_at(world), 0.0)) : vec4(0.0);
	vec4 body = has_body ? texture(body_ramp, vec2(cloud_at(world + lift), 0.0)) : vec4(0.0);

	shade.a *= clamp(shade_amount, 0.0, 1.0);
	body.a *= clamp(body_amount, 0.0, 1.0);

	// Тело кладётся поверх тени, поэтому цвета смешиваются по его непрозрачности
	float alpha = body.a + shade.a * (1.0 - body.a);

	vec3 color = shade.rgb;

	if (alpha > 0.0001)
		color = (body.rgb * body.a + shade.rgb * shade.a * (1.0 - body.a)) / alpha;

	COLOR = vec4(color, alpha);
}
