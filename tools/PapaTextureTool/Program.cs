using StbImageWriteSharp;

if (!TryParseArgs(args, out var inputPaths, out var outputDir, out var error))
{
    Console.Error.WriteLine(error);
    PrintUsage();
    return 1;
}

Directory.CreateDirectory(outputDir);
var files = CollectPapaFiles(inputPaths);

if (files.Count == 0)
{
    Console.WriteLine("PAPA files not found.");
    return 0;
}

var converted = 0;
var failed = 0;
var skipped = 0;

foreach (var file in files)
{
    try
    {
        var texture = PapaTexture.Load(file.FilePath);
        var relativeWithoutExt = Path.ChangeExtension(file.RelativePath, null) ?? Path.GetFileNameWithoutExtension(file.RelativePath);
        var outputPath = Path.Combine(outputDir, relativeWithoutExt + ".png");
        var outputFolder = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputFolder))
        {
            Directory.CreateDirectory(outputFolder);
        }

        using var output = File.Create(outputPath);
        var writer = new ImageWriter();
        writer.WritePng(texture.RgbaPixels, texture.Width, texture.Height, ColorComponents.RedGreenBlueAlpha, output);
        converted++;
        Console.WriteLine($"OK  {file.FilePath} -> {outputPath}");
    }
    catch (Exception ex)
    {
        if (ex is InvalidDataException dataError &&
            dataError.Message.Contains("does not contain texture records", StringComparison.OrdinalIgnoreCase))
        {
            skipped++;
            Console.WriteLine($"SKIP {file.FilePath}: no textures");
            continue;
        }

        failed++;
        Console.Error.WriteLine($"ERR {file.FilePath}: {ex.Message}");
    }
}

Console.WriteLine($"Done. Converted: {converted}, Skipped: {skipped}, Failed: {failed}");
return failed == 0 ? 0 : 2;

static bool TryParseArgs(
    string[] args,
    out List<string> inputPaths,
    out string outputDir,
    out string error)
{
    inputPaths = new List<string>();
    outputDir = string.Empty;
    error = string.Empty;

    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        if (arg is "-o" or "--output")
        {
            if (i + 1 >= args.Length)
            {
                error = "Output directory missing after -o/--output.";
                return false;
            }

            outputDir = Path.GetFullPath(args[++i]);
            continue;
        }

        inputPaths.Add(Path.GetFullPath(arg));
    }

    if (inputPaths.Count == 0)
    {
        error = "Input file or directory is required.";
        return false;
    }

    if (string.IsNullOrWhiteSpace(outputDir))
    {
        error = "Output directory is required.";
        return false;
    }

    return true;
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  PapaTextureTool <input1> [<input2> ...] -o <output_directory>");
    Console.WriteLine();
    Console.WriteLine("Inputs can be .papa files or directories (recursive scan).");
}

