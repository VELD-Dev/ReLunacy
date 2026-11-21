using System.Numerics;
using ReLunacy.Engine.Rendering.Resources;

namespace ReLunacy.Engine.Importing;

/// <summary>What a source image is for: the shader slot it fills (0x5000 slot 0 albedo, 1 normal map, 2 expensive, 3 detail), or a
/// Tie class's overlay mask (0x5700 / 0x5800).</summary>
public enum ImportTextureKind { Albedo = 0, Normal = 1, Expensive = 2, Detail = 3, OverlayMask = 8 }

/// <summary>A material's source image: its encoded bytes (PNG, JPEG, ...) and where they came from.</summary>
public sealed class ImportImage
{
    /// <summary>A label (the glTF image name or the file name).</summary>
    public string Name = "";
    /// <summary>The file the bytes were read from; null for an image embedded in the model.</summary>
    public string? SourcePath;
    public byte[] Bytes = [];

    public static ImportImage FromFile(string path) => new() { Name = Path.GetFileNameWithoutExtension(path), SourcePath = path, Bytes = File.ReadAllBytes(path) };

    private int _longest = -1;

    /// <summary>The longest side of the source image, read from its header; 0 when it cannot be read.</summary>
    public int LongestSide
    {
        get
        {
            if (_longest >= 0) return _longest;
            try
            {
                using var stream = new MemoryStream(Bytes);
                var info = StbImageSharp.ImageInfo.FromStream(stream);
                _longest = info is { } i ? Math.Max(i.Width, i.Height) : 0;
            }
            catch (Exception) { _longest = 0; }
            return _longest;
        }
    }
}

/// <summary>How <see cref="TextureImporter"/> sizes and encodes an image.</summary>
public sealed class TextureImportOptions
{
    /// <summary>Longest side after the power-of-two resize.</summary>
    public int MaxSize = TextureImporter.SafeSize;
    /// <summary>Multiplies the albedo (the glTF base colour factor, linear).</summary>
    public Vector4 Factor = Vector4.One;
    /// <summary>Keep the alpha (BC3 when it is not all opaque): the albedo's, or the expensive texture's detail mask; false writes BC1.</summary>
    public bool KeepAlpha;
    /// <summary>Inverts the normal map's green channel (DirectX-style source).</summary>
    public bool FlipNormalGreen;
}

/// <summary>
/// One encoded texture, laid out as a new-engine level stores it (A Crack in Time, PROVEN on 22/22 levels): the format byte as
/// assetlookup 0x1D140 holds it (bare: 0x06 DXT1, 0x08 DXT5, 0x05 A8R8G8B8), mip 0 in highmips.dat and the rest in textures.dat,
/// both tightly packed; a single-mip texture lives in textures.dat alone (highmips length 0). BC mips stop at the level whose
/// longer side is 4 (log2(longer side) - 1 levels); square A8R8G8B8 goes down to 1 x 1, each mip Morton-swizzled on its own, bytes
/// A, R, G, B (the detail maps: 332 of 342 retail detail slots).
/// </summary>
public sealed class EncodedTexture
{
    /// <summary>The source path the shader names (0x5D00); CRC32(name) is the TUID's high word.</summary>
    public string Name = "";
    public ImportTextureKind Kind;
    /// <summary>0x06 DXT1, 0x08 DXT5 or 0x05 A8R8G8B8.</summary>
    public byte Format;
    public int Width, Height, MipCount;
    /// <summary>Every mip, tightly packed from mip 0.</summary>
    public byte[] Chain = [];
    public int Mip0Size;
    /// <summary>Mip 0 as RGBA8 before encoding (for checks).</summary>
    public byte[] SourceRgba = [];
    public bool IsDxt5 => Format == 0x08;
    /// <summary>Bytes the shader's 0x5A00 entry and descriptor offsets count: the chain on a 0x80 boundary.</summary>
    public int AllocationSize => (Chain.Length + 0x7F) & ~0x7F;
}

