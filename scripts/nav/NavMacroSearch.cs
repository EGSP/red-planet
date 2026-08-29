using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Обратный поиск по графу областей: расстояния от цели до каждой области и полоса,
/// которой ограничивается подробный поиск.
///
/// ПОЧЕМУ ОТ ЦЕЛИ, А НЕ ОТ СТАРТА. Расстояния, посчитанные от цели, годятся любому, кто идёт
/// туда же, поэтому группа с общим приказом оплачивает верхний уровень один раз. Приём взят
/// из описания навигации Factorio и совпадает по смыслу с векторным полем, только считается
/// по нескольким тысячам областей, а не по сотням тысяч ячеек.
///
/// ОЦЕНКА, А НЕ МАРШРУТ. Граф игнорирует временную маску непроходимости и может утверждать
/// проходимость там, где здание уже поставлено, но снимок ещё не опубликован. Поэтому
/// результат используется только как ограничение области подробного поиска, а проходимость
/// каждой ячейки проверяется по растру заново. Если в полосе пути не нашлось, поиск
/// повторяется без ограничения.
/// </summary>
public sealed class NavMacroSearch
{
    private readonly NavHeap _open = new(256);

    private NavRegionLayer _layer;
    private int _goal;

    private int[] _distance = Array.Empty<int>();
    private int[] _band = Array.Empty<int>();

    /// <summary>Номер прогона полосы: запись с чужим номером считается пустой.</summary>
    private int _run;

    private readonly List<int> _chain = new();
    private readonly List<int> _members = new();

    /// <summary>Сколько областей вошло в последнюю полосу. Показывает панель отладки.</summary>
    public int LastBand { get; private set; }

    /// <summary>Цепочка спуска от области старта к области цели. Читает отладочная отрисовка.</summary>
    public IReadOnlyList<int> Chain => _chain;

    /// <summary>Все области полосы, включая соседей цепочки. Читает отладочная отрисовка.</summary>
    public IReadOnlyList<int> Members => _members;

    /// <summary>Середина области в мировых координатах. Нужна отрисовке цепочки.</summary>
    public Vector2 CenterOf(int region) =>
        _layer != null && (uint)region < (uint)_layer.Centers.Length
            ? _layer.Centers[region]
            : Vector2.Zero;

    /// <summary>Готовы ли расстояния для этого слоя и этой цели.</summary>
    public bool Matches(NavRegionLayer layer, int goalRegion) =>
        ReferenceEquals(_layer, layer) && _goal == goalRegion && goalRegion != 0;

    /// <summary>
    /// Посчитать расстояния от области цели до всех остальных. Дейкстра по спискам
    /// смежности: областей тысячи, поэтому полный обход дешевле любой избирательности.
    /// </summary>
    public void Build(NavRegionLayer layer, int goalRegion)
    {
        _layer = layer;
        _goal = goalRegion;

        int count = layer.Count;

        if (_distance.Length < count + 1)
        {
            _distance = new int[count + 1];
            _band = new int[count + 1];
            _run = 0;
        }

        for (int i = 0; i <= count; i++)
            _distance[i] = int.MaxValue;

        if (goalRegion == 0)
            return;

        _distance[goalRegion] = 0;
        _open.Clear();
        _open.Push(goalRegion, 0);

        while (_open.Count > 0)
        {
            int at = _open.Pop();
            int cost = _distance[at];

            for (int edge = layer.EdgeStart[at]; edge < layer.EdgeStart[at + 1]; edge++)
            {
                int next = layer.EdgeTarget[edge];
                int candidate = cost + layer.EdgeCost[edge];

                if (candidate >= _distance[next])
                    continue;

                _distance[next] = candidate;
                _open.Push(next, candidate);
            }
        }
    }

    public int Distance(int region) =>
        (uint)region < (uint)_distance.Length ? _distance[region] : int.MaxValue;

    /// <summary>
    /// Собрать полосу: спуск от области старта к области цели по убыванию расстояния.
    /// Возвращает false, когда спуск невозможен, — тогда ограничивать поиск нечем.
    ///
    /// В полосу входят не только области спуска, но и их соседи. Соседство добавлено
    /// потому, что кратчайшая ломаная по ячейкам вправе срезать угол через область,
    /// в спуск не попавшую, а расширение на один шаг стоит десятка узлов графа.
    /// </summary>
    public bool BuildBand(int startRegion)
    {
        LastBand = 0;
        _chain.Clear();
        _members.Clear();

        if (_layer == null || startRegion == 0 || _goal == 0)
            return false;

        if (Distance(startRegion) == int.MaxValue)
            return false;

        _run++;

        int at = startRegion;
        int guard = _layer.Count + 2;

        _chain.Add(at);
        Include(at);

        while (at != _goal && guard-- > 0)
        {
            int best = 0;
            int bestDistance = Distance(at);

            for (int edge = _layer.EdgeStart[at]; edge < _layer.EdgeStart[at + 1]; edge++)
            {
                int next = _layer.EdgeTarget[edge];
                int distance = Distance(next);

                if (distance >= bestDistance)
                    continue;

                best = next;
                bestDistance = distance;
            }

            if (best == 0)
                return false;

            at = best;
            _chain.Add(at);
            Include(at);
        }

        return at == _goal;
    }

    /// <summary>Входит ли область в полосу последнего спуска.</summary>
    public bool InBand(int region) =>
        (uint)region < (uint)_band.Length && _band[region] == _run;

    private void Include(int region)
    {
        Mark(region);

        for (int edge = _layer.EdgeStart[region]; edge < _layer.EdgeStart[region + 1]; edge++)
            Mark(_layer.EdgeTarget[edge]);
    }

    private void Mark(int region)
    {
        if ((uint)region >= (uint)_band.Length || _band[region] == _run)
            return;

        _band[region] = _run;
        _members.Add(region);
        LastBand++;
    }
}
