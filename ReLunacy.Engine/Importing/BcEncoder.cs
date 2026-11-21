using System.Numerics;

namespace ReLunacy.Engine.Importing;

/// <summary>
/// BC1 (DXT1) and BC3 (DXT5) block encoders for imported textures: the standard little-endian block layout the RSX samples and
/// <c>TextureUtils</c> decodes. Colour endpoints come from the block's principal axis, then two least-squares refits of the endpoints
/// to the chosen indices, each candidate scored with a luminance-weighted error after 565 rounding. Colour blocks are always written
/// in four-colour order (c0 &gt; c1), so BC1 and the colour half of BC3 decode the same everywhere. BC3 alpha is the eight-value mode
/// over the block's alpha range, refitted once.
/// </summary>
public static class BcEncoder
{
    private static readonly Vector3 Weight = new(0.299f, 0.587f, 0.114f);

    /// <summary>One mip level, tightly packed RGBA8 (width x height x 4), to BC1. Sides need not be multiples of 4.</summary>
    public static byte[] EncodeBc1(ReadOnlySpan<byte> rgba, int width, int height) => Encode(rgba, width, height, alpha: false);

    /// <summary>One mip level to BC3: an alpha block then a colour block per 4x4.</summary>
    public static byte[] EncodeBc3(ReadOnlySpan<byte> rgba, int width, int height) => Encode(rgba, width, height, alpha: true);

    /// <summary>Bytes of one BC1 (8 per block) or BC3 (16 per block) mip level.</summary>
    public static int Size(int width, int height, bool bc3) => Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * (bc3 ? 16 : 8);

    private static byte[] Encode(ReadOnlySpan<byte> rgba, int width, int height, bool alpha)
    {
        if (rgba.Length < width * height * 4) throw new ArgumentException("the pixel buffer is shorter than width x height x 4");
        int bw = Math.Max(1, (width + 3) / 4), bh = Math.Max(1, (height + 3) / 4);
        int stride = alpha ? 16 : 8;
        var output = new byte[bw * bh * stride];
        Span<Vector3> colours = stackalloc Vector3[16];
        Span<byte> alphas = stackalloc byte[16];
        for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
            {
                for (int i = 0; i < 16; i++)
                {
                    // Edge blocks repeat the last row / column.
                    int x = Math.Min(bx * 4 + (i & 3), width - 1), y = Math.Min(by * 4 + (i >> 2), height - 1);
                    int p = (y * width + x) * 4;
                    colours[i] = new Vector3(rgba[p], rgba[p + 1], rgba[p + 2]);
                    alphas[i] = rgba[p + 3];
                }
                var block = output.AsSpan((by * bw + bx) * stride, stride);
                if (alpha)
                {
                    EncodeAlpha(alphas, block[..8]);
                    EncodeColour(colours, block[8..]);
                }
                else EncodeColour(colours, block);
            }
        return output;
    }

    // ---- colour ---------------------------------------------------------------------------------------------------------

    private static void EncodeColour(ReadOnlySpan<Vector3> c, Span<byte> block)
    {
        var mean = Vector3.Zero;
        for (int i = 0; i < 16; i++) mean += c[i];
        mean /= 16f;

        // Principal axis of the weighted colours by power iteration on the covariance.
        float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        for (int i = 0; i < 16; i++)
        {
            var d = (c[i] - mean) * Weight;
            xx += d.X * d.X; xy += d.X * d.Y; xz += d.X * d.Z; yy += d.Y * d.Y; yz += d.Y * d.Z; zz += d.Z * d.Z;
        }
        var axis = new Vector3(1f, 1f, 1f);
        for (int k = 0; k < 8; k++)
        {
            var next = new Vector3(xx * axis.X + xy * axis.Y + xz * axis.Z, xy * axis.X + yy * axis.Y + yz * axis.Z, xz * axis.X + yz * axis.Y + zz * axis.Z);
            float len = next.Length();
            if (len < 1e-8f) break;
            axis = next / len;
        }
        axis = Vector3.Normalize(axis / Weight);

        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < 16; i++)
        {
            float t = Vector3.Dot(c[i] - mean, axis);
            lo = MathF.Min(lo, t); hi = MathF.Max(hi, t);
        }
        var bestA = mean + axis * hi;
        var bestB = mean + axis * lo;

