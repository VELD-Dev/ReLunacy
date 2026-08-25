using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Interfaces;

namespace ReLunacy.Engine.Loading.Objects;

/// <summary>One old-engine "Shrub" asset record — main.dat section 0xB100, 0x30 bytes per record.
///
/// NAMING NOTE: the EBOOT is stripped and carries no "Shrub" symbol or string anywhere. What IS
/// proven from the EBOOT alone is that 0xB100 is a distinct old-engine subsystem: an asset table
/// selected by u16 index (<see cref="StreamingLoadCode"/> path, EBOOT 0x57673C-0x57674C proves
/// stride 0x30), tied to a spatial container at section 0x9540, resolving its material through the
/// same 0x5000 shader table every other old-engine mesh uses, and fed from the resources tagged
/// 0x9000/0x9100 in vertices.dat (the SAME two section IDs Tie/Moby/UFrag already use there as the
/// shared vertex/index pools — see TieMetadataOld, Moby.cs QuerySection(0x9000)/(0x9100)). ReLunacy
/// calls this subsystem "Shrub" because static, non-collidable background foliage/props is exactly
/// the category being added and no other old-engine subsystem already claims that name — not
/// because the EBOOT says so.
///
/// PROOF LEVEL PER FIELD is called out individually below. Two fields
/// (<see cref="RuntimeMaterialValue58"/>/<see cref="RuntimeMaterialValue34"/>/<see cref="RuntimeRenderCode"/>)
/// are proven to be OVERWRITTEN by the EBOOT at load time from the resolved material — their on-disk
/// value is kept here for reverse/debug purposes only, not as the value the game actually renders
/// with.
///
/// Everything else (geometry decode, the 9540-&gt;B100 instance/transform mapping) needs a real
/// main.dat containing both 0xB100 and 0x9540 to verify against — none was available while writing
/// this (see ShrubReader's class remarks) — so this record intentionally stops at metadata.</summary>
public record struct ShrubMetadataOld : ILunaSerializable
{
    public const uint ID = 0xB100;
    public const uint Size = 0x30;

    /// <summary>PROVEN relocated at load time against the resource tagged 0x9000 (EBOOT 0x4E2068:
    /// the loader calls its section-0x9000 decode, then at 0x4E2A1C-0x4E2A24 adds that resource's
    /// runtime base onto this field in place). 0x9000 is the same vertices.dat section id Tie/Moby/
    /// UFrag already use as the shared old-engine vertex pool (see TieMetadataOld's remarks) — a
    /// strong independent hint this is a vertex-buffer offset into that same pool, but NOT yet
    /// verified for Shrub specifically (no sample), so it keeps a neutral name rather than
    /// "VertexBufferOffset" until an actual Shrub vertex layout is decoded against it.</summary>
    public uint Resource9000Offset;

    /// <summary>Unidentified. The subsystem also loads the resource tagged 0x9100 (vertices.dat's
    /// shared old-engine INDEX pool for Tie/Moby/UFrag), and this field is a plausible candidate for
    /// an offset/count into it, but the EBOOT reverse did not pin down which of +0x04/+0x08 (if
    /// either) is that field — do not name it indexOffset without checking it against real 0x9100
    /// data (index range validity, divisible-by-3 counts, etc. — see ShrubReader remarks).</summary>
    public uint Unknown04;

    /// <summary>Unidentified — see <see cref="Unknown04"/>.</summary>
    public uint Unknown08;

    /// <summary>PROVEN read and tested (at least one bit) by the EBOOT (`lhz ...,0x0C(B100)`). Which
    /// bit(s) mean what is not established — kept raw for debug tooling.</summary>
    public ushort Flags0C;

    public ushort Unknown0E;

    /// <summary>Raw, unidentified 4 bytes at +0x10.</summary>
    public uint Unknown10_13;

    /// <summary>On-disk value at +0x14. PROVEN overwritten at load time with `*(float*)(material+0x58)`
    /// (EBOOT: `lfs f0,0x58(material)` / `stfs f0,0x14(B100)`) — i.e. ShaderMetadataOld.detailTiling.
    /// The value read here is whatever shipped on disk BEFORE that runtime patch, not what the game
    /// actually renders with; kept for reverse/debug only.</summary>
    public float RuntimeMaterialValue58;

    /// <summary>On-disk value at +0x18. PROVEN overwritten at load time with `*(float*)(material+0x34)`
    /// (EBOOT: `lfs f13,0x34(material)` / `stfs f13,0x18(B100)`). Same caveat as
    /// <see cref="RuntimeMaterialValue58"/> — this is the pre-patch disk value.</summary>
    public float RuntimeMaterialValue34;

    /// <summary>Raw, unidentified 4 bytes at +0x1C.</summary>
    public uint Unknown1C_1F;

    /// <summary>PROVEN direct index into the old-engine shader/material table (main.dat section
    /// 0x5000, <see cref="Shaders.ShaderMetadataOld"/>, record size 0x80) — EBOOT 0x574034-0x574054:
    /// `lhz r11,0x20(record)` then `material = materialTableBase + MaterialIndex * 0x80`. Resolve
    /// through the EXISTING old-engine material path (MaterialReader.GetMaterialByIndex), which
    /// already keys ShaderMetadataOld by this exact table-position index for every other old-engine
    /// mesh type — no Shrub-specific texture/shader resolver needed or wanted.</summary>
    public ushort MaterialIndex;

    /// <summary>On-disk value at +0x22. PROVEN overwritten at load time with either 0x0202 or 0x0203
    /// depending on the resolved material's flags/renderingMode bytes (EBOOT: `li r12,0x0202` /
    /// `li r30,0x0203` / `sth ...,0x22(record)`). Not an asset id — some kind of runtime render
    /// dispatch code, picked per-material. Pre-patch disk value only; kept for reverse/debug.</summary>
    public ushort RuntimeRenderCode;

    /// <summary>Raw, unidentified 12 bytes at +0x24..+0x2F.</summary>
    public byte[] Unknown24_2F;

    public static ShrubMetadataOld Read(StreamHelper sh, uint recordBase)
    {
        var m = new ShrubMetadataOld
        {
            Resource9000Offset = sh.ReadUInt32(recordBase + 0x00),
            Unknown04 = sh.ReadUInt32(recordBase + 0x04),
            Unknown08 = sh.ReadUInt32(recordBase + 0x08),
            Flags0C = sh.ReadUInt16(recordBase + 0x0C),
            Unknown0E = sh.ReadUInt16(recordBase + 0x0E),
            Unknown10_13 = sh.ReadUInt32(recordBase + 0x10),
            Unknown1C_1F = sh.ReadUInt32(recordBase + 0x1C),
            MaterialIndex = sh.ReadUInt16(recordBase + 0x20),
            RuntimeRenderCode = sh.ReadUInt16(recordBase + 0x22),
        };

        sh.Seek(recordBase + 0x14);
        m.RuntimeMaterialValue58 = sh.ReadSingle();
        sh.Seek(recordBase + 0x18);
        m.RuntimeMaterialValue34 = sh.ReadSingle();

        sh.Seek(recordBase + 0x24);
        m.Unknown24_2F = sh.ReadBytes(12);

        return m;
    }

    public readonly byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
