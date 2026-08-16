using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Кэш иконок каталога и таблицы баланса. Силуэт тот же, что у <see cref="UnitIcon"/>:
/// <see cref="UnitSilhouette"/> вписывается в квадрат. Текстура печётся через
/// однокадровый SubViewport, чтобы Tree и ItemList могли поставить SetIcon.
/// </summary>
[Tool]
public partial class ContentEditorIconCache : Control
{
    public const int PixelSize = 28;

    private readonly Dictionary<string, Texture2D> _baked = new(StringComparer.Ordinal);
    private readonly Queue<UnitDefinition> _queue = new();
    private readonly HashSet<string> _queued = new(StringComparer.Ordinal);
    private SubViewport _viewport;
    private UnitIcon _icon;
    private string _capturingId;
    private int _captureWait;

    public event Action IconsReady;

    public ContentEditorIconCache()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = Vector2.Zero;
    }

    public Texture2D GetUnit(UnitDefinition def)
    {
        if (def == null || string.IsNullOrEmpty(def.Id))
            return null;

        if (_baked.TryGetValue(def.Id, out var texture))
            return texture;

        _baked[def.Id] = Placeholder(def);
        Enqueue(def);
        return _baked[def.Id];
    }

    public Texture2D GetSprite(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        string key = "sprite:" + path;
        if (_baked.TryGetValue(key, out var texture))
            return texture;

        var loaded = ResourceLoader.Load<Texture2D>(path);
        if (loaded != null)
            _baked[key] = loaded;
        return loaded;
    }

    public override void _Ready()
    {
        EnsureViewport();
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (!IsInsideTree())
            return;

        EnsureViewport();

        if (_capturingId != null)
        {
            if (_captureWait > 0)
            {
                _captureWait--;
                return;
            }

            Capture();
            return;
        }

        if (_queue.Count == 0)
            return;

        var def = _queue.Dequeue();
        _queued.Remove(def.Id);
        _icon.Definition = def;
        _icon.QueueRedraw();
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        _capturingId = def.Id;
        _captureWait = 1;
    }

    private void Enqueue(UnitDefinition def)
    {
        if (!_queued.Add(def.Id))
            return;

        _queue.Enqueue(def);
        SetProcess(true);
    }

    private void Capture()
    {
        string id = _capturingId;
        _capturingId = null;
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

        var image = _viewport.GetTexture()?.GetImage();
        if (image != null && !string.IsNullOrEmpty(id))
            _baked[id] = ImageTexture.CreateFromImage(image);

        IconsReady?.Invoke();
        if (_queue.Count == 0)
            SetProcess(false);
    }

    private void EnsureViewport()
    {
        if (_viewport != null)
            return;

        _viewport = new SubViewport
        {
            Size = new Vector2I(PixelSize, PixelSize),
            TransparentBg = true,
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        AddChild(_viewport);

        _icon = new UnitIcon
        {
            CustomMinimumSize = new Vector2(PixelSize, PixelSize),
            Size = new Vector2(PixelSize, PixelSize),
        };
        _viewport.AddChild(_icon);
        _icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    private static Texture2D Placeholder(UnitDefinition def)
    {
        var image = Image.CreateEmpty(PixelSize, PixelSize, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        var color = def.Color;
        color.A = 1f;
        int inset = 4;
        for (int y = inset; y < PixelSize - inset; y++)
        {
            for (int x = inset; x < PixelSize - inset; x++)
            {
                float dx = x + 0.5f - PixelSize * 0.5f;
                float dy = y + 0.5f - PixelSize * 0.5f;
                if (dx * dx + dy * dy <= (PixelSize * 0.5f - inset) * (PixelSize * 0.5f - inset))
                    image.SetPixel(x, y, color);
            }
        }

        return ImageTexture.CreateFromImage(image);
    }
}
