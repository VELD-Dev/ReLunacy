using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.Vertices;

namespace ReLunacy.Engine.Loading.Objects;

/// <summary>A foliage ASSET - the card set for one kind of plant, instanced across the level by
/// <see cref="Instances.FoliageInstance"/>. Old-engine section 0xA200, 176 bytes per record
/// (metropolis has exactly two).
///
/// InsomniaToolset's Foliage (its ID 0xC200 is a different engine revision and does not appear in
/// this game's files) matches this record's first 32 bytes field for field, which is what pins the
/// layout: branchLods lands at 0x20 only if the preceding nine fields are exactly as that struct
/// declares them. It is confirmed by the data rather than by trust - the four branch LOD entries
/// hold CONSECUTIVE index ranges (462117 + 156 = 462273, + 84 = 462357), which a wrong alignment
/// would not produce.
///
/// GEOMETRY OFFSETS POINT INTO vertices.dat SECTION 0x9000, not into main.dat. That is worth
/// stating because both files have a blob at a plausible address and reading them against main.dat
/// yields NaNs and values in the 1e38 range that look superficially like data. Against 0x9000 the
/// same bytes decode as clean sprite cards.</summary>
public record struct FoliageMetadata : ILunaSerializable
{
    public const uint ID = 0xA200;
    public const uint Size = 0xB0;

    /// <summary>Number of sprite LOD ranges actually populated (metropolis: 5 of the 5 slots).</summary>
    public const int MaxSpriteLods = 5;

    /// <summary>0x0F on both metropolis foliages. Not identified - a flag set, most likely.</summary>
    public uint Unk0;

    /// <summary>The only fields that differ between metropolis's two foliage assets, besides the
    /// geometry offsets: 0/0 on the first, 1/1 on the second. InsomniaToolset names them foliageId
    /// (u16 at 0x04) and textureIndex (u32 at 0x08).
    ///
    /// TextureIndex is NOT an index into the level texture table. Metropolis's two foliage textures
    /// are at table indices 1286 and 1287, not 0 and 1, and no table anywhere in main.dat maps one
    /// to the other - searched for the values and for the record addresses, aligned, across the
    /// whole file, zero hits. What DOES resolve is the shader: the only references to texture 1286
    /// and 1287 in the entire file are shaders #626 and #627 (both RenderingMode.Blended, both
    /// DXT5). Foliage asset 0 pairs with the first of those and asset 1 with the second, so the
    /// resolution is positional over the foliage shaders. That is an inference from two samples,
    /// not something read out of the file - do not extend it to a level with more foliage types
    /// without checking.</summary>
    public ushort FoliageId;
    public ushort Unk6;
    public uint TextureIndex;

    public uint Unk5;
    public uint IndexOffset;
    public uint Null0;

    /// <summary>Branch geometry, in vertices.dat section 0x9000. Branches are the non-billboard
    /// part of a foliage asset (trunks/stems); NOT decoded yet - only the sprite cards are.</summary>
    public uint BranchVertexOffset;
    public uint Unk1;

    /// <summary>Four branch LODs, {indexOffset, numIndices, unk}. Ranges are consecutive.</summary>
    public FoliageBranchLod[] BranchLods;

    /// <summary>Start of the per-corner array (<see cref="FoliageSpriteCorner"/>) in vertices.dat
    /// section 0x9000. Length is <see cref="TotalCorners"/> * 8 bytes.</summary>
    public uint SpriteCornerOffset;

    /// <summary>Start of the per-sprite array (<see cref="FoliageSpriteAnchor"/>), immediately
    /// after the corner array. Length is (TotalCorners / 4) * 8 bytes. On metropolis the two arrays
    /// tile exactly: 468*8 = 3744 bytes of corners then 117*8 = 936 bytes of anchors, ending
    /// precisely where the next foliage asset's data begins.</summary>
    public uint SpriteAnchorOffset;

    public uint UsedSpriteLods;

    /// <summary>Sprite LOD ranges in CORNER units, {cornerBegin, cornerEnd, distance}. Consecutive
    /// and non-overlapping: metropolis gives [0..232) [232..352) [352..420) [420..464) [464..468),
    /// i.e. 58, 30, 17, 11 and finally 1 card - a foliage LOD chain down to a single billboard.</summary>
    public FoliageSpriteLodRange[] SpriteLodRanges;

    /// <summary>Total corners across every LOD = the last range's end. Divide by 4 for cards.</summary>
    public readonly int TotalCorners =>
        SpriteLodRanges is { Length: > 0 } ? SpriteLodRanges[^1].CornerEnd : 0;

    public readonly int TotalSprites => TotalCorners / FoliageSpriteCorner.CornersPerSprite;

    public static FoliageMetadata Read(StreamHelper sh, uint recordBase)
    {
        var m = new FoliageMetadata
        {
            Unk0 = sh.ReadUInt32(recordBase + 0x00),
            FoliageId = sh.ReadUInt16(recordBase + 0x04),
            Unk6 = sh.ReadUInt16(recordBase + 0x06),
            TextureIndex = sh.ReadUInt32(recordBase + 0x08),
            Unk5 = sh.ReadUInt32(recordBase + 0x0C),
            IndexOffset = sh.ReadUInt32(recordBase + 0x10),
            Null0 = sh.ReadUInt32(recordBase + 0x14),
            BranchVertexOffset = sh.ReadUInt32(recordBase + 0x18),
            Unk1 = sh.ReadUInt32(recordBase + 0x1C),
            BranchLods = new FoliageBranchLod[4],
            SpriteLodRanges = new FoliageSpriteLodRange[MaxSpriteLods],
        };

        for (int i = 0; i < 4; i++)
        {
            uint b = recordBase + 0x20 + (uint)i * 8;
            m.BranchLods[i] = new FoliageBranchLod(sh.ReadUInt32(b), sh.ReadUInt16(b + 4), sh.ReadUInt16(b + 6));
        }

        m.SpriteCornerOffset = sh.ReadUInt32(recordBase + 0x40);
        m.SpriteAnchorOffset = sh.ReadUInt32(recordBase + 0x44);
        m.UsedSpriteLods = sh.ReadUInt32(recordBase + 0x48);

        for (int i = 0; i < MaxSpriteLods; i++)
        {
            uint b = recordBase + 0x50 + (uint)i * 8;
            ushort begin = sh.ReadUInt16(b);
            ushort end = sh.ReadUInt16(b + 2);
            sh.Seek(b + 4);
            m.SpriteLodRanges[i] = new FoliageSpriteLodRange(begin, end, sh.ReadSingle());
        }

        return m;
    }

    public readonly byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}

/// <summary>One branch LOD: a range into the foliage index buffer.</summary>
public readonly record struct FoliageBranchLod(uint IndexOffset, ushort IndexCount, ushort Unk);

/// <summary>One sprite LOD: a half-open range in CORNER units, plus its switch distance.</summary>
public readonly record struct FoliageSpriteLodRange(ushort CornerBegin, ushort CornerEnd, float Distance)
{
    public int CornerCount => CornerEnd - CornerBegin;
    public int SpriteCount => CornerCount / Vertices.FoliageSpriteCorner.CornersPerSprite;
}