/// <summary>
/// Turns a PNG / JPEG into an <see cref="EncodedTexture"/>: decoded to RGBA8 (StbImageSharp, <see cref="Image"/>), resized to powers of
/// two (the nearest in log scale, at most <see cref="TextureImportOptions.MaxSize"/>, at least 4), mips by 2x2 averaging (albedo in
/// linear light, normals renormalised), then BC1 / BC3 (<see cref="BcEncoder"/>).
/// Normal maps are written as retail Tie shaders read them: DXT5 with the partial derivatives dx = x / z in alpha and dy = y / z in
/// green (clamped to -1..1, biased 0.5), R and B zero; the shader's CONTROL1 0x6AE0 presents (A, A, G, 1) (PROVEN: 197 of 197 DXT5
/// normal descriptors of great_clock_a) and the renderer rebuilds normalize(dx, dy, 1). X and Y are the image's red and green, as the
/// glTF exporter writes them back out (round trip); the green sign is INFERRED.
/// </summary>
public static class TextureImporter
{
    /// <summary>The largest side imports are scaled down to unless the user keeps the full size; larger textures may not load in game.</summary>
    public const int SafeSize = 512;

    public static EncodedTexture Encode(ImportImage image, ImportTextureKind kind, TextureImportOptions? options = null) =>
        Encode(new Image(image.Bytes), kind, options);

    /// <summary>A flat 4 x 4 texture (one mip) of <paramref name="rgba"/>: an albedo from a material with only a colour.</summary>
    public static EncodedTexture Solid(Vector4 rgba, ImportTextureKind kind, TextureImportOptions? options = null)
    {
        var data = new byte[4 * 4 * 4];
        for (int i = 0; i < 16; i++)
        {
            data[4 * i] = ToByte(rgba.X); data[4 * i + 1] = ToByte(rgba.Y); data[4 * i + 2] = ToByte(rgba.Z); data[4 * i + 3] = ToByte(rgba.W);
        }
        var o = options ?? new TextureImportOptions();
        return Encode(new Image(4, 4, data), kind, new TextureImportOptions { MaxSize = 4, KeepAlpha = o.KeepAlpha, FlipNormalGreen = o.FlipNormalGreen });
    }

    public static EncodedTexture Encode(Image image, ImportTextureKind kind, TextureImportOptions? options = null)
    {
        options ??= new TextureImportOptions();
        if (image.Width <= 0 || image.Height <= 0) throw new ArgumentException("the image is empty");
        int maxSize = Math.Clamp(PowerOfTwo(options.MaxSize), 4, 4096);
        int w = Math.Clamp(PowerOfTwo(image.Width), 4, maxSize), h = Math.Clamp(PowerOfTwo(image.Height), 4, maxSize);
        // Detail maps and overlay masks are square on retail (detail 0x85; masks 128..1024, one mip, 55 of 55 classes).
        if (kind is ImportTextureKind.Detail or ImportTextureKind.OverlayMask) w = h = Math.Max(w, h);
        if (kind == ImportTextureKind.OverlayMask) w = h = Math.Clamp(w, 128, 1024);

        // Working pixels: linear RGBA for albedo, a unit vector in RGB for normals, raw for properties.
        var pixels = ToWorking(image, kind, options);
        var level = Resample(pixels, image.Width, image.Height, w, h);
        if (kind == ImportTextureKind.Normal) Renormalise(level);

        bool argb = kind == ImportTextureKind.Detail;
        bool dxt5 = kind == ImportTextureKind.Normal
                    || (kind is ImportTextureKind.Albedo or ImportTextureKind.Expensive && options.KeepAlpha && level.Any(p => p.W < 254.5f / 255f));
        int mips = kind == ImportTextureKind.OverlayMask ? 1 : argb ? (int)Math.Log2(w) + 1 : Math.Max(1, (int)Math.Log2(Math.Max(w, h)) - 1);
        var chain = new List<byte>();
        int mip0 = 0;
        byte[] source0 = [];
        int lw = w, lh = h;
        for (int m = 0; m < mips; m++)
        {
            if (m > 0)
            {
                level = Halve(level, lw, lh, out int nw, out int nh);
                lw = nw; lh = nh;
                if (kind == ImportTextureKind.Normal) Renormalise(level);
            }
            byte[] rgba = ToRgba(level, kind);
            if (m == 0) source0 = rgba;
            byte[] encoded = argb ? SwizzleArgb(rgba, lw, lh) : dxt5 ? BcEncoder.EncodeBc3(rgba, lw, lh) : BcEncoder.EncodeBc1(rgba, lw, lh);
            if (m == 0) mip0 = encoded.Length;
            chain.AddRange(encoded);
        }
        return new EncodedTexture
        {
            Kind = kind,
            Format = argb ? (byte)0x05 : dxt5 ? (byte)0x08 : (byte)0x06,
            Width = w,
            Height = h,
            MipCount = mips,
            Chain = [.. chain],
            Mip0Size = mip0,
            SourceRgba = source0,
        };
    }

