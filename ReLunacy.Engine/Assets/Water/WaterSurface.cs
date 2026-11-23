using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Assets.Water;

/// <summary>
/// One water body of a zone: new-engine zone section 0x6641, one 0x50-byte record each (the game's
/// IGPS3_R2OWaterObject). A Crack in Time ships them in 17 zones of 7 levels (tombli's sea, krell_canyon's
/// river...), All 4 One and Into the Nexus too.
///
/// A Crack in Time EBOOT: the zone loader (0x005878F8) hands 0x6641 to 0x00672018, which copies each record into a
/// 0xF40-byte water object (0x00671EA0, pool at 0x00F77400 +0x224, last index +0x238) tagged with the zone's index
/// (+0xE1); loading and unloading the zone switches it on and off (0x006700E0 / 0x00670088). The water query
/// (0x00676F18, used by the hero) takes the object whose X/Z rectangle holds the point and whose height is
/// nearest; a ClueNoWater volume vetoes it (0x00171334). The SPU supplies the deformed mesh and visible
/// index batches to the draw at 0x00673878.
///
///   +0x00  corner: x, height, z, 1          +0x10  size along X, 0, size along Z, 0
///   +0x20  colour: r, g, b (linear)          +0x2C  absorption per unit of depth; 100 or more draws opaque
///   +0x30  u64 the body's own id (unique)    +0x38  zone-relative pointer to an optional 0x5200 descriptor
///   +0x40  float, doubled at load            +0x44  u32, +6 at load and capped at 17
///   +0x48  u32                               +0x4C  u16
///   +0x4E  u8 index into 0x71C0, one-based (0 = default)
/// </summary>
public sealed class WaterSurface
{
    public const uint SectionId = 0x6641;
    public const int RecordSize = 0x50;

    /// <summary>The corner with the lowest X and Z, at the water's height.</summary>
    public Vector3 Corner { get; set; }
    /// <summary>Extent along X (.X) and Z (.Y).</summary>
    public Vector2 Size { get; set; }
    public Vector3 Colour { get; set; }
    public float Absorption { get; set; }
    public ulong Tuid { get; init; }
    public float Param40 { get; init; }
    public uint Param44 { get; init; }
    public uint Param48 { get; init; }
    public ushort Param4C { get; init; }
    public byte CubeSet { get; init; }
    public ulong ReflectionCubeTuid { get; init; }
    public ITexture? ClipMask { get; init; }

    /// <summary>The record's index in its zone's 0x6641.</summary>
    public int Index { get; init; }
    /// <summary>Where the record sits in zones.dat (zone base + section offset + index * 0x50).</summary>
    public long SourceOffset { get; init; }

    public float Height => Corner.Y;
    public Vector3 Centre => Corner + new Vector3(Size.X * 0.5f, 0f, Size.Y * 0.5f);

    /// <summary>The mesh parameter at +0x44 after the game's load-time adjustment (0x00671FD4).
    /// The editor uses it to choose a preview grid. The game receives per-batch counts from the SPU.</summary>
    public int TessellationLevel => (int)Math.Min(Param44, 11u) + 6;

    /// <summary>Drawn opaque, the game's second water program (0x00674F78).</summary>
    public bool Opaque => Absorption >= 100f;

