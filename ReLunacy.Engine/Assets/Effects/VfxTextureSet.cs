// Ported from ReLunacy Resurgence by Fianshoo (GPL-3.0). "Docs/reverse/..." paths cite that repository.
using System.Buffers.Binary;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Assets.Textures;
using ReLunacy.Engine.Rendering;
using Texture = ReLunacy.Engine.Assets.Textures.Texture;

namespace ReLunacy.Engine.Assets.Effects;

/// <summary>
/// The level's particle textures: vfx_system_header.dat + vfx_system_texel.dat (new engine).
///
/// Both are ordinary IGHW 1.1 files (PROVEN on the 15 QForce levels):
/// <code>
///   header  0x5200  n x 0x20  RSX fragment-texture descriptors, the same NV4097 register image as a
///                             shader's 0x5200 (offset, mips|format, address, control0..3, filter, size)
///   texel   0x5300  one blob  the texels; a descriptor's +0x00 is an offset from the blob's start
///   texel   0x5A00  n x 8     { u32 hash, u32 texel byte size rounded to 0x80 } per descriptor (hash OPEN)
/// </code>
/// n is 99-136 per QForce level; every one is DXT1 (0x86) or DXT5 (0x88), 0x29 flags (linear, main
/// memory, 2D). An emitter's or quad's FxTexture handle is <c>0x8000 | index</c> into this table.
/// </summary>
public sealed class VfxTextureSet
{
    public sealed class Entry
    {
        public required int Index { get; init; }
        public required OldTextureDescriptorState Descriptor { get; init; }
        public required Texture Texture { get; init; }
        public uint Hash { get; init; }
        public uint TexelSize { get; init; }
        /// <summary>A composite's texels (<see cref="Composite"/>), already RGBA8; null for a texture of the packs.</summary>
        public byte[]? Rgba { get; init; }
        public string FormatName => Rgba != null ? "composite" : Texture.Format.ToString();
    }

    public static readonly VfxTextureSet Empty = new([]);

    private readonly List<Entry> _entries;
    private readonly Dictionary<string, int> _composites = [];
    public IReadOnlyList<Entry> Entries => _entries;

    /// <summary>Old engine: index of the global effect pack's first texture (-1 = none / new engine).</summary>
    public int GlobalBase { get; private init; } = -1;

    private VfxTextureSet(List<Entry> entries) => _entries = entries;

    public Entry? this[int index] => (uint)index < (uint)Entries.Count ? Entries[index] : null;

    public static VfxTextureSet Read(byte[] header, byte[] texel) => new(ReadEntries(header, texel, 0, "vfx"));

    /// <summary>
    /// Tools of Destruction: the level's <c>system.tph</c> / <c>system.tp</c> (handles <c>0x8000 | i</c>) followed by the game's
    /// <c>processed/texpacks/effect.tph</c> / <c>effect.tp</c> (plain handles), when found. Both packs are the same IGHW 1.1
    /// sections as the new engine's VFX pair (0x5200 descriptors, 0x5300 texels, 0x5A00 sizes) - metropolis: 48 + 69, all DXT5.
    /// </summary>
    public static VfxTextureSet ReadOldEngine(byte[] levelHeader, byte[] levelTexel, byte[]? globalHeader, byte[]? globalTexel)
    {
        var entries = ReadEntries(levelHeader, levelTexel, 0, "system");
        int globalBase = -1;
        if (globalHeader != null && globalTexel != null)
        {
            globalBase = entries.Count;
            entries.AddRange(ReadEntries(globalHeader, globalTexel, globalBase, "effect"));
        }
        return new VfxTextureSet(entries) { GlobalBase = globalBase };
    }

