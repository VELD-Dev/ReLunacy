namespace ReLunacy.Engine.Assets.Materials;

/// <summary>Which asset family a <see cref="OldMaterialTextureBindingTable"/> belongs to.</summary>
public enum OldMaterialTextureBindingOwner
{
    /// <summary>main.dat 0x3500. Several records per Tie definition; +0x06 names the 0x3400 ordinal.</summary>
    Tie,

    /// <summary>main.dat 0x6400. Exactly one record per 0x6200 UFrag, in UFrag order, so the owner is
    /// the record's own ordinal and +0x06 carries the UFrag's secondary material instead.</summary>
    UFrag,

    /// <summary>main.dat 0xD800. Several records per Moby prototype; +0x06 names the 0xD100 ordinal.</summary>
    MobyPrototype,
}

/// <summary>
/// One 0x10-byte record of the old-engine material/texture binding family
/// (main.dat 0x3500 for Ties, 0x6400 for UFrags, 0xD800 for Moby prototypes).
///
/// NATIVE PROOF that the three are one family. The loader at 0x004E2068 files each table into its
/// subsystem registry (Tie 0x721C(r2) +0x18/+0x1C, UFrag 0x7220(r2) +0x08/+0x0C, Moby 0x725C(r2)
/// +0x2C/+0x30) and then hands all three (pointer, count) pairs to the SAME consumer, through the
/// cross-module thunk 0x0026AEC0 -> 0x00584B50:
///
///   0x004E26A0  UFrag +0x08/+0x0C  (0x6400)  -> 0x0026AEC0
///   0x004E26B4  Tie   +0x18/+0x1C  (0x3500)  -> 0x0026AEC0
///   0x004E26C8  Moby  +0x2C/+0x30  (0xD800)  -> 0x0026AEC0
///
/// 0x00584B50 reads the u16 at +0x08 as a material ordinal, indexes the 0x5000 material table by
/// `material * 0x80`, takes that material's four texture pointers, converts each to a 0x5200
/// descriptor ordinal by `(pointer - textureTableBase) / 0x20`, and writes the four results back
/// over the record's own +0x08/+0x0A/+0x0C/+0x0E. An out-of-range result is stored as the texture
/// count, which is that subsystem's "no texture" marker.
///
/// So the last eight bytes are an in-place input/output cell: on disk +0x08 is the material ordinal
/// and +0x0A..+0x0F are zero; after load the same eight bytes are four texture ordinals. Anything
/// that rewrites this section must serialize the DISK form, never the runtime form.
///
/// CORPUS (24 retail levels, ~90 000 records): +0x04 is 0 in every record of every table;
/// +0x0A/+0x0C/+0x0E are 0 in every record; +0x08 is below the level's 0x5000 count in every record.
/// </summary>
public readonly struct OldMaterialTextureBinding
{
    public const uint Stride = 0x10;

    /// <summary>+0x06 sentinel. On 0x6400 it means the UFrag has no secondary material.</summary>
    public const ushort NoSecondaryMaterial = 0xFFFF;

    /// <summary>Exact ceiling observed on <see cref="StreamingMetric"/> in all three tables.</summary>
    public const float StreamingMetricCeiling = 64.0f;

    /// <summary>Which table this record came from, which is what gives +0x06 its meaning.</summary>
    public required OldMaterialTextureBindingOwner Table { get; init; }

    /// <summary>Ordinal of this record inside its own section.</summary>
    public required int Index { get; init; }

    /// <summary>Absolute source offset of the record inside main.dat.</summary>
    public required uint SourceOffset { get; init; }

    /// <summary>
    /// +0x00. Authored initial value of a field the game mutates at runtime: the residency pass at
    /// 0x00584E78 walks this very table, and for a record whose bound textures are flagged it
    /// compares this float against a global threshold and, when below, subtracts a global step and
    /// stores the result back into +0x00. It is therefore a streaming / residency metric with an
    /// authored starting value, not a geometric property of the asset.
    ///
    /// Deliberately NOT named a distance, an LOD range or a texel density: the retail comparison
    /// and decrement are proven, the physical unit is not.
    /// </summary>
    public required float StreamingMetric { get; init; }

    /// <summary>+0x04. Zero in all ~90 000 audited retail records. Retained rather than dropped.</summary>
    public required ushort Reserved04 { get; init; }

    /// <summary>
    /// +0x06 exactly as serialized. Its meaning depends on <see cref="Table"/>; prefer
    /// <see cref="OwnerOrdinal"/> and <see cref="SecondaryMaterialIndex"/> over reading it directly.
    /// </summary>
    public required ushort Field06 { get; init; }

    /// <summary>+0x08 as serialized: the ordinal of the material in main.dat 0x5000.</summary>
    public required ushort MaterialIndex { get; init; }

    /// <summary>
    /// +0x0A/+0x0C/+0x0E as serialized. Zero in every audited retail record; these are the last
    /// three of the four runtime texture-ordinal slots that 0x00584B50 fills in after load.
    /// </summary>
    public required ushort ReservedRuntimeSlot1 { get; init; }

    /// <inheritdoc cref="ReservedRuntimeSlot1"/>
    public required ushort ReservedRuntimeSlot2 { get; init; }

    /// <inheritdoc cref="ReservedRuntimeSlot1"/>
    public required ushort ReservedRuntimeSlot3 { get; init; }

    /// <summary>
    /// Ordinal of the asset this binding belongs to: the 0x3400 Tie definition or the 0xD100 Moby
    /// prototype named at +0x06, or — for the UFrag table, which is parallel to 0x6200 — this
    /// record's own index.
    ///
    /// CORPUS PROOF, not inference: grouping 0x3500 by +0x06 reproduces exactly the set of primary
    /// material indices (0x3300 +0x28) of that definition's primitives for 2359 of 2361 definitions
    /// across the 24 retail levels, and grouping 0xD800 by +0x06 reproduces exactly the set of
    /// 0xDD00 +0x08 shader indices reachable from that prototype's bangles for 3562 of 3564
    /// prototypes. The handful of exceptions are preserved, not smoothed over.
    /// </summary>
    public int OwnerOrdinal => Table == OldMaterialTextureBindingOwner.UFrag ? Index : Field06;

    /// <summary>
    /// The UFrag's secondary material ordinal, or null when there is none or the table is not
    /// 0x6400. PROVEN: per level, the set of non-sentinel 0x6400 +0x06 values equals the set of
    /// non-sentinel 0x6200 +0x52 values, in all 21 levels that have both.
    /// </summary>
    public ushort? SecondaryMaterialIndex =>
        Table == OldMaterialTextureBindingOwner.UFrag && Field06 != NoSecondaryMaterial ? Field06 : null;

    /// <summary>True when the authored metric sits exactly on the ceiling every table clamps to.</summary>
    public bool IsAtCeiling => StreamingMetric == StreamingMetricCeiling;
}

