using System;

/// <summary>
/// Двоичная куча минимума. Своя, потому что System.Collections.Generic.PriorityQueue
/// не даёт переиспользовать хранилище между поисками, а поисков за кадр несколько.
/// </summary>
internal sealed class NavHeap
{
    private int[] _items;
    private int[] _keys;

    public int Count { get; private set; }

    public NavHeap(int capacity)
    {
        _items = new int[capacity];
        _keys = new int[capacity];
    }

    public void Clear() => Count = 0;

    public void Push(int item, int key)
    {
        if (Count == _items.Length)
        {
            Array.Resize(ref _items, Count * 2);
            Array.Resize(ref _keys, Count * 2);
        }

        int at = Count++;
        _items[at] = item;
        _keys[at] = key;

        while (at > 0)
        {
            int parent = (at - 1) / 2;

            if (_keys[parent] <= _keys[at])
                break;

            Swap(parent, at);
            at = parent;
        }
    }

    public int Pop()
    {
        int top = _items[0];

        Count--;
        _items[0] = _items[Count];
        _keys[0] = _keys[Count];

        int at = 0;

        while (true)
        {
            int left = at * 2 + 1;
            int right = left + 1;
            int least = at;

            if (left < Count && _keys[left] < _keys[least])
                least = left;

            if (right < Count && _keys[right] < _keys[least])
                least = right;

            if (least == at)
                break;

            Swap(least, at);
            at = least;
        }

        return top;
    }

    private void Swap(int a, int b)
    {
        (_items[a], _items[b]) = (_items[b], _items[a]);
        (_keys[a], _keys[b]) = (_keys[b], _keys[a]);
    }
}
