shader_type canvas_item;
render_mode blend_add, unshaded;

// Мягкость краёв луча поперёк его длины.
uniform float edge_softness : hint_range(0.0, 1.0) = 0.45;

// Затухание у начала и конца луча.
uniform float end_fade : hint_range(0.0, 0.5) = 0.05;

// Трансформация текстуры.
uniform bool flip_x = false;
uniform bool flip_y = false;
uniform bool rotate_90 = false;

varying flat vec2 tiling;
varying vec4 instance_color;


void vertex() {
	// x — количество повторов текстуры вдоль луча.
	// y — смещение рисунка вдоль луча.
	tiling = INSTANCE_CUSTOM.xy;

	// Сохраняем цвет экземпляра отдельно, чтобы не умножать
	// результат на исходную TEXTURE повторно.
	instance_color = COLOR;
}


void fragment() {
	// Получаем координаты внутри текущего тайла.
	// Благодаря fract() альфа и цвет текстуры повторяются вместе.
	vec2 tile_uv = vec2(
		fract(UV.x * tiling.x + tiling.y),
		UV.y
	);

	// Поворачиваем текстуру внутри каждого отдельного тайла.
	vec2 uv = mix(
		tile_uv,
		vec2(tile_uv.y, 1.0 - tile_uv.x),
		float(rotate_90)
	);

	// Отражение текстуры.
	uv.x = mix(uv.x, 1.0 - uv.x, float(flip_x));
	uv.y = mix(uv.y, 1.0 - uv.y, float(flip_y));

	// Выбираем только преобразованную и тайловую текстуру.
	vec4 tex = texture(TEXTURE, uv);

	// Сглаживание краёв луча поперёк его длины. Пороги разведены хотя бы на малую величину:
	// smoothstep с совпавшими порогами объявлен неопределённым, и при нулевой мягкости
	// разные драйверы возвращают разное.
	float across = abs(UV.y - 0.5) * 2.0;
	float softness = max(edge_softness, 0.0001);
	float edge = 1.0 - smoothstep(
		1.0 - softness,
		1.0,
		across
	);

	// Затухание у начала и конца луча.
	float fade = max(end_fade, 0.0001);
	float along = smoothstep(
		0.0,
		fade,
		UV.x
	) * (1.0 - smoothstep(
		1.0 - fade,
		1.0,
		UV.x
	));

	// Цвет экземпляра × текстура.
	// Альфа текстуры также применяется здесь.
	COLOR = instance_color * tex;

	// Дополнительные геометрические маски альфы.
	COLOR.a *= edge * along;
}