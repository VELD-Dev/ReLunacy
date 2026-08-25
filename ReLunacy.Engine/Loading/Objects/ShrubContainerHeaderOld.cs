using System.Numerics;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Objects;

/// <summary>Header of the old-engine Shrub spatial container — main.dat section 0x9540, a single
/// blob (not a repeated-record table like <see cref="ShrubMetadataOld"/>'s 0xB100).
///
/// PROVEN field-for-field from the EBOOT's own initializer (0x573EF8, reached from the 0x9540
/// loader handler at 0x4E29D4 via stub 0x269CC0): it reads Value00/08/10/18 as plain u32s into the
/// runtime manager, and Relative04/0C/14 the same way EXCEPT it also adds the section's own base
/// pointer before storing them — i.e. those three are offsets relative to the START OF THIS BLOB,
/// not absolute main.dat offsets and not relative to main.dat's own base. Resolve them as
/// <c>section9540.offset + relativeValue</c>, and bounds-check against the section's length before
/// touching anything at the resolved address.
///
/// <see cref="Parameter20"/>/<see cref="Parameter24"/>/<see cref="Parameter28"/> are proven to be
/// floats (EBOOT reads them with `lfs`) used somewhere in the renderer's spatial math — nothing
/// pins down which distance/scale/parameter each one is, so they keep neutral names rather than a
/// guessed "CellSize"/"GridScale"/"WorldScale".
///
/// <see cref="Version"/> is proven read and compared against 3 by the loader (EBOOT 0x573F18-
/// 0x573F1C, `lhz r0,0x2C(r4)` / `cmpwi r0,3`) before it trusts the rest of this header — ShrubReader
/// does the same and skips parsing anything past this header when it doesn't match, rather than
/// guessing at a different revision's layout.</summary>
public record struct ShrubContainerHeaderOld
{
    public const uint ID = 0x9540;
    public const ushort ExpectedVersion = 3;

    public uint Value00;

    /// <summary>Offset, relative to this section's own start, of a table of 0x40-byte records — see
    /// <see cref="ShrubSpatialRecordOld"/>. The record COUNT is not established (no field in this
    /// header is proven to hold it), so ShrubReader reports this offset for diagnostics but does not
    /// enumerate the table yet.</summary>
    public uint Relative04;

    public uint Value08;

    /// <summary>Offset, relative to this section's own start, of a second internal table. Contents
    /// not yet examined.</summary>
    public uint Relative0C;

    public uint Value10;

    /// <summary>Offset, relative to this section's own start, of a third internal table. Contents
    /// not yet examined.</summary>
    public uint Relative14;

    public uint Value18;
    public uint Unknown1C;

    /// <summary>PROVEN float (EBOOT `lfs f4,0x20(r4)`). Meaning not established.</summary>
    public float Parameter20;

    /// <summary>PROVEN float (EBOOT `lfs f3,0x24(r4)`). Meaning not established.</summary>
    public float Parameter24;

    /// <summary>PROVEN float (EBOOT `lfs f12,0x28(r4)`). Meaning not established.</summary>
    public float Parameter28;

    /// <summary>PROVEN: the loader requires this to equal <see cref="ExpectedVersion"/> (3) before
    /// trusting the rest of the blob.</summary>
    public ushort Version;

    public ushort Unknown2E;

    public static ShrubContainerHeaderOld Read(StreamHelper sh, uint sectionOffset)
    {
        var h = new ShrubContainerHeaderOld
        {
            Value00 = sh.ReadUInt32(sectionOffset + 0x00),
            Relative04 = sh.ReadUInt32(sectionOffset + 0x04),
            Value08 = sh.ReadUInt32(sectionOffset + 0x08),
            Relative0C = sh.ReadUInt32(sectionOffset + 0x0C),
            Value10 = sh.ReadUInt32(sectionOffset + 0x10),
            Relative14 = sh.ReadUInt32(sectionOffset + 0x14),
            Value18 = sh.ReadUInt32(sectionOffset + 0x18),
            Unknown1C = sh.ReadUInt32(sectionOffset + 0x1C),
            Version = sh.ReadUInt16(sectionOffset + 0x2C),
            Unknown2E = sh.ReadUInt16(sectionOffset + 0x2E),
        };

        sh.Seek(sectionOffset + 0x20);
        h.Parameter20 = sh.ReadSingle();
        h.Parameter24 = sh.ReadSingle();
        h.Parameter28 = sh.ReadSingle();

        return h;
    }
}

/// <summary>One 0x40-byte record of the table pointed to by <see cref="ShrubContainerHeaderOld.Relative04"/>.
/// Size is PROVEN by the EBOOT's own copy loop (0x5748D0-0x57499C: index * 64 addressing, four
/// SIMD 0x10-byte block copies per record). Split into four raw Vector4 slots deliberately — NOT
/// named Position/Scale/Axis1/Axis2 the way older Insomniac-engine layouts of this shape are
/// sometimes structured, because that has not been checked against real Shrub data yet (no
/// main.dat with both 0xB100 and 0x9540 was available while writing this). Rename the slots only
/// after validating the interpretation against a real sample.</summary>
public record struct ShrubSpatialRecordOld
{
    public const uint Size = 0x40;

    public Vector4 Value0;
    public Vector4 Value1;
    public Vector4 Value2;
    public Vector4 Value3;

    public static ShrubSpatialRecordOld Read(StreamHelper sh, uint recordBase)
    {
        sh.Seek(recordBase);
        return new ShrubSpatialRecordOld
        {
            Value0 = new Vector4(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle()),
            Value1 = new Vector4(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle()),
            Value2 = new Vector4(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle()),
            Value3 = new Vector4(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle()),
        };
    }
}