    /// <summary>
    /// Which body each of a zone's records belongs to, numbered from 0 in record order. The file has no shape but the
    /// rectangle and no name for a body at all - the +0x30 id appears nowhere else in any file of krell_canyon,
    /// seaside or swamp_wasteland - so a body that is not a rectangle is authored as several records: same colour,
    /// absorption, +0x40, +0x44 and cube set, at the same height (within <see cref="BodyHeightTolerance"/>), touching or
    /// overlapping in X/Z (within <see cref="BodyGapTolerance"/>). That joins swamp_wasteland's 16 records into 11 bodies,
    /// one of four rectangles, and keeps krell_canyon's stepped pools apart (26 records, 24 bodies). INFERRED from the
    /// data; the game itself only ever looks at one record at a time.
    /// </summary>
    public static int[] Bodies(IReadOnlyList<WaterSurface> surfaces)
    {
        int n = surfaces.Count;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        int Find(int a) { while (parent[a] != a) a = parent[a] = parent[parent[a]]; return a; }

        for (int a = 0; a < n; a++)
        for (int b = a + 1; b < n; b++)
            if (SameBody(surfaces[a], surfaces[b])) parent[Find(a)] = Find(b);

        var numbers = new Dictionary<int, int>();
        var bodies = new int[n];
        for (int i = 0; i < n; i++)
        {
            int root = Find(i);
            if (!numbers.TryGetValue(root, out int body)) numbers[root] = body = numbers.Count;
            bodies[i] = body;
        }
        return bodies;
    }

    public const float BodyHeightTolerance = 0.25f;
    public const float BodyGapTolerance = 1f;

    private static bool SameBody(WaterSurface a, WaterSurface b)
    {
        if (a.Colour != b.Colour || a.Absorption != b.Absorption || a.Param40 != b.Param40 || a.Param44 != b.Param44 ||
            a.CubeSet != b.CubeSet || MathF.Abs(a.Height - b.Height) > BodyHeightTolerance)
            return false;
        float gapX = MathF.Max(a.Corner.X - (b.Corner.X + b.Size.X), b.Corner.X - (a.Corner.X + a.Size.X));
        float gapZ = MathF.Max(a.Corner.Z - (b.Corner.Z + b.Size.Y), b.Corner.Z - (a.Corner.Z + a.Size.Y));
        return gapX <= BodyGapTolerance && gapZ <= BodyGapTolerance;
    }

    /// <summary>A zone's water records, or an empty list when it has none - most zones.</summary>
    internal static List<WaterSurface> Read(IGFile zone, StreamHelper sh, long sourceBase,
        ulong[]? cubePalette, Func<uint, ITexture?>? readClipMask)
    {
        var result = new List<WaterSurface>();
        IGFile.SectionHeader section = zone.QuerySection(SectionId);
        if (section.id != SectionId || section.count == 0 || section.length != RecordSize) return result;
        for (int i = 0; i < section.count; i++)
        {
            long at = section.offset + (long)i * RecordSize;
            sh.Seek(at);
            var corner = new Vector3(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());
            sh.ReadSingle();
            float sizeX = sh.ReadSingle();
            sh.ReadSingle();
            float sizeZ = sh.ReadSingle();
            sh.ReadSingle();
            var colour = new Vector3(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());
            float absorption = sh.ReadSingle();
            ulong tuid = sh.ReadUInt64();
            uint maskOffset = sh.ReadUInt32();
            sh.ReadUInt32();
            float p40 = sh.ReadSingle();
            uint p44 = sh.ReadUInt32();
            uint p48 = sh.ReadUInt32();
            ushort p4C = sh.ReadUInt16();
            byte cubeSet = sh.ReadByte();
            if (!float.IsFinite(corner.X + corner.Y + corner.Z + sizeX + sizeZ) || sizeX <= 0f || sizeZ <= 0f) continue;
            result.Add(new WaterSurface
            {
                Corner = corner,
                Size = new Vector2(sizeX, sizeZ),
                Colour = colour,
                Absorption = absorption,
                Tuid = tuid,
                Param40 = p40,
                Param44 = p44,
                Param48 = p48,
                Param4C = p4C,
                CubeSet = cubeSet,
                ReflectionCubeTuid = cubePalette != null && cubeSet > 0 && cubeSet <= cubePalette.Length
                    ? cubePalette[cubeSet - 1] : 0,
                ClipMask = maskOffset != 0 ? readClipMask?.Invoke(maskOffset) : null,
                Index = i,
                SourceOffset = sourceBase + at,
            });
        }
        return result;
    }
}
