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

    /// <summary><see cref="TextureIndex"/> sentinel: this foliage asset binds no texture. The game
    /// tests the field against -1 and takes a fallback branch instead of indexing 0x5200 (EBOOT
    /// 0x4E2304 / 0x4E233C), so this must be resolved to "no texture", never used as an index.</summary>
    public const uint NoTexture = 0xFFFFFFFF;

    /// <summary>0x0F on both metropolis foliages. Not identified - a flag set, most likely.</summary>
    public uint Unk0;

    /// <summary>u16 @ 0x04. InsomniaToolset names this foliageId. It is 0 on BOTH metropolis
    /// foliages, so it is NOT the field that distinguishes the two assets - the earlier note here
    /// claimed 0x04 was the varying field (0/0 then 1/1), which is wrong for this sample. The pair
    /// that actually moves between the two assets is <see cref="Unk6"/> (0x06) and
    /// <see cref="TextureIndex"/> (0x08), each 0 on the first and 1 on the second.</summary>
    public ushort FoliageId;

    /// <summary>u16 @ 0x06. The renderer reads this directly off the live A200 pointer (EBOOT
    /// 0x51FFD0 and 0x52007C both `lhz rN,0x06(...)`), so it is a genuine per-asset selection/sort
    /// key rather than padding - but which exactly (material variant, render key, foliage type) is
    /// not pinned down, so it keeps a neutral name. Varies 0/1 across the two metropolis assets, in
    /// lockstep with <see cref="TextureIndex"/>. NOT needed to resolve the texture.</summary>
    public ushort Unk6;

    /// <summary>DIRECT physical index into the old-engine texture table (section 0x5200,
    /// <see cref="Textures.TextureMetadataOld"/>) - NOT a shader lookup. The game's own A200 loader
    /// proves it instruction for instruction: it reads this field (EBOOT 0x4E22F8, `lwz r14,0x08`),
    /// tests it against -1 (0x4E2304), and when it isn't the sentinel rewrites the slot in place as
    /// `section5200Base + TextureIndex * 0x20` (0x4E22B8..0x4E22CC multiply the index by 0x20 and add
    /// the section base the 0x5200 handler cached at manager+0x0C, EBOOT 0x4E2054). So the texture is
    /// TextureMetadataOld[TextureIndex], addressed by POSITION in the table, not by id/TUID.
    ///
    /// This supersedes the earlier shader-626/627 inference, which was a guess from two samples and
    /// is NOT what the loader does - there is no shader indirection and no 0/1 -&gt; 1286/1287 remap.
    /// The atlas bound for asset 0 is 0x5200 entry 0 (512x512 DXT5), for asset 1 entry 1; confirmed
    /// independently by the two descriptors' pixel offsets sitting exactly one full 512x512 BC3 mip
    /// chain (0x55580) apart.
    ///
    /// 0xFFFFFFFF is the "no texture" sentinel (see <see cref="NoTexture"/> / <see cref="HasTexture"/>).
    /// Resolve through TextureShaderLoader.ResolveOldTextureIndex, which handles both the sentinel
    /// and an out-of-range index.</summary>
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

    /// <summary>False when <see cref="TextureIndex"/> is the <see cref="NoTexture"/> sentinel, i.e.
    /// the game would take its no-texture fallback for this asset.</summary>
    public readonly bool HasTexture => TextureIndex != NoTexture;

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