    private static List<Entry> ReadEntries(byte[] header, byte[] texel, int first, string prefix)
    {
        var headerSections = ReadSections(header);
        var texelSections = ReadSections(texel);
        if (!headerSections.TryGetValue(0x5200, out var descriptors))
            throw new InvalidDataException("vfx_system_header.dat has no 0x5200 descriptor table.");
        if (!texelSections.TryGetValue(0x5300, out var blob))
            throw new InvalidDataException("vfx_system_texel.dat has no 0x5300 texel blob.");
        texelSections.TryGetValue(0x5A00, out var sizes);

        var entries = new List<Entry>(descriptors.Count);
        for (int i = 0; i < descriptors.Count; i++)
        {
            int at = descriptors.Offset + i * 0x20;
            if (at + 0x20 > header.Length) break;
            OldTextureDescriptorState d = ReadDescriptor(header.AsSpan(at, 0x20));
            TextureFormat format = d.BaseFormatByte switch
            {
                0x81 => TextureFormat.R8,
                0x82 => TextureFormat.A1R5G5B5,
                0x83 => TextureFormat.RGBA4,
                0x84 => TextureFormat.R5G6B5,
                0x85 => TextureFormat.A8R8G8B8,
                0x86 => TextureFormat.DXT1,
                0x87 => TextureFormat.DXT3,
                0x88 => TextureFormat.DXT5,
                0x8B => TextureFormat.G8B8,
                _ => TextureFormat.Unknown,
            };

            uint hash = 0, size = 0;
            if (sizes.Count > i)
            {
                hash = BinaryPrimitives.ReadUInt32BigEndian(texel.AsSpan(sizes.Offset + 8 * i));
                size = BinaryPrimitives.ReadUInt32BigEndian(texel.AsSpan(sizes.Offset + 8 * i + 4));
            }

            int start = blob.Offset + (int)d.RsxOffset;
            int mips = Math.Max(1, d.MipmapCount);
            int bytes = format == TextureFormat.Unknown ? 0 : ChainSize(d.Width, d.Height, format, mips);
            if (start < 0 || start + bytes > texel.Length) bytes = Math.Max(0, texel.Length - start);
            byte[] data = bytes > 0 ? texel.AsSpan(start, bytes).ToArray() : [];

            var texture = Texture.FromData(0xEFF0_0000_0000_0000UL | (uint)(first + i), d.Width, d.Height,
                format, data, mips, d.Control1);
            texture.NewEngineDescriptor = d;
            texture.Name = $"{prefix}_{i:D3}";

            entries.Add(new Entry { Index = first + i, Descriptor = d, Texture = texture, Hash = hash, TexelSize = size });
        }
        return entries;
    }

    /// <summary>One 0x20-byte 0x5200 descriptor, the same NV4097 record the level's textures use.</summary>
    private static OldTextureDescriptorState ReadDescriptor(ReadOnlySpan<byte> r) => new(
        BinaryPrimitives.ReadUInt32BigEndian(r), BinaryPrimitives.ReadUInt16BigEndian(r[0x04..]),
        BinaryPrimitives.ReadUInt16BigEndian(r[0x06..]), BinaryPrimitives.ReadUInt32BigEndian(r[0x08..]),
        BinaryPrimitives.ReadUInt32BigEndian(r[0x0C..]), BinaryPrimitives.ReadUInt32BigEndian(r[0x10..]),
        BinaryPrimitives.ReadUInt32BigEndian(r[0x14..]), BinaryPrimitives.ReadUInt16BigEndian(r[0x18..]),
        BinaryPrimitives.ReadUInt16BigEndian(r[0x1A..]), BinaryPrimitives.ReadUInt32BigEndian(r[0x1C..]));

    /// <summary>Decodes mip 0 to RGBA8 with the descriptor's CONTROL1 channel remap applied (the same
    /// path the level's material textures take). Null for a format the decoder does not handle.</summary>
    public byte[]? DecodeRgba(int index, out int width, out int height)
    {
        width = height = 0;
        Entry? entry = this[index];
        if (entry?.Rgba != null)
        {
            (width, height) = ((int)entry.Texture.Width, (int)entry.Texture.Height);
            return entry.Rgba;
        }
        if (entry == null || entry.Texture.Format == TextureFormat.Unknown || entry.Texture.GetPixelData().Length == 0)
            return null;
        return TextureUtils.DecodeMipmapToRgba8888(entry.Texture, 0, out width, out height);
    }

