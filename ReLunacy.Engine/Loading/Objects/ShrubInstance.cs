using System.Buffers.Binary;
using System.Numerics;

namespace ReLunacy.Engine.Loading.Objects;

/// <summary>
/// One shrub placement, from a zone's 0x7540 section: a 0x40-byte record per placement.
///
/// Three sections carry shrubs in a zone and they only appear together - all twelve A Crack in
/// Time levels that ship shrubs.dat have 0x7500, 0x7540 and 0x75D0, and the ten that do not have
/// none of them:
///
///   0x7500  the zone's class table, one 8-byte TUID per shrub class used here (every entry
///           resolves in assetlookup's 0x1D500 table)
///   0x7540  these placements
///   0x75D0  0x20-byte culling groups: a bounding sphere, then u32 +0x10 the first placement and
///           u16 +0x18 how many. They partition the placements contiguously with no gap - proven
///           on krell_canyon zone 1, where 253 groups tile all 2180 placements exactly.
///
/// Record: +0x00 position, +0x0C scale, +0x10 and +0x20 two axes of the rotation, +0x30 a colour,
/// +0x3C the class.
///
/// <see cref="ClassIndex"/> is PROVEN: read as a u32 it indexes the zone's 0x7500 table in every
/// one of the 52 zones, attributing all 142,644 A Crack in Time placements with none out of range.
/// It reads as 0.0 when taken for a float, because 0-4 as float bits are denormals - which is
/// exactly how an earlier pass of this missed it and concluded the class was not stored at all.
/// </summary>
public readonly struct ShrubInstance
{
    public const uint ClassTableID = 0x7500;
    public const uint ID = 0x7540;
    public const uint GroupID = 0x75D0;

    /// <summary>
    /// Shrub placement names, positionally parallel to <see cref="ID"/>, in the same record shape as
    /// TieInstance.NameTableID: TUID (8) + absolute name pointer (4) + 4 zero bytes. Present only on
    /// zones that have shrubs, which is why ZoneMetadata used to read a shrub's name as the zone's on
    /// some zones and nothing at all on others (see NewEngineZoneNames). Names read as
    /// "nb_cr_tree_curved_2_5" on A4O crumbler. Not consumed yet - shrubs are loaded but not drawn.
    /// </summary>
    public const uint NameTableID = 0x74C0;
    public const int Size = 0x40;

    public Vector3 Position { get; init; }
    public float Scale { get; init; }

    /// <summary>Which entry of the zone's 0x7500 class table this placement draws.</summary>
    public uint ClassIndex { get; init; }

    /// <summary>
    /// Per-placement colour, +0x30. INFERRED as baked light: neighbouring placements carry nearly
    /// equal values and they range far above 1 (6.0, 4.3, 2.6 in open ground against 0.11, 0.21,
    /// 0.39 a few records later), which is how the rest of this engine's prelight reads.
    /// </summary>
    public Vector3 Colour { get; init; }

    /// <summary>+0x1C, a whole number stored as a float, 0-31. Unexplained: it is not the class
    /// (that is <see cref="ClassIndex"/>) and it takes 32 values in zones holding one class.</summary>
    public float Variant { get; init; }

    /// <summary>
    /// The placement's orientation, built from the two stored axes.
    ///
    /// Only two are written and the third is the cross product. They are unit and perpendicular to
    /// well under a percent on krell_canyon, molonoth and valkyrie_fleet - all 52,676 placements of
    /// those three levels - while zolar_forest stores them loosely, within about 5% of unit and 2.5
    /// degrees of square, so they are re-orthonormalised here rather than trusted as written.
    /// </summary>
    public Matrix4x4 ToMatrix()
    {
        Vector3 right = Normalise(AxisA, Vector3.UnitX);
        Vector3 up = Normalise(AxisB, Vector3.UnitY);
        // Gram-Schmidt: keep up, square right against it, and derive the third.
        right = Normalise(right - up * Vector3.Dot(right, up), Vector3.UnitX);
        Vector3 forward = Vector3.Cross(right, up);

        return new Matrix4x4(
            right.X * Scale, right.Y * Scale, right.Z * Scale, 0f,
            up.X * Scale, up.Y * Scale, up.Z * Scale, 0f,
            forward.X * Scale, forward.Y * Scale, forward.Z * Scale, 0f,
            Position.X, Position.Y, Position.Z, 1f);
    }

    internal Vector3 AxisA { get; init; }
    internal Vector3 AxisB { get; init; }

    private static Vector3 Normalise(Vector3 v, Vector3 fallback)
    {
        float length = v.Length();
        return length > 1e-6f ? v / length : fallback;
    }

    public static ShrubInstance Read(ReadOnlySpan<byte> r) => new()
    {
        Position = new Vector3(F(r, 0x00), F(r, 0x04), F(r, 0x08)),
        Scale = F(r, 0x0C),
        AxisA = new Vector3(F(r, 0x10), F(r, 0x14), F(r, 0x18)),
        Variant = F(r, 0x1C),
        AxisB = new Vector3(F(r, 0x20), F(r, 0x24), F(r, 0x28)),
        Colour = new Vector3(F(r, 0x30), F(r, 0x34), F(r, 0x38)),
        ClassIndex = BinaryPrimitives.ReadUInt32BigEndian(r[0x3C..]),
    };

    private static float F(ReadOnlySpan<byte> r, int at) => BinaryPrimitives.ReadSingleBigEndian(r[at..]);
}
