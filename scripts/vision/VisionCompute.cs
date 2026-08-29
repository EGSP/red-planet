using System;
using Godot;

/// <summary>
/// Поле расстояний до границы обзора, вычисляемое на видеокарте.
///
/// ЗАЧЕМ НА ВИДЕОКАРТЕ. Прежде поле жило в массиве байтов и обходилось процессором трижды:
/// очистка с наложением кругов при пересборке, смещение показываемого поля к собранному
/// каждый кадр и выгрузка в текстуру тоже каждый кадр. Два последних прохода шли по всему
/// растру независимо от того, изменилось ли в нём хоть что-нибудь, и при четверти миллиона
/// ячеек обходились до восьмой части времени кадра. Здесь все три прохода делают
/// вычислительные шейдеры, а результат остаётся в памяти видеокарты и попадает в шейдер
/// тумана без обратного чтения.
///
/// ПОЛЕЙ ДВА. Собранное поле хранит цель — расстояния по нынешним положениям источников;
/// показываемое догоняет его и попадает в отрисовку. Разделение то же, что было
/// и на процессоре, и по той же причине: пересборка идёт реже вывода кадров, поэтому
/// без сглаживания граница двигалась бы ступенями.
///
/// ФОРМАТЫ РАЗНЫЕ, И РАЗЛИЧИЕ СУЩЕСТВЕННО. Собранное поле целочисленное, поскольку
/// объединение кругов есть взятие наибольшего, а атомарные операции определены только
/// над целыми. Показываемое — вещественное с выборкой линейной фильтрацией: именно
/// по промежуточным значениям шейдер тумана находит границу точнее размера ячейки.
///
/// РАБОТА ИДЁТ НА ПОТОКЕ ОТРИСОВКИ. Godot по умолчанию ведёт отрисовку отдельным потоком,
/// и обращаться к общему <see cref="RenderingDevice"/> с главного потока нельзя. Поэтому
/// каждое действие ставится в очередь через <see cref="RenderingServer.CallOnRenderThread"/>,
/// а данные передаются копией: главный поток к моменту исполнения занят следующим кадром.
/// </summary>
public sealed class VisionCompute
{
    /// <summary>Наибольшее число источников, которое вмещает буфер. Излишек отбрасывается.</summary>
    public const int MaxSources = 8192;

    private const string StampPath = "res://resources/shaders/vision_stamp.glsl";
    private const string SmoothPath = "res://resources/shaders/vision_smooth.glsl";

    /// <summary>Текстура показываемого поля для шейдера тумана. Её RID ставит поток отрисовки.</summary>
    public Texture2Drd Texture { get; } = new();

    /// <summary>Готово ли поле к показу. Пока нет — туман не рисуется вовсе.</summary>
    public bool Ready => _shown.IsValid;

    /// <summary>Сторона поля в ячейках, какой её создал поток отрисовки.</summary>
    public int Width => _width;

    private RenderingDevice _rd;

    private Rid _stampShader;
    private Rid _stampPipeline;
    private Rid _smoothShader;
    private Rid _smoothPipeline;

    private Rid _field;
    private Rid _shown;
    private Rid _sources;

    private Rid _fieldForStamp;
    private Rid _sourcesSet;
    private Rid _fieldForSmooth;
    private Rid _shownSet;

    private int _width;
    private bool _broken;

    /// <summary>
    /// Постоянные протокола собираются сюда, чтобы не выделять память на каждый вызов.
    /// Длина у каждого шейдера своя и должна совпадать с объявленной в нём точно: несовпадение
    /// видеокарта считает ошибкой и запуск отменяет.
    /// </summary>
    private readonly byte[] _push = new byte[32];

    private readonly byte[] _pushSmooth = new byte[16];

    /// <summary>
    /// Пересобрать поле: положить источники и наложить круги.
    ///
    /// Массив positions хранит середины источников в ячейках поля, по два числа на источник,
    /// партиями одного радиуса подряд. Массив groups описывает партии по четыре числа: номер
    /// первого источника, длина партии, полуширина квадрата в ячейках и радиус в ячейках.
    ///
    /// Массивы передаются потоку отрисовки как есть и повторно не используются: главный поток
    /// к моменту исполнения ушёл дальше, и правка на месте испортила бы содержимое прямо
    /// во время чтения.
    /// </summary>
    public void Rebuild(int width, byte[] positions, float[] groups, float range) =>
        RenderingServer.CallOnRenderThread(
            Callable.From(() => OnRebuild(width, positions, groups, range)));

