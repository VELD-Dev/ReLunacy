using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace ReLunacy.Engine.Loading.Objects;

/// <summary>
/// One shrub class as shrubs.dat stores it: a small scattered model (grass, fern, groundcover) with
/// a single mesh and a single material.
///
/// shrubs.dat is a CONTAINER, not one asset - assetlookup.dat's 0x1D500 pointer table slices it the
/// same way 0x1D300 slices ties.dat. Each slice is its own IGHW with six sections:
///
///   0xB700  the asset path, NUL-terminated, e.g.
///           "levels/krell_canyon/entities/plant/ml_krell_grass_desert/ml_krell_grass_desert.entity.irb"
///   0xB100  the 0x80 class header (below)
///   0xB300  vertices, 16 bytes each
///   0xB400  indices, u16, a plain triangle list
///   0x3150  4 bytes per vertex, purpose unknown (see <see cref="Unknown3150"/>)
///   0x5600  the material's TUID, 8 bytes
///
/// Header fields, PROVEN over all 49 A Crack in Time shrub classes: +0x08 is the index count
/// (49/49 equal 0xB400's length / 2) and +0x28 is the class's own TUID (49/49 equal the pointer
/// table's). +0x10/+0x14/+0x18/+0x1C are floats whose value sets are small and suggestive -
/// +0x14 takes 50/64/75/100/128/130/150/750 and +0x18 takes 10/25/75 - so they are read as a
/// draw-distance pair, which is INFERRED from those ranges rather than proven. All 4 One and Into
/// the Nexus keep the same pair (+0x18 is 25 in all 51 of their slices).
///
/// The same six sections, in the same order, at the same IGHW version (1.1), in every shrub slice of
/// every level that ships shrubs.dat: 12 of A Crack in Time's 22 levels, 6 of All 4 One's 10 and 6 of
/// Into the Nexus' 8 - 107 slices. Header +0x08 and +0x28 hold over all of them, and 0x3150 is a
/// quarter of 0xB300 in all of them. No later game adds a section or widens the vertex.
///
/// Header +0x40 is the model's box centre and +0x50 its half-extents, three floats each. That box is
/// what PROVES the position scale: it equals the i16 positions' own box divided by 1024 in 105 of the
/// 107 slices, in all three games (to 2e-3). The other two are the same class, Into the Nexus'
/// mc_vortex_large_01, whose x and z were clamped to the i16 range when it was built.
///
/// Vertex, 16 bytes: three i16 position components, scaled by 1/1024 as above, then a fourth i16 that
/// is 32767 everywhere except a few vertices of two classes (0 there) and the clamped vortex (10570) - so it is
/// NOT a normalising w, which made shrubs 32 times too small when it was read as one. Then four
/// halves: u, v, a third value, and a zero (the fourth is 0.0 on every vertex of all three games). The
/// UVs come out as exact quads - (0, 0.42) and (1, 0.42) over (0, 0) and (1, 0) - and the third half is
/// non-zero ONLY on the vertices whose v is non-zero, i.e. the top of the blade. It is read here as a
/// per-vertex bend weight, zero at the base, which is what a wind shader needs; that reading is
/// INFERRED from the correlation, not from the shader itself.
/// </summary>
public sealed class ShrubClass
{
    public const uint NameSection = 0xB700;
    public const uint HeaderSection = 0xB100;
    public const uint VertexSection = 0xB300;
    public const uint IndexSection = 0xB400;
    public const uint UnknownSection = 0x3150;
    public const uint MaterialSection = 0x5600;

    public const int VertexSize = 0x10;
    public const int HeaderSize = 0x80;

    /// <summary>i16 position components are fixed point with 10 fractional bits (see the header box above).</summary>
    private const float PositionScale = 1f / 1024f;

    public ulong TUID { get; private init; }
    public ulong MaterialTUID { get; private init; }
    public string? SourcePath { get; private init; }
    public (float Near, float Far) DrawDistances { get; private init; }

    /// <summary>xyz per vertex, already scaled to model units.</summary>
    public float[] Positions { get; private init; } = [];
    public float[] UVs { get; private init; } = [];

    /// <summary>Per-vertex bend weight (the third half of each vertex); 0 at the base of the model.</summary>
    public float[] BendWeights { get; private init; } = [];
    public uint[] Indices { get; private init; } = [];

    /// <summary>
    /// Section 0x3150 verbatim, one u32 per vertex, kept because it is not understood rather than
    /// because anything reads it. It is exactly a quarter of the vertex section in all 24 levels
    /// of the three games, so it is certainly per-vertex; the values are distinct within a class, but only for the two
    /// cloth props are they a permutation of 0..vertexCount-1 - for the grass classes they run
    /// past the vertex count (62 vertices reaching 94, 832 reaching 1583), which rules out a plain
    /// reorder. The ACiT EBOOT is SCE-encrypted, so the renderer has not been read.
    /// </summary>
    public uint[] Unknown3150 { get; private init; } = [];

    public int VertexCount => Positions.Length / 3;

