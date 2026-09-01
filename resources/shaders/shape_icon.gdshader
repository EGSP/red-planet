shader_type canvas_item;
render_mode blend_mix, unshaded;

// Значок приказа. Все значки лежат в одном массиве изображений, собранном при запуске
// из отдельных файлов, поэтому вид приказа выбирается номером слоя, а не сменой текстуры:
// смена текстуры принадлежит узлу и разбила бы сетку на отдельные вызовы отрисовки.
//
// ЧТО В ДАННЫХ ЭКЗЕМПЛЯРА: x — номер слоя. Остальные поля свободны.
//
// Цвет экземпляра умножается на изображение, поэтому картинки рисуются белыми
// с прозрачностью, а оттенок вида приказа приходит из DrawTheme.

uniform sampler2DArray icons : source_color, filter_linear_mipmap;

varying flat float layer;

void vertex() {
	layer = INSTANCE_CUSTOM.x;
}

void fragment() {
	COLOR *= texture(icons, vec3(UV, layer));
}
