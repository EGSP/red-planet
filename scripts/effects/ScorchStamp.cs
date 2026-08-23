using Godot;

/// <summary>
/// Объявление отметины внутри сцены эффекта: «этот взрыв оставляет на земле копоть такого
/// набора и такого размера».
///
/// СОБСТВЕННОЙ ЛОГИКИ НЕТ, как и у прочих узлов эффектов. Узел не ставит отпечатка и
/// не знает, по какому поводу проигрывается эффект; ставит отпечаток
/// <see cref="EffectSystem"/> в тот же миг, когда запускает частицы, а хранит его
/// <see cref="ScorchField"/>. Отсюда общее свойство: отметину оставляет любой эффект,
/// в котором художник положил этот узел, — гибель юнита, разрушение постройки, попадание
/// фугасного снаряда, — и <see cref="EffectSystem"/> не знает про копоть ничего сверх
/// одного вызова.
///
/// ПРЕДПРОСМОТР В РЕДАКТОРЕ. Узел рисует выбранное изображение в натуральную величину,
/// пока сцена открыта в редакторе: размер пятна подбирается только на глаз, а на глаз его
/// нужно видеть рядом с частицами. В игре <see cref="_Draw"/> не рисует ничего.
///
/// Отпечаток ложится на слой <see cref="WorldLayer.GroundEffects"/>, то есть под юнитами
/// и постройками, а сам этот узел лежит в сцене эффекта, которая живёт выше. Совпадения
/// порядка отрисовки между предпросмотром и игрой поэтому нет и быть не может.
/// </summary>
[Tool, GlobalClass]
public partial class ScorchStamp : Node2D
{
    private ScorchDecal _decal;
    private float _size = 128f;
    private float _sizeRandom = 0.25f;
    private bool _showGizmo = true;

    /// <summary>Набор отметин. Не назначен — эффект следа не оставляет.</summary>
    [Export]
    public ScorchDecal Decal
    {
        get => _decal;
        set { _decal = value; Refresh(); }
    }

    /// <summary>
    /// Поперечник отпечатка в пикселях мира. Клетка застройки — 64 пикселя
    /// (<see cref="Const.Unit"/>), поэтому 128 означает пятно шириной в две клетки.
    /// </summary>
    [Export(PropertyHint.Range, "8,1024,1,or_greater")]
    public float Size
    {
        get => _size;
        set { _size = value; Refresh(); }
    }

    /// <summary>
    /// Разброс размера долей от <see cref="Size"/>. Четверть означает, что отпечаток
    /// выходит шириной от 0.75 до 1.25 заданного поперечника. Нужен затем, что след
    /// от двух одинаковых взрывов иначе совпадает до пикселя.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.05")]
    public float SizeRandom
    {
        get => _sizeRandom;
        set { _sizeRandom = value; Refresh(); }
    }

    /// <summary>Показывать ли предпросмотр в редакторе. На игру не влияет.</summary>
    [Export]
    public bool ShowGizmo
    {
        get => _showGizmo;
        set { _showGizmo = value; Refresh(); }
    }

    /// <summary>Наименьший поперечник отпечатка с учётом разброса.</summary>
    public float MinSize => Size * (1f - Mathf.Clamp(SizeRandom, 0f, 1f));

    /// <summary>Наибольший поперечник отпечатка с учётом разброса.</summary>
    public float MaxSize => Size * (1f + Mathf.Clamp(SizeRandom, 0f, 1f));

    public override void _Draw()
    {
        if (!Engine.IsEditorHint() || !ShowGizmo)
            return;

        var texture = Pick();

        if (texture != null)
        {
            float side = Size;
            DrawTextureRect(texture, new Rect2(-side * 0.5f, -side * 0.5f, side, side), false,
                Decal.Tint);
        }

        // Границы разброса — тонкими окружностями. Изображение отметины по краям почти
        // прозрачно, и без них не видно, куда именно уходит её край
        var mark = new Color(1f, 0.55f, 0.2f, 0.55f);

        DrawArc(Vector2.Zero, MinSize * 0.5f, 0f, Mathf.Tau, 48, mark, 1.5f);

        if (!Mathf.IsEqualApprox(MinSize, MaxSize))
            DrawArc(Vector2.Zero, MaxSize * 0.5f, 0f, Mathf.Tau, 48, mark, 1.5f);
    }

    /// <summary>Первое изображение набора — то, что показывается в предпросмотре.</summary>
    private Texture2D Pick()
    {
        if (Decal?.Textures == null)
            return null;

        foreach (var texture in Decal.Textures)
            if (texture != null)
                return texture;

        return null;
    }

    private void Refresh()
    {
        if (Engine.IsEditorHint())
            QueueRedraw();
    }
}