    /// <summary>Сместить показываемое поле к собранному на долю оставшейся разницы.</summary>
    public void Smooth(int width, float part) =>
        RenderingServer.CallOnRenderThread(Callable.From(() => OnSmooth(width, part)));

    /// <summary>Освободить ресурсы видеокарты. Зовётся при сносе системы зрения.</summary>
    public void Release()
    {
        Texture.TextureRdRid = new Rid();
        RenderingServer.CallOnRenderThread(Callable.From(OnRelease));
    }

    /// <summary>
    /// Отдать текстуре RID показываемого поля. Зовётся с главного потока, поскольку ресурс
    /// читает отрисовка сцены: поток отрисовки лишь создаёт поле и оставляет RID в поле класса.
    /// </summary>
    public void Publish()
    {
        if (Texture.TextureRdRid != _shown)
            Texture.TextureRdRid = _shown;
    }

    // ── дальше всё исполняется на потоке отрисовки ────────────────────────────────

    private void OnRebuild(int width, byte[] positions, float[] groups, float range)
    {
        if (!Ensure(width))
            return;

        if (positions.Length > 0)
            _rd.BufferUpdate(_sources, 0, (uint)positions.Length, positions);

        // Взятие наибольшего не отменяет прежнего значения, поэтому поле очищается целиком:
        // ушедший источник иначе оставил бы за собой просматриваемую область навсегда
        _rd.TextureClear(_field, new Color(0f, 0f, 0f, 0f), 0, 1, 0, 1);

        if (groups.Length < 4)
            return;

        long list = _rd.ComputeListBegin();

        _rd.ComputeListBindComputePipeline(list, _stampPipeline);
        _rd.ComputeListBindUniformSet(list, _fieldForStamp, 0);
        _rd.ComputeListBindUniformSet(list, _sourcesSet, 1);

        // Партия на каждый радиус: полуширина квадрата обхода зависит от радиуса, а один
        // запуск задаёт её всем своим потокам сразу. Радиусы приходят из справочников,
        // поэтому партий выходит единицы, сколько бы источников ни было на карте
        for (int i = 0; i + 3 < groups.Length; i += 4)
        {
            int first = (int)groups[i];
            int count = (int)groups[i + 1];
            int half = (int)groups[i + 2];
            float radius = groups[i + 3];

            if (count <= 0)
                continue;

            Push(width, first, count, half, radius, range);
            _rd.ComputeListSetPushConstant(list, _push, (uint)_push.Length);

            uint side = (uint)(2 * half / 8 + 1);
            _rd.ComputeListDispatch(list, side, side, (uint)count);
        }

        _rd.ComputeListEnd();
    }

    private void OnSmooth(int width, float part)
    {
        if (!Ensure(width))
            return;

        Push(width, part);

        long list = _rd.ComputeListBegin();

        _rd.ComputeListBindComputePipeline(list, _smoothPipeline);
        _rd.ComputeListBindUniformSet(list, _fieldForSmooth, 0);
        _rd.ComputeListBindUniformSet(list, _shownSet, 1);
        _rd.ComputeListSetPushConstant(list, _pushSmooth, (uint)_pushSmooth.Length);

        uint groups = (uint)((width - 1) / 8 + 1);
        _rd.ComputeListDispatch(list, groups, groups, 1);

        _rd.ComputeListEnd();
    }

    /// <summary>
    /// Завести всё, чего ещё нет, и подогнать поля под указанную сторону. Возвращает false,
    /// если шейдеры не собрались: тогда система зрения молча ничего не делает, а не сообщает
    /// об ошибке на каждом кадре.
    /// </summary>
    private bool Ensure(int width)
    {
        if (_broken)
            return false;

        if (_rd == null && !Setup())
            return false;

        if (_width == width && _field.IsValid)
            return true;

        FreeFields();

        _width = Mathf.Max(1, width);
        _field = MakeTexture(_width, RenderingDevice.DataFormat.R32Uint, false);
        _shown = MakeTexture(_width, RenderingDevice.DataFormat.R32Sfloat, true);

        _fieldForStamp = ImageSet(_field, _stampShader, 0);
        _fieldForSmooth = ImageSet(_field, _smoothShader, 0);
        _shownSet = ImageSet(_shown, _smoothShader, 1);

        return true;
    }