        Span<int> indices = stackalloc int[16];
        Span<int> trial = stackalloc int[16];
        ushort best0 = 0, best1 = 0;
        float bestError = float.MaxValue;
        var a = bestA; var b = bestB;
        for (int pass = 0; pass < 3; pass++)
        {
            ushort q0 = To565(a), q1 = To565(b);
            float error = Fit(c, q0, q1, trial);
            if (error < bestError)
            {
                bestError = error; best0 = q0; best1 = q1;
                trial.CopyTo(indices);
            }
            if (!Refit(c, trial, out a, out b)) break;
        }
        Write(block, best0, best1, indices);
    }

    /// <summary>Indices for the endpoints (as decoded) and the weighted squared error.</summary>
    private static float Fit(ReadOnlySpan<Vector3> c, ushort q0, ushort q1, Span<int> indices)
    {
        Span<Vector3> palette = stackalloc Vector3[4];
        Palette(q0, q1, palette);
        float total = 0;
        for (int i = 0; i < 16; i++)
        {
            int pick = 0; float best = float.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                var d = (c[i] - palette[k]) * Weight;
                float e = d.LengthSquared();
                if (e < best) { best = e; pick = k; }
            }
            indices[i] = pick;
            total += best;
        }
        return total;
    }

    /// <summary>Least-squares endpoints for fixed indices (weights 1, 2/3, 1/3, 0 on endpoint A). False when degenerate.</summary>
    private static bool Refit(ReadOnlySpan<Vector3> c, ReadOnlySpan<int> indices, out Vector3 a, out Vector3 b)
    {
        float aa = 0, bb = 0, ab = 0;
        Vector3 ax = Vector3.Zero, bx = Vector3.Zero;
        for (int i = 0; i < 16; i++)
        {
            float w = indices[i] switch { 0 => 1f, 1 => 0f, 2 => 2f / 3f, _ => 1f / 3f };
            float v = 1f - w;
            aa += w * w; bb += v * v; ab += w * v;
            ax += c[i] * w; bx += c[i] * v;
        }
        float det = aa * bb - ab * ab;
        if (MathF.Abs(det) < 1e-6f) { a = b = Vector3.Zero; return false; }
        a = Vector3.Clamp((ax * bb - bx * ab) / det, Vector3.Zero, new Vector3(255f));
        b = Vector3.Clamp((bx * aa - ax * ab) / det, Vector3.Zero, new Vector3(255f));
        return true;
    }

    private static void Palette(ushort q0, ushort q1, Span<Vector3> palette)
    {
        palette[0] = From565(q0);
        palette[1] = From565(q1);
        palette[2] = (palette[0] * 2f + palette[1]) / 3f;
        palette[3] = (palette[0] + palette[1] * 2f) / 3f;
    }

    /// <summary>Writes the block in four-colour order: c0 &gt; c1, indices remapped when the endpoints are swapped.</summary>
    private static void Write(Span<byte> block, ushort q0, ushort q1, ReadOnlySpan<int> indices)
    {
        Span<int> map = stackalloc int[] { 0, 1, 2, 3 };
        if (q0 < q1) { (q0, q1) = (q1, q0); map[0] = 1; map[1] = 0; map[2] = 3; map[3] = 2; }
        uint bits = 0;
        if (q0 != q1)
            for (int i = 0; i < 16; i++) bits |= (uint)map[indices[i]] << (2 * i);
        block[0] = (byte)q0; block[1] = (byte)(q0 >> 8);
        block[2] = (byte)q1; block[3] = (byte)(q1 >> 8);
        block[4] = (byte)bits; block[5] = (byte)(bits >> 8); block[6] = (byte)(bits >> 16); block[7] = (byte)(bits >> 24);
    }

    private static ushort To565(Vector3 c)
    {
        int r = Math.Clamp((int)MathF.Round(c.X * 31f / 255f), 0, 31);
        int g = Math.Clamp((int)MathF.Round(c.Y * 63f / 255f), 0, 63);
        int b = Math.Clamp((int)MathF.Round(c.Z * 31f / 255f), 0, 31);
        return (ushort)(r << 11 | g << 5 | b);
    }

    private static Vector3 From565(ushort q)
    {
        int r = q >> 11 & 31, g = q >> 5 & 63, b = q & 31;
        return new Vector3(r << 3 | r >> 2, g << 2 | g >> 4, b << 3 | b >> 2);
    }

    // ---- alpha ----------------------------------------------------------------------------------------------------------

    private static void EncodeAlpha(ReadOnlySpan<byte> alpha, Span<byte> block)
    {
        int lo = 255, hi = 0;
        for (int i = 0; i < 16; i++) { lo = Math.Min(lo, alpha[i]); hi = Math.Max(hi, alpha[i]); }
        Span<int> indices = stackalloc int[16];
        Span<int> trial = stackalloc int[16];
        int bestA = hi, bestB = lo;
        float bestError = AlphaFit(alpha, hi, lo, indices);
        // One least-squares refit of the two endpoints to the chosen ramp positions.
        if (hi > lo)
        {
            float aa = 0, bb = 0, ab = 0, ax = 0, bx = 0;
            for (int i = 0; i < 16; i++)
            {
                float w = AlphaWeight(indices[i]), v = 1f - w;
                aa += w * w; bb += v * v; ab += w * v; ax += alpha[i] * w; bx += alpha[i] * v;
            }
            float det = aa * bb - ab * ab;
            if (MathF.Abs(det) > 1e-6f)
            {
                int a = Math.Clamp((int)MathF.Round((ax * bb - bx * ab) / det), 0, 255);
                int b = Math.Clamp((int)MathF.Round((bx * aa - ax * ab) / det), 0, 255);
                if (a < b) (a, b) = (b, a);
                if (a > b)
                {
                    float e = AlphaFit(alpha, a, b, trial);
                    if (e < bestError) { bestError = e; bestA = a; bestB = b; trial.CopyTo(indices); }
                }
            }
        }
        if (bestA == bestB) indices.Clear();
        block[0] = (byte)bestA;
        block[1] = (byte)bestB;
        ulong bits = 0;
        for (int i = 0; i < 16; i++) bits |= (ulong)indices[i] << (3 * i);
        for (int k = 0; k < 6; k++) block[2 + k] = (byte)(bits >> (8 * k));
    }

    /// <summary>Weight of endpoint a0 for code k of the eight-value ramp (a0 &gt; a1): 0 -&gt; a0, 1 -&gt; a1, 2..7 between.</summary>
    private static float AlphaWeight(int k) => k switch { 0 => 1f, 1 => 0f, _ => (8 - k) / 7f };

    private static float AlphaFit(ReadOnlySpan<byte> alpha, int a0, int a1, Span<int> indices)
    {
        Span<float> ramp = stackalloc float[8];
        // The decoder's integer ramp: ((8 - k) a0 + (k - 1) a1) / 7 between the endpoints.
        ramp[0] = a0; ramp[1] = a1;
        for (int k = 2; k < 8; k++) ramp[k] = a0 > a1 ? ((8 - k) * a0 + (k - 1) * a1) / 7 : a0;
        float total = 0;
        for (int i = 0; i < 16; i++)
        {
            int pick = 0; float best = float.MaxValue;
            for (int k = 0; k < 8; k++)
            {
                float e = (alpha[i] - ramp[k]) * (alpha[i] - ramp[k]);
                if (e < best) { best = e; pick = k; }
            }
            indices[i] = pick;
            total += best;
        }
        return total;
    }
}
