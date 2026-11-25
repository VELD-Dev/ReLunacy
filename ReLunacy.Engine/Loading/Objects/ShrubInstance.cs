using System.Buffers.Binary;
using System.Numerics;

namespace ReLunacy.Engine.Loading.Objects;

/// <summary>
/// One shrub placement, from a zone's 0x7540 section: a 0x40-byte record per placement.
///
/// Three sections carry shrubs in a zone and they only appear together - every level of A Crack in
/// Time, All 4 One and Into the Nexus that ships shrubs.dat has 0x7500, 0x7540 and 0x75D0 (12, 6 and
/// 6 levels; 52, 34 and 65 zones), and the levels that do not have none of them:
///
///   0x7500  the zone's class table, one 8-byte TUID per shrub class used here (every entry
///           resolves in assetlookup's 0x1D500 table, in all three games)
///   0x7540  these placements, 0x40 bytes each in all three games
///   0x75D0  0x20-byte culling groups: a bounding sphere, then u32 +0x10 the first placement and
///           u16 +0x18 how many. They partition the placements contiguously with no gap - proven
///           on krell_canyon zone 1, where 253 groups tile all 2180 placements exactly.
///   0x7560  All 4 One and Into the Nexus only: the placements' names (see <see cref="NameTableID"/>).
///
/// Record: +0x00 position, +0x0C scale, +0x10 and +0x20 two axes of the rotation, +0x2C a u32 that
/// grows through the zone (an offset into data not read here), +0x30 a colour, +0x3C the class.
///
/// The class index is where the games DIFFER (see <see cref="Read"/>). A Crack in Time stores it as a
/// u32 at +0x3C: it indexes the zone's 0x7500 table in every one of the 52 zones, attributing all
/// 142,644 placements with none out of range. All 4 One and Into the Nexus store it as a u16 at +0x3C
/// with a second u16 after it: PROVEN by the name table, where 38,645 of their 38,646 placement names
/// start with the name of the class that u16 picks (one vendra_homeworld placement is named after
/// another class). Read as a u32 there, 21,141 placements fell outside the table and were dropped.
/// +0x3E is 0 or 2 in one crumbler zone and 0 everywhere else; what it means is unknown. It reads as
/// 0.0 when taken for a float, because small integers as float bits are denormals - which is exactly
/// how an earlier pass of this missed it and concluded the class was not stored at all.
/// </summary>
public readonly struct ShrubInstance
{
    public const uint ClassTableID = 0x7500;
    public const uint ID = 0x7540;
    public const uint GroupID = 0x75D0;

    /// <summary>
    /// All 4 One and Into the Nexus: shrub placement names, positionally parallel to <see cref="ID"/>,
    /// in the same record shape as TieInstance.NameTableID: TUID (8) + absolute name pointer (4) + 4
    /// zero bytes. Present in all 34 + 65 of their shrub zones with exactly one record per placement,
    /// names like "nb_cr_grass_4_280", and no TUID repeated within a level. A Crack in Time has no shrub
    /// name table, so its placements are named after their class.
    ///
    /// 0x74C0, which this used to be taken for, belongs to the FOLIAGE family instead: it is parallel to
    /// 0x7440 in every zone that has it, beside a 0x7400 class table whose TUIDs resolve in 0x1D400
    /// (foliages.dat) - hence its tree names, "nb_cr_tree_curved_2_5" on crumbler.
    /// </summary>
    public const uint NameTableID = 0x7560;
    public const int Size = 0x40;

    public Vector3 Position { get; init; }
    public float Scale { get; init; }

    /// <summary>Which entry of the zone's 0x7500 class table this placement draws.</summary>
    public uint ClassIndex { get; init; }

    /// <summary>
    /// Per-placement colour, +0x30. INFERRED as baked light: neighbouring placements carry nearly
    /// equal values and they range far above 1 (6.0, 4.3, 2.6 in open ground against 0.11, 0.21,
    /// 0.39 a few records later), which is how the rest of this engine's prelight reads. It is a real
    /// colour, not a scalar: the smallest channel is about half the largest at the median on
    /// zolar_forest, krell_canyon, seaside and swamp_wasteland. Into the Nexus writes exactly (1, 1, 1)
    /// on most of swamp_wasteland, which reads as an unbaked default.
    /// </summary>
    public Vector3 Colour { get; init; }

    /// <summary>+0x1C, a whole number stored as a float, 0-31. It is not the class (that is
    /// <see cref="ClassIndex"/>): every value takes an equal share of a level's placements (412 to
    /// 403 each over zolar_forest's 13,006, the same on krell_canyon, seaside and swamp_wasteland),
    /// the shape of a per-placement seed. Used as the sway phase; that use is INFERRED.</summary>
    public float Variant { get; init; }

    /// <summary>
    /// The placement's orientation, built from the two stored axes.
    ///
    /// Only two are written and the third is the cross product. They are unit and perpendicular to
    /// well under a percent in most levels of all three games, but not all: zolar_forest stores some
    /// up to 37% off unit, and nefarious_station, haunted_city and swamp_wasteland up to 10%, so they
    /// are re-orthonormalised here rather than trusted as written.
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

    /// <param name="crackInTime">A Crack in Time's class index is a u32 at +0x3C; All 4 One's and Into the
    /// Nexus' is a u16 there (see the summary).</param>
    public static ShrubInstance Read(ReadOnlySpan<byte> r, bool crackInTime) => new()
    {
        Position = new Vector3(F(r, 0x00), F(r, 0x04), F(r, 0x08)),
        Scale = F(r, 0x0C),
        AxisA = new Vector3(F(r, 0x10), F(r, 0x14), F(r, 0x18)),
        Variant = F(r, 0x1C),
        AxisB = new Vector3(F(r, 0x20), F(r, 0x24), F(r, 0x28)),
        Colour = new Vector3(F(r, 0x30), F(r, 0x34), F(r, 0x38)),
        ClassIndex = crackInTime
            ? BinaryPrimitives.ReadUInt32BigEndian(r[0x3C..])
            : BinaryPrimitives.ReadUInt16BigEndian(r[0x3C..]),
    };

    private static float F(ReadOnlySpan<byte> r, int at) => BinaryPrimitives.ReadSingleBigEndian(r[at..]);
}
