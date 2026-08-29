#[compute]
#version 450

// Наложение кругов обзора на поле расстояний.
//
// ЧТО СЧИТАЕТСЯ. Ячейка получает значение 0.5 + (радиус − расстояние) / (2 · range),
// обрезанное по краям диапазона: половина шкалы означает саму границу поля зрения,
// больше половины — внутреннюю сторону, меньше — внешнюю. Значение линейно по расстоянию
// в полосе шириной range вокруг границы, поэтому линейная фильтрация текстуры
// восстанавливает положение границы точнее размера ячейки.
//
// ОБЪЕДИНЕНИЕ ЕСТЬ ВЗЯТИЕ НАИБОЛЬШЕГО: расстояние до ближайшей границы объединения
// и получается наибольшим из расстояний до границ отдельных кругов. Источники
// накладываются одновременно и в неизвестном порядке, поэтому сложение идёт атомарной
// операцией. Атомарные действия определены только над целыми, отсюда и целочисленный
// формат поля: значение шкалы хранится умноженным на 65535.
//
// ПОТОК НА ЯЧЕЙКУ, СЛОЙ НА ИСТОЧНИК. Третье измерение сетки потоков нумерует источники
// партии, первые два обходят квадрат вокруг источника со стороной 2·span+1 ячеек.
// Партия составлена из источников одного радиуса, поэтому span и radius общие для всей
// партии и переданы постоянными протокола, а не считаны из памяти.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// Поле расстояний в целых. Значение шкалы умножено на 65535
layout(r32ui, set = 0, binding = 0) uniform restrict uimage2D field;

// Середины источников в ячейках поля, по два числа на источник, партии подряд
layout(set = 1, binding = 0, std430) restrict readonly buffer Sources {
    vec2 at[];
} sources;

layout(push_constant, std430) uniform Params {
    int width;      // сторона поля в ячейках
    int first;      // номер первого источника партии
    int count;      // сколько источников в партии
    int span;       // полуширина квадрата в ячейках
    float radius;   // радиус обзора в ячейках
    float range;    // полоса линейности в ячейках
    float pad0;
    float pad1;
} params;

void main() {
    int index = int(gl_GlobalInvocationID.z);

    if (index >= params.count)
        return;

    int side = 2 * params.span + 1;

    if (gl_GlobalInvocationID.x >= uint(side) || gl_GlobalInvocationID.y >= uint(side))
        return;

    vec2 at = sources.at[params.first + index];

    // Квадрат обхода привязан к ячейке источника, а расстояние меряется до его настоящего
    // места: заготовок круга здесь нет, и округлять середину к сетке ячеек незачем
    ivec2 cell = ivec2(floor(at)) + ivec2(gl_GlobalInvocationID.xy) - ivec2(params.span);

    if (cell.x < 0 || cell.y < 0 || cell.x >= params.width || cell.y >= params.width)
        return;

    float distance = length(vec2(cell) + vec2(0.5) - at);
    float level = 0.5 + (params.radius - distance) / (2.0 * params.range);

    if (level <= 0.0)
        return;

    imageAtomicMax(field, cell, uint(min(level, 1.0) * 65535.0));
}