    /// <summary>RGBA8 to the swizzled A8R8G8B8 the loader reads: element i of the stream is pixel MortonSwizzle(i), bytes A, R, G, B.</summary>
    private static byte[] SwizzleArgb(byte[] rgba, int w, int h)
    {
        var output = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            int p = Loading.Textures.Texture.MortonSwizzle(i, w, h) * 4;
            output[4 * i] = rgba[p + 3]; output[4 * i + 1] = rgba[p]; output[4 * i + 2] = rgba[p + 1]; output[4 * i + 3] = rgba[p + 2];
        }
        return output;
    }

    /// <summary>
    /// The expensive texture (slot 2) from its parts, as the retail fragment programs read it: R specular, G parallax height, B
    /// emissive (lighting += 4 x B), A detail mask (props.a scales the detail map) - the editor's renderer, from the EBOOT programs.
    /// <paramref name="expensive"/> gives all four when present; otherwise R is <paramref name="specular"/>, G 0, A 1. An emissive
    /// image (times <paramref name="emissiveFactor"/>, its linear luminance) replaces B; the slot has no gamma (address 0x130101), the
    /// glow takes its colour from the albedo.
    /// </summary>
    public static Image ComposeExpensive(Image? expensive, Image? emissive, Vector3 emissiveFactor, float specular, int fallbackSize = 4)
    {
        int w = expensive?.Width ?? emissive?.Width ?? fallbackSize, h = expensive?.Height ?? emissive?.Height ?? fallbackSize;
        var data = new byte[w * h * 4];
        byte r = ToByte(specular);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int p = (y * w + x) * 4;
                if (expensive != null)
                {
                    int q = ((y * expensive.Height / h) * expensive.Width + x * expensive.Width / w) * 4;
                    data[p] = expensive.Data[q]; data[p + 1] = expensive.Data[q + 1]; data[p + 2] = expensive.Data[q + 2]; data[p + 3] = expensive.Data[q + 3];
                }
                else { data[p] = r; data[p + 3] = 255; }
                if (emissive != null)
                {
                    int q = ((y * emissive.Height / h) * emissive.Width + x * emissive.Width / w) * 4;
                    var e = new Vector3(SrgbToLinear[emissive.Data[q]], SrgbToLinear[emissive.Data[q + 1]], SrgbToLinear[emissive.Data[q + 2]]) * emissiveFactor;
                    data[p + 2] = ToByte(Math.Clamp(Vector3.Dot(e, new Vector3(0.2126f, 0.7152f, 0.0722f)), 0f, 1f));
                }
                else if (expensive == null) data[p + 2] = ToByte(Math.Clamp(Vector3.Dot(emissiveFactor, new Vector3(0.2126f, 0.7152f, 0.0722f)), 0f, 1f));
            }
        return new Image(w, h, data);
    }

    /// <summary>
    /// The expensive texture (slot 2) from one grayscale image per channel: R specular, G parallax height, B emissive, A detail mask.
    /// A colour image gives its luminance; the images are resized to the largest. A missing channel is <paramref name="specular"/> (R),
    /// 0 (G, B) or 1 (A).
    /// </summary>
    public static Image ComposeExpensiveChannels(Image? specular, Image? height, Image? emissive, Image? mask, float specularLevel, int fallbackSize = 4)
    {
        var parts = new[] { specular, height, emissive, mask };
        int w = parts.Max(i => i?.Width) ?? fallbackSize, h = parts.Max(i => i?.Height) ?? fallbackSize;
        var data = new byte[w * h * 4];
        byte[] r = Luminance(specular, w, h, ToByte(specularLevel)), g = Luminance(height, w, h, 0), b = Luminance(emissive, w, h, 0), a = Luminance(mask, w, h, 255);
        for (int i = 0; i < w * h; i++) { data[4 * i] = r[i]; data[4 * i + 1] = g[i]; data[4 * i + 2] = b[i]; data[4 * i + 3] = a[i]; }
        return new Image(w, h, data);
    }

    /// <summary>
    /// The detail texture (slot 3, A8R8G8B8 read as tex x 2 - 1) from its parts: RG the detail normal's partial derivatives (as the main
    /// normal map, added to its own), B the albedo lift and A the specular add, both grayscale. A missing part is 128, the neutral
    /// byte of the signed read (the normal's RG is then no perturbation).
    /// </summary>
    public static Image ComposeDetail(Image? normal, Image? albedo, Image? specular, bool flipNormalGreen, int fallbackSize = 4)
    {
        var parts = new[] { normal, albedo, specular };
        int w = parts.Max(i => i?.Width) ?? fallbackSize, h = parts.Max(i => i?.Height) ?? fallbackSize;
        var data = new byte[w * h * 4];
        byte[] b = Luminance(albedo, w, h, 128), a = Luminance(specular, w, h, 128);
        var n = normal == null ? null : ResizeRgba(normal, w, h);
        for (int i = 0; i < w * h; i++)
        {
            byte dx = 128, dy = 128;
            if (n != null)
            {
                float y = n[4 * i + 1] / 255f * 2f - 1f;
                var v = new Vector3(n[4 * i] / 255f * 2f - 1f, flipNormalGreen ? -y : y, n[4 * i + 2] / 255f * 2f - 1f);
                v = v.LengthSquared() < 1e-8f ? Vector3.UnitZ : Vector3.Normalize(v);
                var (ddx, ddy) = Derivatives(new Vector4(v, 1f));
                dx = ToByte(ddx * 0.5f + 0.5f); dy = ToByte(ddy * 0.5f + 0.5f);
            }
            data[4 * i] = dx; data[4 * i + 1] = dy; data[4 * i + 2] = b[i]; data[4 * i + 3] = a[i];
        }
        return new Image(w, h, data);
    }

    /// <summary>The image's luminance (Rec. 709 on the stored bytes) resized bilinearly to w x h; <paramref name="missing"/> everywhere for null.</summary>
    private static byte[] Luminance(Image? image, int w, int h, byte missing)
    {
        var result = new byte[w * h];
        if (image == null) { Array.Fill(result, missing); return result; }
        var rgba = ResizeRgba(image, w, h);
        for (int i = 0; i < result.Length; i++)
            result[i] = ToByte((0.2126f * rgba[4 * i] + 0.7152f * rgba[4 * i + 1] + 0.0722f * rgba[4 * i + 2]) / 255f);
        return result;
    }

    private static byte[] ResizeRgba(Image image, int w, int h)
    {
        if (image.Width == w && image.Height == h) return image.Data;
        var src = new Vector4[image.Width * image.Height];
        for (int i = 0; i < src.Length; i++) src[i] = new Vector4(image.Data[4 * i], image.Data[4 * i + 1], image.Data[4 * i + 2], image.Data[4 * i + 3]);
        var level = Resample(src, image.Width, image.Height, w, h);
        var result = new byte[w * h * 4];
        for (int i = 0; i < level.Length; i++)
        {
            result[4 * i] = (byte)Math.Clamp((int)MathF.Round(level[i].X), 0, 255); result[4 * i + 1] = (byte)Math.Clamp((int)MathF.Round(level[i].Y), 0, 255);
            result[4 * i + 2] = (byte)Math.Clamp((int)MathF.Round(level[i].Z), 0, 255); result[4 * i + 3] = (byte)Math.Clamp((int)MathF.Round(level[i].W), 0, 255);
        }
        return result;
    }

    /// <summary>The nearest power of two in log scale.</summary>
    private static int PowerOfTwo(int v)
    {
        if (v <= 1) return 1;
        int lo = 1 << (int)Math.Floor(Math.Log2(v));
        int hi = lo << 1;
        return v / (double)lo <= hi / (double)v ? lo : hi;
    }

    // ---- colour spaces -----------------------------------------------------------------------------------------------

    private static readonly float[] SrgbToLinear = Enumerable.Range(0, 256).Select(i =>
    {
        float c = i / 255f;
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }).ToArray();

    private static float LinearToSrgb(float c)
    {
        c = Math.Clamp(c, 0f, 1f);
        return c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    private static Vector4[] ToWorking(Image image, ImportTextureKind kind, TextureImportOptions options)
    {
        var data = image.Data;
        var result = new Vector4[image.Width * image.Height];
        for (int i = 0; i < result.Length; i++)
        {
            int p = 4 * i;
            switch (kind)
            {
                case ImportTextureKind.Albedo:
                    result[i] = new Vector4(SrgbToLinear[data[p]], SrgbToLinear[data[p + 1]], SrgbToLinear[data[p + 2]], data[p + 3] / 255f) * options.Factor;
                    break;
                case ImportTextureKind.Normal:
                {
                    float y = data[p + 1] / 255f * 2f - 1f;
                    var n = new Vector3(data[p] / 255f * 2f - 1f, options.FlipNormalGreen ? -y : y, data[p + 2] / 255f * 2f - 1f);
                    n = n.LengthSquared() < 1e-8f ? Vector3.UnitZ : Vector3.Normalize(n);
                    result[i] = new Vector4(n, 1f);
                    break;
                }
                default:
                    result[i] = new Vector4(data[p], data[p + 1], data[p + 2], data[p + 3]) / 255f;
                    break;
            }
        }
        return result;
    }

    private static byte[] ToRgba(Vector4[] level, ImportTextureKind kind)
    {
        var rgba = new byte[level.Length * 4];
        for (int i = 0; i < level.Length; i++)
        {
            var v = level[i];
            int p = 4 * i;
            switch (kind)
            {
                case ImportTextureKind.Albedo:
                    rgba[p] = ToByte(LinearToSrgb(v.X)); rgba[p + 1] = ToByte(LinearToSrgb(v.Y)); rgba[p + 2] = ToByte(LinearToSrgb(v.Z)); rgba[p + 3] = ToByte(v.W);
                    break;
                case ImportTextureKind.Normal:
                    // DXT5 X-in-alpha, Y-in-green: CONTROL1 0x6AE0 presents (A, A, G, 1).
                    var (dx, dy) = Derivatives(v);
                    rgba[p] = 0; rgba[p + 1] = ToByte(dy * 0.5f + 0.5f); rgba[p + 2] = 0; rgba[p + 3] = ToByte(dx * 0.5f + 0.5f);
                    break;
                default:
                    rgba[p] = ToByte(v.X); rgba[p + 1] = ToByte(v.Y); rgba[p + 2] = ToByte(v.Z); rgba[p + 3] = ToByte(v.W);
                    break;
            }
        }
        return rgba;
    }

    /// <summary>A unit tangent-space normal as the partial derivatives (x / z, y / z) the shaders read (normalize(dx, dy, 1)), clamped to
    /// the texture's -1..1 range.</summary>
    private static (float Dx, float Dy) Derivatives(Vector4 n)
    {
        float z = MathF.Max(n.Z, 0.05f);
        return (Math.Clamp(n.X / z, -1f, 1f), Math.Clamp(n.Y / z, -1f, 1f));
    }

    private static void Renormalise(Vector4[] level)
    {
        for (int i = 0; i < level.Length; i++)
        {
            var n = new Vector3(level[i].X, level[i].Y, level[i].Z);
            n = n.LengthSquared() < 1e-8f ? Vector3.UnitZ : Vector3.Normalize(n);
            level[i] = new Vector4(n, 1f);
        }
    }

    // ---- resampling --------------------------------------------------------------------------------------------------

    /// <summary>Separable resample: area average where a side shrinks, bilinear where it grows.</summary>
    private static Vector4[] Resample(Vector4[] src, int sw, int sh, int dw, int dh)
    {
        if (sw == dw && sh == dh) return (Vector4[])src.Clone();
        var rows = new Vector4[dw * sh];
        for (int y = 0; y < sh; y++)
            ResampleLine(src.AsSpan(y * sw, sw), rows.AsSpan(y * dw, dw));
        var result = new Vector4[dw * dh];
        var column = new Vector4[sh];
        var outColumn = new Vector4[dh];
        for (int x = 0; x < dw; x++)
        {
            for (int y = 0; y < sh; y++) column[y] = rows[y * dw + x];
            ResampleLine(column, outColumn);
            for (int y = 0; y < dh; y++) result[y * dw + x] = outColumn[y];
        }
        return result;
    }

    private static void ResampleLine(ReadOnlySpan<Vector4> src, Span<Vector4> dst)
    {
        int n = src.Length, m = dst.Length;
        if (n == m) { src.CopyTo(dst); return; }
        float scale = (float)n / m;
        if (m < n)
        {
            for (int i = 0; i < m; i++)
            {
                float start = i * scale, end = start + scale;
                var sum = Vector4.Zero; float weight = 0;
                for (int s = (int)start; s < Math.Min(n, (int)MathF.Ceiling(end)); s++)
                {
                    float w = MathF.Min(end, s + 1) - MathF.Max(start, s);
                    if (w <= 0) continue;
                    sum += src[s] * w; weight += w;
                }
                dst[i] = weight > 0 ? sum / weight : src[Math.Min(n - 1, (int)start)];
            }
        }
        else
        {
            for (int i = 0; i < m; i++)
            {
                float x = (i + 0.5f) * scale - 0.5f;
                int x0 = Math.Clamp((int)MathF.Floor(x), 0, n - 1), x1 = Math.Min(n - 1, x0 + 1);
                float t = Math.Clamp(x - x0, 0f, 1f);
                dst[i] = Vector4.Lerp(src[x0], src[x1], t);
            }
        }
    }

    private static Vector4[] Halve(Vector4[] src, int w, int h, out int nw, out int nh)
    {
        nw = Math.Max(1, w / 2); nh = Math.Max(1, h / 2);
        var result = new Vector4[nw * nh];
        for (int y = 0; y < nh; y++)
            for (int x = 0; x < nw; x++)
            {
                int x0 = Math.Min(w - 1, 2 * x), x1 = Math.Min(w - 1, 2 * x + 1), y0 = Math.Min(h - 1, 2 * y), y1 = Math.Min(h - 1, 2 * y + 1);
                result[y * nw + x] = (src[y0 * w + x0] + src[y0 * w + x1] + src[y1 * w + x0] + src[y1 * w + x1]) * 0.25f;
            }
        return result;
    }
}