    private bool Setup()
    {
        _rd = RenderingServer.GetRenderingDevice();

        if (_rd == null)
        {
            _broken = true;
            GD.PushError("[зрение] общий RenderingDevice недоступен: поле расстояний не считается");
            return false;
        }

        if (!Load(StampPath, out _stampShader) || !Load(SmoothPath, out _smoothShader))
        {
            _broken = true;
            return false;
        }

        _stampPipeline = _rd.ComputePipelineCreate(_stampShader);
        _smoothPipeline = _rd.ComputePipelineCreate(_smoothShader);

        // Буфер заводится сразу наибольшего размера: длина хранилища задана при создании,
        // а пересоздавать его на каждом изменении численности войск незачем
        _sources = _rd.StorageBufferCreate(MaxSources * 2 * sizeof(float));
        _sourcesSet = BufferSet(_sources, _stampShader, 1);

        return true;
    }

    private bool Load(string path, out Rid shader)
    {
        shader = new Rid();

        var file = GD.Load<RDShaderFile>(path);

        if (file == null)
        {
            GD.PushError($"[зрение] шейдер {path} не загрузился");
            return false;
        }

        var spirv = file.GetSpirV();
        string error = spirv.GetStageCompileError(RenderingDevice.ShaderStage.Compute);

        if (!string.IsNullOrEmpty(error))
        {
            GD.PushError($"[зрение] шейдер {path} не собрался: {error}");
            return false;
        }

        shader = _rd.ShaderCreateFromSpirV(spirv);
        return shader.IsValid;
    }

    private Rid MakeTexture(int width, RenderingDevice.DataFormat format, bool sampled)
    {
        var usage = RenderingDevice.TextureUsageBits.StorageBit
                  | RenderingDevice.TextureUsageBits.CanCopyToBit;

        if (sampled)
            usage |= RenderingDevice.TextureUsageBits.SamplingBit;

        var shape = new RDTextureFormat
        {
            Format = format,
            TextureType = RenderingDevice.TextureType.Type2D,
            Width = (uint)width,
            Height = (uint)width,
            Depth = 1,
            ArrayLayers = 1,
            Mipmaps = 1,
            UsageBits = usage,
        };

        var texture = _rd.TextureCreate(shape, new RDTextureView());
        _rd.TextureClear(texture, new Color(0f, 0f, 0f, 0f), 0, 1, 0, 1);

        return texture;
    }

    private Rid ImageSet(Rid texture, Rid shader, uint set)
    {
        var uniform = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.Image,
            Binding = 0,
        };

        uniform.AddId(texture);

        return _rd.UniformSetCreate([uniform], shader, set);
    }

    private Rid BufferSet(Rid buffer, Rid shader, uint set)
    {
        var uniform = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.StorageBuffer,
            Binding = 0,
        };

        uniform.AddId(buffer);

        return _rd.UniformSetCreate([uniform], shader, set);
    }

    private void Push(int width, int first, int count, int half, float radius, float range)
    {
        var at = _push.AsSpan();

        BitConverter.TryWriteBytes(at, width);
        BitConverter.TryWriteBytes(at[4..], first);
        BitConverter.TryWriteBytes(at[8..], count);
        BitConverter.TryWriteBytes(at[12..], half);
        BitConverter.TryWriteBytes(at[16..], radius);
        BitConverter.TryWriteBytes(at[20..], range);
        BitConverter.TryWriteBytes(at[24..], 0f);
        BitConverter.TryWriteBytes(at[28..], 0f);
    }

    private void Push(int width, float part)
    {
        var at = _pushSmooth.AsSpan();

        BitConverter.TryWriteBytes(at, width);
        BitConverter.TryWriteBytes(at[4..], part);
        BitConverter.TryWriteBytes(at[8..], 0f);
        BitConverter.TryWriteBytes(at[12..], 0f);
    }

    private void FreeFields()
    {
        Free(ref _fieldForStamp);
        Free(ref _fieldForSmooth);
        Free(ref _shownSet);
        Free(ref _field);
        Free(ref _shown);
    }

    private void OnRelease()
    {
        if (_rd == null)
            return;

        FreeFields();
        Free(ref _sourcesSet);
        Free(ref _sources);
        Free(ref _stampPipeline);
        Free(ref _smoothPipeline);
        Free(ref _stampShader);
        Free(ref _smoothShader);

        _rd = null;
        _width = 0;
    }

    private void Free(ref Rid rid)
    {
        if (rid.IsValid)
            _rd.FreeRid(rid);

        rid = new Rid();
    }
}
