using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.Vertices;

namespace ReLunacy.Engine.Loading.Objects;

/// <summary>A foliage asset - the card set for one kind of plant, instanced across the level by
/// <see cref="Instances.FoliageInstance"/>. Old-engine section 0xA200, 176 bytes per record.
/// Geometry offsets point into vertices.dat section 0x9000, not main.dat.</summary>
public record struct FoliageMetadata : ILunaSerializable
{
    public const uint ID = 0xA200;
    public const uint Size = 0xB0;

    /// <summary>Number of sprite LOD ranges actually populated (metropolis: 5 of the 5 slots).</summary>
    public const int MaxSpriteLods = 5;

    /// <summary><see cref="TextureIndex"/> sentinel meaning this foliage asset binds no texture.</summary>
    public const uint NoTexture = 0xFFFFFFFF;

    /// <summary>Unidentified, likely a flag set.</summary>
    public uint Unk0;

    /// <summary>u16 @ 0x04 (InsomniaToolset: foliageId).</summary>
    public ushort FoliageId;

    /// <summary>u16 @ 0x06. Per-asset selection/sort key read directly by the renderer; exact
    /// meaning (material variant, render key, foliage type) not pinned down. Not needed to resolve
    /// the texture.</summary>
    public ushort Unk6;

    /// <summary>Direct index into the old-engine texture table (section 0x5200,
    /// <see cref="Textures.TextureMetadataOld"/>), not a shader lookup: the texture is
    /// TextureMetadataOld[TextureIndex]. 0xFFFFFFFF is the "no texture" sentinel (see
    /// <see cref="NoTexture"/> / <see cref="HasTexture"/>). Resolve through
    /// TextureShaderLoader.ResolveOldTextureIndex, which handles both the sentinel and an
    /// out-of-range index.</summary>
    public uint TextureIndex;

    public uint Unk5;
    public uint IndexOffset;
    public uint Null0;

    /// <summary>Branch geometry offset in vertices.dat section 0x9000 (trunks/stems, the
    /// non-billboard part of a foliage asset). Not decoded yet - only the sprite cards are.</summary>
    public uint BranchVertexOffset;
    public uint Unk1;

    /// <summary>Four branch LODs, {indexOffset, numIndices, unk}. Ranges are consecutive.</summary>
    public FoliageBranchLod[] BranchLods;

    /// <summary>Start of the per-corner array (<see cref="FoliageSpriteCorner"/>) in vertices.dat
    /// section 0x9000. Length is <see cref="TotalCorners"/> * 8 bytes.</summary>
    public uint SpriteCornerOffset;

    /// <summary>Start of the per-sprite array (<see cref="FoliageSpriteAnchor"/>), immediately
    /// after the corner array. Length is (TotalCorners / 4) * 8 bytes.</summary>
    public uint SpriteAnchorOffset;

    public uint UsedSpriteLods;

    /// <summary>Sprite LOD ranges in corner units, {cornerBegin, cornerEnd, distance}. Consecutive
    /// and non-overlapping.</summary>
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
