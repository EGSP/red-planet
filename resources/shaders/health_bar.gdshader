shader_type canvas_item;
render_mode blend_mix, unshaded;

// Материал полосы прочности. Один на все полосы: изображение и геометрия у них общие,
// поэтому весь мир обходится сервером отрисовки в один вызов на множественную сетку —
// см. HealthBarSystem. Доля прочности приходит экземпляром, а не отдельным материалом:
// у материала нет способа остаться общим, когда у каждой полосы своя доля.

// Цвет незаполненной части. Лежит под наложением, поэтому его альфа задаёт то,
// насколько сквозь полосу виден мир.
uniform vec4 back_color : source_color = vec4(0.0, 0.0, 0.0, 0.55);

// Цвета заполненной части по убыванию прочности. Порог означает «доля выше него».
uniform vec4 full_color : source_color = vec4(0.45, 0.85, 0.4, 1.0);
uniform vec4 hurt_color : source_color = vec4(0.95, 0.75, 0.25, 1.0);
uniform vec4 low_color : source_color = vec4(0.9, 0.3, 0.25, 1.0);
uniform float hurt_below : hint_range(0.0, 1.0) = 0.6;
uniform float low_below : hint_range(0.0, 1.0) = 0.3;

// Наложение поверх полосы: рамка, штриховка, скругление. Кладётся на всю полосу целиком,
// включая недостающую часть прочности, поэтому рамка остаётся целой при любой доле.
// Незаданное изображение считается полностью прозрачным и полосу не меняет.
uniform sampler2D overlay : source_color, hint_default_transparent, filter_linear_mipmap;

// Доля прочности берётся из данных экземпляра. Через varying, а не прямым обращением
// в fragment: данные экземпляра доступны только вершинной части.
varying flat float ratio;

void vertex() {
	ratio = INSTANCE_CUSTOM.x;
}

void fragment() {
	vec4 fill = ratio > hurt_below ? full_color : (ratio > low_below ? hurt_color : low_color);
	vec4 base = UV.x < ratio ? fill : back_color;
	vec4 over = texture(overlay, UV);

	// Наложение кладётся поверх полосы обычным смешиванием: цвета сводятся с учётом
	// собственной прозрачности каждого слоя, иначе полупрозрачная рамка гасила бы заливку
	float alpha = over.a + base.a * (1.0 - over.a);
	vec3 rgb = alpha > 0.0
		? (over.rgb * over.a + base.rgb * base.a * (1.0 - over.a)) / alpha
		: vec3(0.0);

	COLOR = vec4(rgb, alpha);
}
