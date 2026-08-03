using Godot;

/// <summary>Камера: WASD или перетаскивание средней кнопкой, зум колесом.</summary>
public partial class CameraRig : Camera2D
{
    [Export] public float PanSpeed = 700f;
    [Export] public float ZoomStep = 1.12f;
    [Export] public float ZoomMin = 0.25f;
    [Export] public float ZoomMax = 2.5f;

    private bool _dragging;

    public override void _Ready() => MakeCurrent();

    public override void _Process(double dt)
    {
        var dir = Vector2.Zero;

        if (Input.IsKeyPressed(Key.W)) dir.Y -= 1f;
        if (Input.IsKeyPressed(Key.S)) dir.Y += 1f;
        if (Input.IsKeyPressed(Key.A)) dir.X -= 1f;
        if (Input.IsKeyPressed(Key.D)) dir.X += 1f;

        if (dir != Vector2.Zero)
            Position += dir.Normalized() * PanSpeed * (float)dt / Zoom.X;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouse)
        {
            switch (mouse.ButtonIndex)
            {
                case MouseButton.WheelUp when mouse.Pressed:
                    ApplyZoom(ZoomStep);
                    break;

                case MouseButton.WheelDown when mouse.Pressed:
                    ApplyZoom(1f / ZoomStep);
                    break;

                case MouseButton.Middle:
                    _dragging = mouse.Pressed;
                    break;
            }
        }

        if (@event is InputEventMouseMotion motion && _dragging)
            Position -= motion.Relative / Zoom;
    }

    private void ApplyZoom(float factor)
    {
        float value = Mathf.Clamp(Zoom.X * factor, ZoomMin, ZoomMax);

        if (Mathf.IsEqualApprox(value, Zoom.X))
            return;

        Zoom = new Vector2(value, value);
        ShapeDraw.NotifyZoomChanged(GetTree());
    }
}