static List<PapaFileTask> CollectPapaFiles(IEnumerable<string> inputPaths)
{
    var result = new List<PapaFileTask>();
    foreach (var input in inputPaths)
    {
        if (File.Exists(input))
        {
            if (Path.GetExtension(input).Equals(".papa", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new PapaFileTask(input, Path.GetFileName(input)));
            }

            continue;
        }

        if (!Directory.Exists(input))
        {
            Console.Error.WriteLine($"Skip missing path: {input}");
            continue;
        }

        foreach (var file in Directory.EnumerateFiles(input, "*.papa", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(input, file);
            result.Add(new PapaFileTask(file, relative));
        }
    }

    return result;
}

internal readonly record struct PapaFileTask(string FilePath, string RelativePath);

internal sealed class PapaTexture
{
    private const uint PapaMagic = 0x50617061; // "Papa" in little-endian order for uint32 read.
    private const int HeaderSize = 104;

    public required int Width { get; init; }
    public required int Height { get; init; }
    public required byte[] RgbaPixels { get; init; }

    public static PapaTexture Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        var magic = reader.ReadUInt32();
        if (magic != PapaMagic)
        {
            throw new InvalidDataException("Unsupported file signature.");
        }

        reader.BaseStream.Seek(6, SeekOrigin.Begin);
        var numTextures = reader.ReadUInt16();
        if (numTextures < 1)
        {
            throw new InvalidDataException("PAPA does not contain texture records.");
        }

        reader.BaseStream.Seek(40, SeekOrigin.Begin);
        var textureTableOffset = reader.ReadUInt64();
        if (textureTableOffset == ulong.MaxValue || textureTableOffset < HeaderSize)
        {
            throw new InvalidDataException("Invalid texture table offset.");
        }

        reader.BaseStream.Seek((long)textureTableOffset, SeekOrigin.Begin);
        _ = reader.ReadUInt16(); // name index
        var format = reader.ReadByte();
        _ = reader.ReadByte(); // mips + srgb bitfield
        var width = reader.ReadUInt16();
        var height = reader.ReadUInt16();
        var dataSize = reader.ReadUInt64();
        var dataOffset = reader.ReadUInt64();

        if (width == 0 || height == 0)
        {
            throw new InvalidDataException("Texture dimensions are invalid.");
        }

        if (dataOffset >= (ulong)reader.BaseStream.Length)
        {
            throw new InvalidDataException("Texture data offset is out of range.");
        }

        reader.BaseStream.Seek((long)dataOffset, SeekOrigin.Begin);
        var bytesToRead = (int)Math.Min(dataSize, (ulong)(reader.BaseStream.Length - reader.BaseStream.Position));
        var raw = reader.ReadBytes(bytesToRead);
        if (raw.Length == 0)
        {
            throw new InvalidDataException("Texture data is empty.");
        }

        var rgba = DecodeTopLevelTexture(format, width, height, raw);
        return new PapaTexture
        {
            Width = width,
            Height = height,
            RgbaPixels = rgba
        };
    }

    private static byte[] DecodeTopLevelTexture(byte format, int width, int height, byte[] raw)
    {
        return format switch
        {
            1 => DecodeR8G8B8A8(width, height, raw),
            2 => DecodeR8G8B8X8(width, height, raw),
            3 => DecodeB8G8R8A8(width, height, raw),
            4 => DecodeDxt1(width, height, raw),
            6 => DecodeDxt5(width, height, raw),
            13 => DecodeR8(width, height, raw),
            _ => throw new NotSupportedException($"Unsupported texture format code: {format}")
        };
    }

    private static byte[] DecodeR8G8B8A8(int width, int height, byte[] raw)
    {
        var expected = width * height * 4;
        if (raw.Length < expected)
        {
            throw new InvalidDataException("Unexpected data size for R8G8B8A8.");
        }

        var rgba = new byte[expected];
        Buffer.BlockCopy(raw, 0, rgba, 0, expected);
        return rgba;
    }

    private static byte[] DecodeR8G8B8X8(int width, int height, byte[] raw)
    {
        var expected = width * height * 4;
        if (raw.Length < expected)
        {
            throw new InvalidDataException("Unexpected data size for R8G8B8X8.");
        }

        var rgba = new byte[expected];
        for (var i = 0; i < expected; i += 4)
        {
            rgba[i + 0] = raw[i + 0];
            rgba[i + 1] = raw[i + 1];
            rgba[i + 2] = raw[i + 2];
            rgba[i + 3] = 255;
        }

        return rgba;
    }

    private static byte[] DecodeB8G8R8A8(int width, int height, byte[] raw)
    {
        var expected = width * height * 4;
        if (raw.Length < expected)
        {
            throw new InvalidDataException("Unexpected data size for B8G8R8A8.");
        }

        var rgba = new byte[expected];
        for (var i = 0; i < expected; i += 4)
        {
            rgba[i + 0] = raw[i + 2];
            rgba[i + 1] = raw[i + 1];
            rgba[i + 2] = raw[i + 0];
            rgba[i + 3] = raw[i + 3];
        }

        return rgba;
    }

    private static byte[] DecodeR8(int width, int height, byte[] raw)
    {
        var pixels = width * height;
        if (raw.Length < pixels)
        {
            throw new InvalidDataException("Unexpected data size for R8.");
        }

        var rgba = new byte[pixels * 4];
        for (var i = 0; i < pixels; i++)
        {
            var value = raw[i];
            var p = i * 4;
            rgba[p + 0] = value;
            rgba[p + 1] = value;
            rgba[p + 2] = value;
            rgba[p + 3] = 255;
        }

        return rgba;
    }

    private static byte[] DecodeDxt1(int width, int height, byte[] raw)
    {
        var blockWidth = (width + 3) / 4;
        var blockHeight = (height + 3) / 4;
        var required = blockWidth * blockHeight * 8;
        if (raw.Length < required)
        {
            throw new InvalidDataException("Unexpected data size for DXT1.");
        }

        var rgba = new byte[width * height * 4];
        var offset = 0;
        var palette = new Color32[4];
        for (var by = 0; by < blockHeight; by++)
        {
            for (var bx = 0; bx < blockWidth; bx++)
            {
                var c0 = BitConverter.ToUInt16(raw, offset + 0);
                var c1 = BitConverter.ToUInt16(raw, offset + 2);
                var indices = BitConverter.ToUInt32(raw, offset + 4);
                offset += 8;

                palette[0] = DecodeRgb565(c0, 255);
                palette[1] = DecodeRgb565(c1, 255);

                if (c0 > c1)
                {
                    palette[2] = Lerp(palette[0], palette[1], 2, 1, 3, 255);
                    palette[3] = Lerp(palette[0], palette[1], 1, 2, 3, 255);
                }
                else
                {
                    palette[2] = Lerp(palette[0], palette[1], 1, 1, 2, 255);
                    palette[3] = new Color32(0, 0, 0, 0);
                }

                WriteBlockPixels(width, height, bx, by, indices, palette, rgba);
            }
        }

        return rgba;
    }

    private static byte[] DecodeDxt5(int width, int height, byte[] raw)
    {
        var blockWidth = (width + 3) / 4;
        var blockHeight = (height + 3) / 4;
        var required = blockWidth * blockHeight * 16;
        if (raw.Length < required)
        {
            throw new InvalidDataException("Unexpected data size for DXT5.");
        }

        var rgba = new byte[width * height * 4];
        var offset = 0;
        var alphaPalette = new byte[8];
        var colorPalette = new Color32[4];
        for (var by = 0; by < blockHeight; by++)
        {
            for (var bx = 0; bx < blockWidth; bx++)
            {
                var a0 = raw[offset + 0];
                var a1 = raw[offset + 1];
                ulong alphaBits = 0;
                for (var i = 0; i < 6; i++)
                {
                    alphaBits |= (ulong)raw[offset + 2 + i] << (8 * i);
                }

                var c0 = BitConverter.ToUInt16(raw, offset + 8);
                var c1 = BitConverter.ToUInt16(raw, offset + 10);
                var colorBits = BitConverter.ToUInt32(raw, offset + 12);
                offset += 16;

                BuildAlphaPalette(a0, a1, alphaPalette);

                colorPalette[0] = DecodeRgb565(c0, 255);
                colorPalette[1] = DecodeRgb565(c1, 255);
                colorPalette[2] = Lerp(colorPalette[0], colorPalette[1], 2, 1, 3, 255);
                colorPalette[3] = Lerp(colorPalette[0], colorPalette[1], 1, 2, 3, 255);

                for (var py = 0; py < 4; py++)
                {
                    for (var px = 0; px < 4; px++)
                    {
                        var x = bx * 4 + px;
                        var y = by * 4 + py;
                        if (x >= width || y >= height)
                        {
                            continue;
                        }

                        var pixel = py * 4 + px;
                        var colorIdx = (int)((colorBits >> (pixel * 2)) & 0x3);
                        var alphaIdx = (int)((alphaBits >> (pixel * 3)) & 0x7);

                        var color = colorPalette[colorIdx];
                        var outOffset = (y * width + x) * 4;
                        rgba[outOffset + 0] = color.R;
                        rgba[outOffset + 1] = color.G;
                        rgba[outOffset + 2] = color.B;
                        rgba[outOffset + 3] = alphaPalette[alphaIdx];
                    }
                }
            }
        }

        return rgba;
    }

    private static void BuildAlphaPalette(byte a0, byte a1, Span<byte> palette)
    {
        palette[0] = a0;
        palette[1] = a1;

        if (a0 > a1)
        {
            palette[2] = (byte)((6 * a0 + 1 * a1) / 7);
            palette[3] = (byte)((5 * a0 + 2 * a1) / 7);
            palette[4] = (byte)((4 * a0 + 3 * a1) / 7);
            palette[5] = (byte)((3 * a0 + 4 * a1) / 7);
            palette[6] = (byte)((2 * a0 + 5 * a1) / 7);
            palette[7] = (byte)((1 * a0 + 6 * a1) / 7);
            return;
        }

        palette[2] = (byte)((4 * a0 + 1 * a1) / 5);
        palette[3] = (byte)((3 * a0 + 2 * a1) / 5);
        palette[4] = (byte)((2 * a0 + 3 * a1) / 5);
        palette[5] = (byte)((1 * a0 + 4 * a1) / 5);
        palette[6] = 0;
        palette[7] = 255;
    }

    private static void WriteBlockPixels(
        int width,
        int height,
        int blockX,
        int blockY,
        uint indices,
        Span<Color32> palette,
        byte[] output)
    {
        for (var py = 0; py < 4; py++)
        {
            for (var px = 0; px < 4; px++)
            {
                var x = blockX * 4 + px;
                var y = blockY * 4 + py;
                if (x >= width || y >= height)
                {
                    continue;
                }

                var pixel = py * 4 + px;
                var idx = (int)((indices >> (pixel * 2)) & 0x3);
                var color = palette[idx];
                var outOffset = (y * width + x) * 4;
                output[outOffset + 0] = color.R;
                output[outOffset + 1] = color.G;
                output[outOffset + 2] = color.B;
                output[outOffset + 3] = color.A;
            }
        }
    }

    private static Color32 DecodeRgb565(ushort value, byte alpha)
    {
        var r = (byte)((((value >> 11) & 0x1F) * 255 + 15) / 31);
        var g = (byte)((((value >> 5) & 0x3F) * 255 + 31) / 63);
        var b = (byte)(((value & 0x1F) * 255 + 15) / 31);
        return new Color32(r, g, b, alpha);
    }

    private static Color32 Lerp(Color32 c0, Color32 c1, int w0, int w1, int div, byte alpha)
    {
        var r = (byte)((c0.R * w0 + c1.R * w1) / div);
        var g = (byte)((c0.G * w0 + c1.G * w1) / div);
        var b = (byte)((c0.B * w0 + c1.B * w1) / div);
        return new Color32(r, g, b, alpha);
    }
}

internal readonly record struct Color32(byte R, byte G, byte B, byte A);
