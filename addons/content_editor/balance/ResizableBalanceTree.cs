using System;
using Godot;

/// <summary>
/// Таблица баланса с изменяемой шириной колонок.
///
/// ЗАЧЕМ СВОЙ КЛАСС. У <see cref="Tree"/> в Godot 4 нет встроенного изменения ширины
/// колонок мышью: ширина задаётся только программно. Здесь перетаскивание разделителя
/// в полосе заголовков меняет <c>CustomMinimumWidth</c> колонки и по окончании сообщает
/// об этом наружу, чтобы вторая таблица получила ту же ширину, а снимок — новое значение.
///
/// ПОЧЕМУ ПРИЗНАК ПЕРЕТАСКИВАНИЯ ВИДЕН СНАРУЖИ. Отпускание кнопки над заголовком Godot
/// сообщает и как щелчок по заголовку, то есть как требование сменить сортировку.
/// Признак позволяет отличить конец перетаскивания от щелчка.
/// </summary>
[Tool]
public partial class ResizableBalanceTree : Tree
{
    /// <summary>Полуширина полосы захвата разделителя, пикселей.</summary>
    private const float SeparatorHit = 6f;

    /// <summary>Наименьшая ширина колонки: более узкая перестаёт быть схватываемой мышью.</summary>
    public const int MinimumColumnWidth = 36;

    private int _dragColumn = -1;
    private float _dragStartX;
    private int _dragStartWidth;

    /// <summary>Ширина колонок изменена перетаскиванием и требует согласования.</summary>
    public event Action<ResizableBalanceTree> ColumnWidthsChanged;

    /// <summary>Идёт ли перетаскивание разделителя прямо сейчас.</summary>
    public bool IsResizingColumns => _dragColumn >= 0;

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouse && mouse.ButtonIndex == MouseButton.Left)
        {
            if (mouse.Pressed && SeparatorAt(mouse.Position) is var column and >= 0)
            {
                _dragColumn = column;
                _dragStartX = mouse.Position.X;
                _dragStartWidth = GetColumnWidth(column);
                MouseDefaultCursorShape = CursorShape.Hsize;
                AcceptEvent();
                return;
            }

            if (!mouse.Pressed && _dragColumn >= 0)
            {
                _dragColumn = -1;
                MouseDefaultCursorShape = CursorShape.Arrow;
                ColumnWidthsChanged?.Invoke(this);
                AcceptEvent();
                return;
            }
        }
        else if (@event is InputEventMouseMotion motion)
        {
            if (_dragColumn >= 0)
            {
                int width = Mathf.Max(
                    MinimumColumnWidth,
                    _dragStartWidth + (int)(motion.Position.X - _dragStartX));
                SetColumnExpand(_dragColumn, false);
                SetColumnCustomMinimumWidth(_dragColumn, width);
                AcceptEvent();
                return;
            }

            MouseDefaultCursorShape = SeparatorAt(motion.Position) >= 0
                ? CursorShape.Hsize
                : CursorShape.Arrow;
        }

        base._GuiInput(@event);
    }

    /// <summary>Номер колонки, у правой границы которой находится указатель, либо −1.</summary>
    private int SeparatorAt(Vector2 position)
    {
        float titleHeight = GetThemeConstant("title_button_height");
        if (titleHeight <= 0f)
            titleHeight = 24f;
        if (position.Y < 0f || position.Y > titleHeight)
            return -1;

        float x = 0f;
        for (int i = 0; i < Columns; i++)
        {
            x += GetColumnWidth(i);
            if (Mathf.Abs(position.X - x) <= SeparatorHit)
                return i;
        }

        return -1;
    }
}