    /// <summary>
    /// A texture that is the texel-wise product of <paramref name="layers"/> (indices into this set), appended once per
    /// distinct list; -1 when a layer does not decode. Tools of Destruction draws a Smoke sprite with the fragment program
    /// 0x2ED (EBOOT 0x1007F330: COL0 x tex0 x tex1 x tex2, every channel) and a Flame one with 0x2F0 / 0x2F1 (COL0 x tex0 x
    /// tex1, plus an alpha-threshold discard) - PROVEN, Docs/reverse/TOD_EFFECTS_2026-10-09.md. Each layer has its own UV set
    /// in the game (the vertex program turns the noise layers with the particle); the composite samples them all on the
    /// sprite's UV, a repeated layer turned a quarter turn so two samples of one noise do not square it (INFERRED).
    /// <paramref name="premultiply"/> scales the colour by the alpha: a screen blend (ONE, 1 - SRC_COLOR) ignores alpha, and
    /// the game's Flame program cuts the sprite with an alpha-threshold discard instead (INFERRED stand-in for that cut).
    /// </summary>
    public int Composite(IReadOnlyList<int> layers, bool premultiply = false)
    {
        string key = string.Join(',', layers) + (premultiply ? "p" : "");
        if (_composites.TryGetValue(key, out int known)) return known;
        var decoded = new List<(byte[] Rgba, int Width, int Height)>(layers.Count);
        foreach (int layer in layers)
        {
            byte[]? rgba = DecodeRgba(layer, out int w, out int h);
            if (rgba == null || w <= 0 || h <= 0) { _composites[key] = -1; return -1; }
            decoded.Add((rgba, w, h));
        }
        int size = Math.Clamp(decoded.Max(d => Math.Max(d.Width, d.Height)), 16, 256);
        var texels = new byte[size * size * 4];
        Span<float> c = stackalloc float[4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
            c.Fill(1f);
            for (int k = 0; k < decoded.Count; k++)
            {
                bool repeat = k > 0 && layers[k] == layers[0];
                Sample(decoded[k], repeat ? 1f - v : u, repeat ? u : v, c);
            }
            int o = (y * size + x) * 4;
            if (premultiply) { c[0] *= c[3]; c[1] *= c[3]; c[2] *= c[3]; }
            for (int ch = 0; ch < 4; ch++) texels[o + ch] = (byte)Math.Clamp(c[ch] * 255f + 0.5f, 0f, 255f);
        }
        int index = _entries.Count;
        Texture texture = Texture.FromData(0xEFF1_0000_0000_0000UL | (uint)index, (uint)size, (uint)size, TextureFormat.A8R8G8B8, texels);
        texture.Name = $"composite_{string.Join('_', layers)}{(premultiply ? "_p" : "")}";
        _entries.Add(new Entry { Index = index, Descriptor = _entries[layers[0]].Descriptor, Texture = texture, Rgba = texels });
        _composites[key] = index;
        return index;
    }

    /// <summary>Bilinear, clamped: multiplies <paramref name="into"/> by the texel at (u, v).</summary>
    private static void Sample((byte[] Rgba, int Width, int Height) t, float u, float v, Span<float> into)
    {
        float fx = Math.Clamp(u * t.Width - 0.5f, 0f, t.Width - 1), fy = Math.Clamp(v * t.Height - 0.5f, 0f, t.Height - 1);
        int x0 = (int)fx, y0 = (int)fy, x1 = Math.Min(x0 + 1, t.Width - 1), y1 = Math.Min(y0 + 1, t.Height - 1);
        float ax = fx - x0, ay = fy - y0;
        for (int ch = 0; ch < 4; ch++)
        {
            float top = t.Rgba[(y0 * t.Width + x0) * 4 + ch] * (1 - ax) + t.Rgba[(y0 * t.Width + x1) * 4 + ch] * ax;
            float bottom = t.Rgba[(y1 * t.Width + x0) * 4 + ch] * (1 - ax) + t.Rgba[(y1 * t.Width + x1) * 4 + ch] * ax;
            into[ch] *= (top * (1 - ay) + bottom * ay) / 255f;
        }
    }

    private static int ChainSize(int width, int height, TextureFormat format, int mips)
    {
        int total = 0, w = width, h = height;
        for (int m = 0; m < mips; m++)
        {
            total += TextureFormatCatalog.Get(format).MipByteSize(w, h);
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }
        return total;
    }

    private readonly record struct Section(int Offset, int Count, int Stride);

    /// <summary>IGHW 1.1 directory at 0x20: { id, offset, flags|count, stride-or-length }.</summary>
    private static Dictionary<uint, Section> ReadSections(byte[] data)
    {
        if (data.Length < 0x20 || data[0] != 'I' || data[1] != 'G' || data[2] != 'H' || data[3] != 'W')
            throw new InvalidDataException("Not an IGHW file.");
        int count = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8));
        var sections = new Dictionary<uint, Section>();
        for (int i = 0; i < count; i++)
        {
            int at = 0x20 + 0x10 * i;
            if (at + 0x10 > data.Length) break;
            uint id = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 4));
            uint third = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 8));
            int fourth = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 12));
            bool array = (third & 0x10000000) != 0;
            sections[id] = array
                ? new Section(offset, (int)(third & 0x0FFFFFFF), fourth)
                : new Section(offset, 1, fourth);
        }
        return sections;
    }
}