/// <summary>One of the three material/texture binding tables of a level.</summary>
public sealed class OldMaterialTextureBindingTable
{
    public static readonly OldMaterialTextureBindingTable Empty =
        new(0, OldMaterialTextureBindingOwner.Tie, 0, []);

    public OldMaterialTextureBindingTable(
        uint sectionId,
        OldMaterialTextureBindingOwner owner,
        uint sectionOffset,
        IReadOnlyList<OldMaterialTextureBinding> records)
    {
        SectionId = sectionId;
        Owner = owner;
        SectionOffset = sectionOffset;
        Records = records;
    }

    public uint SectionId { get; }
    public OldMaterialTextureBindingOwner Owner { get; }

    /// <summary>Absolute offset of the section inside main.dat, 0 when the level has no such table.</summary>
    public uint SectionOffset { get; }

    public IReadOnlyList<OldMaterialTextureBinding> Records { get; }
    public bool IsEmpty => Records.Count == 0;

    /// <summary>Every record belonging to one asset ordinal, in serialized order. Retail writes
    /// 0x3500 and 0xD800 sorted by owner, but this scan does not depend on that.</summary>
    public IEnumerable<OldMaterialTextureBinding> ForOwner(int ownerOrdinal)
    {
        foreach (var record in Records)
            if (record.OwnerOrdinal == ownerOrdinal)
                yield return record;
    }

    /// <summary>Distinct 0x5000 material ordinals this table binds, in ascending order.</summary>
    public IReadOnlyList<ushort> DistinctMaterials()
    {
        var seen = new SortedSet<ushort>();
        foreach (var record in Records) seen.Add(record.MaterialIndex);
        return [.. seen];
    }
}

/// <summary>The three material/texture binding tables of an old-engine level.</summary>
public sealed class OldMaterialTextureBindings
{
    public static readonly OldMaterialTextureBindings Empty = new(
        OldMaterialTextureBindingTable.Empty,
        OldMaterialTextureBindingTable.Empty,
        OldMaterialTextureBindingTable.Empty);

    public OldMaterialTextureBindings(
        OldMaterialTextureBindingTable tie,
        OldMaterialTextureBindingTable uFrag,
        OldMaterialTextureBindingTable mobyPrototype)
    {
        Tie = tie;
        UFrag = uFrag;
        MobyPrototype = mobyPrototype;
    }

    /// <summary>main.dat 0x3500.</summary>
    public OldMaterialTextureBindingTable Tie { get; }

    /// <summary>main.dat 0x6400.</summary>
    public OldMaterialTextureBindingTable UFrag { get; }

    /// <summary>main.dat 0xD800.</summary>
    public OldMaterialTextureBindingTable MobyPrototype { get; }

    public bool IsEmpty => Tie.IsEmpty && UFrag.IsEmpty && MobyPrototype.IsEmpty;

    public int RecordCount => Tie.Records.Count + UFrag.Records.Count + MobyPrototype.Records.Count;

    public IEnumerable<OldMaterialTextureBindingTable> Tables
    {
        get
        {
            yield return Tie;
            yield return UFrag;
            yield return MobyPrototype;
        }
    }
}