    public static ShrubClass Read(ReadOnlySpan<byte> slice)
    {
        var sections = ReadSectionTable(slice);

        var header = Span(slice, sections, HeaderSection);
        ulong tuid = header.Length >= 0x30 ? BinaryPrimitives.ReadUInt64BigEndian(header[0x28..]) : 0;
        float far = header.Length >= 0x18 ? BinaryPrimitives.ReadSingleBigEndian(header[0x14..]) : 0f;
        float near = header.Length >= 0x1C ? BinaryPrimitives.ReadSingleBigEndian(header[0x18..]) : 0f;

        var material = Span(slice, sections, MaterialSection);
        ulong materialTuid = material.Length >= 8 ? BinaryPrimitives.ReadUInt64BigEndian(material) : 0;

        var nameBytes = Span(slice, sections, NameSection);
        int nul = nameBytes.IndexOf((byte)0);
        string? path = nameBytes.IsEmpty ? null : Encoding.ASCII.GetString(nul < 0 ? nameBytes : nameBytes[..nul]);

        var vertexBytes = Span(slice, sections, VertexSection);
        int vertexCount = vertexBytes.Length / VertexSize;
        var positions = new float[vertexCount * 3];
        var uvs = new float[vertexCount * 2];
        var bend = new float[vertexCount];
        for (int v = 0; v < vertexCount; v++)
        {
            ReadOnlySpan<byte> r = vertexBytes.Slice(v * VertexSize, VertexSize);
            positions[v * 3 + 0] = BinaryPrimitives.ReadInt16BigEndian(r) * PositionScale;
            positions[v * 3 + 1] = BinaryPrimitives.ReadInt16BigEndian(r[0x02..]) * PositionScale;
            positions[v * 3 + 2] = BinaryPrimitives.ReadInt16BigEndian(r[0x04..]) * PositionScale;
            uvs[v * 2 + 0] = (float)BinaryPrimitives.ReadHalfBigEndian(r[0x08..]);
            uvs[v * 2 + 1] = (float)BinaryPrimitives.ReadHalfBigEndian(r[0x0A..]);
            bend[v] = (float)BinaryPrimitives.ReadHalfBigEndian(r[0x0C..]);
        }

        var indexBytes = Span(slice, sections, IndexSection);
        var indices = new uint[indexBytes.Length / 2];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = BinaryPrimitives.ReadUInt16BigEndian(indexBytes[(i * 2)..]);

        var unknownBytes = Span(slice, sections, UnknownSection);
        var unknown = new uint[unknownBytes.Length / 4];
        for (int i = 0; i < unknown.Length; i++)
            unknown[i] = BinaryPrimitives.ReadUInt32BigEndian(unknownBytes[(i * 4)..]);

        return new ShrubClass
        {
            TUID = tuid,
            MaterialTUID = materialTuid,
            SourcePath = path,
            DrawDistances = (near, far),
            Positions = positions,
            UVs = uvs,
            BendWeights = bend,
            Indices = indices,
            Unknown3150 = unknown,
        };
    }

    public (Vector3 center, float radius) BoundingSphere()
    {
        if (VertexCount == 0) return (Vector3.Zero, 0f);
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        for (int v = 0; v < VertexCount; v++)
        {
            var p = new Vector3(Positions[v * 3], Positions[v * 3 + 1], Positions[v * 3 + 2]);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        Vector3 centre = (min + max) * 0.5f;
        float radius = 0f;
        for (int v = 0; v < VertexCount; v++)
            radius = MathF.Max(radius, Vector3.Distance(centre, new Vector3(Positions[v * 3], Positions[v * 3 + 1], Positions[v * 3 + 2])));
        return (centre, radius);
    }

    /// <summary>The slice's own IGHW section table. Read here rather than through IGFile because a
    /// shrub is a span inside shrubs.dat, not a file.</summary>
    private static Dictionary<uint, (int Offset, int Length)> ReadSectionTable(ReadOnlySpan<byte> slice)
    {
        var sections = new Dictionary<uint, (int, int)>();
        if (slice.Length < 0x20) return sections;
        if (!slice[..4].SequenceEqual("IGHW"u8)) return sections;

        ushort major = BinaryPrimitives.ReadUInt16BigEndian(slice[4..]);
        ushort minor = BinaryPrimitives.ReadUInt16BigEndian(slice[6..]);
        int tableBase = (major, minor) switch { (1, 1) => 0x20, (0, 2) => 0x10, _ => -1 };
        if (tableBase < 0) return sections;

        uint count = BinaryPrimitives.ReadUInt32BigEndian(slice[8..]);
        for (int i = 0; i < count; i++)
        {
            int at = tableBase + i * 0x10;
            if (at + 0x10 > slice.Length) break;
            uint id = BinaryPrimitives.ReadUInt32BigEndian(slice[at..]);
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(slice[(at + 4)..]);
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(slice[(at + 12)..]);
            if (offset >= 0 && length >= 0 && offset + length <= slice.Length)
                sections[id] = (offset, length);
        }
        return sections;
    }

    private static ReadOnlySpan<byte> Span(ReadOnlySpan<byte> slice, Dictionary<uint, (int Offset, int Length)> sections, uint id)
        => sections.TryGetValue(id, out var s) ? slice.Slice(s.Offset, s.Length) : default;
}
