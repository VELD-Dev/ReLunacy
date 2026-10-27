namespace ReLunacy.Engine.Assets.Textures;

/// <summary>
/// Rewrites the alpha half of DXT5 (BC3) blocks and leaves the colour half byte for byte. The RSX reads
/// DXT5 in the standard layout (TextureUtils decodes the files' bytes with a plain BC3 decoder): per
/// 4x4 block, alpha endpoints a0 / a1, 16 three-bit indices little-endian, then the colour block.
/// </summary>
public static class Dxt5AlphaBlocks
{
    /// <summary>Returns <paramref name="chain"/> (top level first, standard mip sizes) with the alpha of
    /// every level replaced by <paramref name="alphaLevels"/> (one byte per texel, row-major).</summary>
    public static byte[] ReplaceAlpha(byte[] chain, int width, int height, byte[][] alphaLevels)
    {
        var result = (byte[])chain.Clone();
        int offset = 0, w = width, h = height;
        for (int level = 0; level < alphaLevels.Length; level++)
        {
            int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
            if (offset + bw * bh * 16 > result.Length)
                throw new InvalidDataException($"DXT5 chain of {width}x{height} ends before level {level}.");
            byte[] alpha = alphaLevels[level];
            if (alpha.Length != w * h)
                throw new ArgumentException($"Level {level} alpha holds {alpha.Length} texels, not {w}x{h}.");
            Span<byte> values = stackalloc byte[16];
            for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
            {
                for (int py = 0; py < 4; py++)
                for (int px = 0; px < 4; px++)
                {
                    int x = Math.Min(w - 1, bx * 4 + px), y = Math.Min(h - 1, by * 4 + py);
                    values[py * 4 + px] = alpha[y * w + x];
                }
                Encode(values, result.AsSpan(offset + (by * bw + bx) * 16, 8));
            }
            offset += bw * bh * 16;
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }
        return result;
    }

    /// <summary>
    /// Scales the colour of every DXT5 block of <paramref name="chain"/> (<paramref name="levels"/> levels from
    /// <paramref name="width"/> x <paramref name="height"/>) by a LINEAR per-channel factor, moving only the two
    /// RGB565 endpoints: the indices, and so every block's pattern, stay byte for byte. <paramref name="srgb"/>
    /// when the texture is sampled with gamma (zone lightmaps: ADDRESS gamma on R, G, B), so the stored value
    /// scales by factor^(1/2.2). BC3 colour blocks are always four-colour, so endpoint order does not matter.
    /// </summary>
    public static byte[] ScaleColour(byte[] chain, int width, int height, int levels, System.Numerics.Vector3 linearScale, bool srgb)
    {
        var result = (byte[])chain.Clone();
        var k = srgb
            ? new System.Numerics.Vector3(MathF.Pow(MathF.Max(0f, linearScale.X), 1f / 2.2f), MathF.Pow(MathF.Max(0f, linearScale.Y), 1f / 2.2f), MathF.Pow(MathF.Max(0f, linearScale.Z), 1f / 2.2f))
            : linearScale;
        int offset = 0, w = width, h = height;
        for (int level = 0; level < levels; level++)
        {
            int blocks = Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4);
            for (int b = 0; b < blocks && offset + b * 16 + 16 <= result.Length; b++)
            {
                int at = offset + b * 16 + 8;
                for (int e = 0; e < 2; e++)
                {
                    int c = result[at + e * 2] | (result[at + e * 2 + 1] << 8);
                    int r = (int)Math.Clamp(MathF.Round(((c >> 11) & 31) * k.X), 0, 31);
                    int g = (int)Math.Clamp(MathF.Round(((c >> 5) & 63) * k.Y), 0, 63);
                    int bl = (int)Math.Clamp(MathF.Round((c & 31) * k.Z), 0, 31);
                    int n = (r << 11) | (g << 5) | bl;
                    result[at + e * 2] = (byte)n;
                    result[at + e * 2 + 1] = (byte)(n >> 8);
                }
            }
            offset += blocks * 16;
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }
        return result;
    }

    /// <summary>Encodes 16 alpha values into an 8-byte BC3 alpha block, trying both endpoint modes.</summary>
    public static void Encode(ReadOnlySpan<byte> values, Span<byte> block)
    {
        byte min = 255, max = 0, innerMin = 255, innerMax = 0;
        foreach (byte v in values)
        {
            if (v < min) min = v;
            if (v > max) max = v;
            if (v != 0 && v != 255)
            {
                if (v < innerMin) innerMin = v;
                if (v > innerMax) innerMax = v;
            }
        }
        Span<byte> palette = stackalloc byte[8];
        Span<byte> bestIdx = stackalloc byte[16];
        Span<byte> idx = stackalloc byte[16];
        int bestError = int.MaxValue;
        byte bestA0 = 0, bestA1 = 0;

        // Mode 8: a0 > a1, six interpolated values.
        if (max > min)
        {
            Palette8(max, min, palette);
            int e = Fit(values, palette, idx);
            if (e < bestError) { bestError = e; bestA0 = max; bestA1 = min; idx.CopyTo(bestIdx); }
        }
        // Mode 6: a0 <= a1, four interpolated values plus exact 0 and 255 - best for hard shadows.
        {
            byte lo = innerMin <= innerMax ? innerMin : min, hi = innerMin <= innerMax ? innerMax : max;
            Palette6(lo, hi, palette);
            int e = Fit(values, palette, idx);
            if (e < bestError) { bestError = e; bestA0 = lo; bestA1 = hi; idx.CopyTo(bestIdx); }
        }

        block[0] = bestA0;
        block[1] = bestA1;
        ulong bits = 0;
        for (int i = 0; i < 16; i++) bits |= (ulong)bestIdx[i] << (3 * i);
        for (int i = 0; i < 6; i++) block[2 + i] = (byte)(bits >> (8 * i));
    }

    private static void Palette8(byte a0, byte a1, Span<byte> p)
    {
        p[0] = a0; p[1] = a1;
        for (int k = 1; k <= 6; k++) p[k + 1] = (byte)(((7 - k) * a0 + k * a1 + 3) / 7);
    }

    private static void Palette6(byte a0, byte a1, Span<byte> p)
    {
        p[0] = a0; p[1] = a1;
        for (int k = 1; k <= 4; k++) p[k + 1] = (byte)(((5 - k) * a0 + k * a1 + 2) / 5);
        p[6] = 0; p[7] = 255;
    }

    private static int Fit(ReadOnlySpan<byte> values, ReadOnlySpan<byte> palette, Span<byte> idx)
    {
        int error = 0;
        for (int i = 0; i < 16; i++)
        {
            int best = 0, bestD = int.MaxValue;
            for (int k = 0; k < 8; k++)
            {
                int d = values[i] - palette[k];
                d *= d;
                if (d < bestD) { bestD = d; best = k; }
            }
            idx[i] = (byte)best;
            error += bestD;
        }
        return error;
    }
}
