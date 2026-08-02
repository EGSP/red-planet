// Туман войны и обводка границы поля зрения.
//
// На вход подаётся растр видимости одной текстурой. Значение канала — не признак «видно
// или нет», а расстояние до границы, приведённое к отрезку от нуля до единицы: половина
// означает саму границу, а полный размах шкалы отвечает полосе шириной sdf_range пикселей
// в каждую сторону. Поэтому линейная фильтрация текстуры восстанавливает границу точнее,
// чем размер ячейки растра, и обводка получается гладкой линией, а не ступенчатой.
//
// ТОЛЩИНА СЧИТАЕТСЯ В ПИКСЕЛЯХ ЭКРАНА. Производная координаты растра даёт, сколько мировых
// пикселей приходится на пиксель экрана при текущем масштабе камеры; расстояние делится
// на эту величину. Отсюда обводка одинаковой толщины на любом масштабе — так же, как
// обводка фигур в режиме WidthMode.Screen.
//
// Прямоугольник, на который натянута текстура, больше карты, поэтому координата
// пересчитывается из координаты прямоугольника в координату растра, а выборка за его
// пределами повторяет краевое значение — так туман продолжается за краем карты.
shader_type canvas_item;

uniform vec4 fog_color : source_color = vec4(0.02, 0.02, 0.06, 0.72);
uniform vec4 outline_color : source_color = vec4(0.95, 0.95, 0.35, 0.9);

uniform float outline_width = 2.0;
uniform float softness = 1.5;

uniform bool fog_on = true;
uniform bool outline_on = true;

// Полоса линейности значения, мировых пикселей в каждую сторону от границы
uniform float sdf_range = 96.0;

// Сторона растра в мировых пикселях — ею производная координаты переводится в масштаб
uniform float field_size = 2624.0;

// Пересчёт координаты прямоугольника в координату растра
uniform vec2 field_scale = vec2(1.0);
uniform vec2 field_offset = vec2(0.0);

void fragment() {
    vec2 uv = UV * field_scale + field_offset;
    float value = texture(TEXTURE, clamp(uv, vec2(0.0), vec2(1.0))).r;

    // Мировых пикселей на пиксель экрана
    float world_per_pixel = max(fwidth(uv.x) * field_size, 0.00001);

    // Расстояние до границы: положительное внутри поля зрения, отрицательное снаружи
    float distance_px = (value - 0.5) * 2.0 * sdf_range / world_per_pixel;

    float soft = max(softness, 0.001);
    float inside = clamp(distance_px / soft + 0.5, 0.0, 1.0);

    float fog_alpha = fog_on ? fog_color.a * (1.0 - inside) : 0.0;

    float band = 1.0 - clamp((abs(distance_px) - outline_width * 0.5) / soft, 0.0, 1.0);
    float outline_alpha = outline_on ? outline_color.a * band : 0.0;

    // Обводка кладётся поверх тумана, поэтому цвета смешиваются по её непрозрачности
    float alpha = outline_alpha + fog_alpha * (1.0 - outline_alpha);

    vec3 color = fog_color.rgb;

    if (alpha > 0.0001) {
        color = (outline_color.rgb * outline_alpha
              + fog_color.rgb * fog_alpha * (1.0 - outline_alpha)) / alpha;
    }

    COLOR = vec4(color, alpha);
}
