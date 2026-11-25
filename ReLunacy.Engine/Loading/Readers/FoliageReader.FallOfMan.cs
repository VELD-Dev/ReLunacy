using System.Numerics;
using ReLunacy.Engine.Assets.Foliage;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Objects;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>
/// Fall of Man's foliage: main.dat 0xC200 classes (0x120 bytes) placed by 0x9700 records, which are Tools of
/// Destruction's 0x9340 layout (matrix, sphere at +0xB0, class pointer at +0xC4). Unlike Tools of Destruction
/// the cards are indexed geometry: the class's index range (vertices.dat 0x9100) holds the branch LODs first,
/// then the sprite LODs, each sprite a quad (0, 1, 2) (2, 1, 3) of 12-byte corners. Its anchors are float4s in
/// 0x9150, one per sprite in vertex order.
/// </summary>
public sealed partial class FoliageReader
{
    private const uint FallOfManClasses = 0xC200;
    private const uint FallOfManClassSize = 0x120;
    private const uint FallOfManPlacements = 0x9700;
    private const uint FallOfManCornerSize = 12;
    private const int FallOfManSpriteLods = 4;

    // Class record:
    //   +0x04 u16 id, +0x08 texture index (0x5300), +0x10 first index (u16 units),
    //   +0x18 branch vertex offset (20-byte Tie vertices), +0x20 four branch LODs {first index, u16 count, u16},
    //   +0x40 sprite vertex offset, +0x48 four sprite LODs {first index, end index, u32, f32},
    //   +0xB8 anchor pointer (0x9150).
    private IReadOnlyList<Foliage> ReadAllFallOfMan(LevelLoadProgress? progress)
    {
        if (!_fileManager.igfiles.TryGetValue("main.dat", out IGFile? main) || main is null) return [];
        if (!_fileManager.igfiles.TryGetValue("vertices.dat", out IGFile? vertices) || vertices is null) return [];
        var classes = main.QuerySection(FallOfManClasses);
        if (classes.count == 0 || classes.length != FallOfManClassSize) return [];
        uint vertexBase = vertices.QuerySection(0x9000).offset;
        long vertexEnd = vertices.sh.BaseStream.Length;

        var byOffset = new Dictionary<uint, int>();
        var result = new List<Foliage>();
        progress?.SetTotal(classes.count);
        for (uint i = 0; i < classes.count; i++)
        {
            progress?.Advance();
            uint record = classes.offset + FallOfManClassSize * i;
            var sh = main.sh;
            var lods = new (uint Begin, uint End, float Distance)[FallOfManSpriteLods];
            for (int l = 0; l < FallOfManSpriteLods; l++)
            {
                uint at = record + 0x48 + (uint)l * 16;
                lods[l] = (sh.ReadUInt32(at), sh.ReadUInt32(at + 4), ReadSingleAt(sh, at + 0xC));
            }
            // The sprite vertices start at the first sprite LOD's first index; every quad takes six indices.
            uint spriteStart = lods[0].Begin;
            uint spriteEnd = lods.Max(l => l.End);
            int corners = spriteEnd > spriteStart ? (int)(spriteEnd - spriteStart) / 6 * 4 : 0;
            var meta = new FoliageMetadata
            {
                Unk0 = sh.ReadUInt32(record),
                FoliageId = sh.ReadUInt16(record + 0x04),
                Unk6 = sh.ReadUInt16(record + 0x06),
                TextureIndex = sh.ReadUInt32(record + 0x08),
                IndexOffset = sh.ReadUInt32(record + 0x10),
                BranchVertexOffset = sh.ReadUInt32(record + 0x18),
                BranchLods = new FoliageBranchLod[4],
                SpriteCornerOffset = sh.ReadUInt32(record + 0x40),
                SpriteAnchorOffset = sh.ReadUInt32(record + 0xB8),
                UsedSpriteLods = (uint)lods.Count(l => l.End > l.Begin),
                SpriteLodRanges = lods.Select(l => new FoliageSpriteLodRange(
                    (ushort)Math.Clamp((long)(l.Begin - spriteStart) / 6 * 4, 0, ushort.MaxValue),
                    (ushort)Math.Clamp((long)(l.End - spriteStart) / 6 * 4, 0, ushort.MaxValue), l.Distance))
                    .Concat(Enumerable.Repeat(default(FoliageSpriteLodRange), FoliageMetadata.MaxSpriteLods - FallOfManSpriteLods))
                    .ToArray(),
            };
            for (int b = 0; b < 4; b++)
            {
                uint at = record + 0x20 + (uint)b * 8;
                meta.BranchLods[b] = new FoliageBranchLod(sh.ReadUInt32(at), sh.ReadUInt16(at + 4), sh.ReadUInt16(at + 6));
            }

            var sprites = ReadFallOfManSprites(vertices.sh, vertexBase + (long)meta.SpriteCornerOffset, vertexEnd,
                main.sh, meta.SpriteAnchorOffset, corners, meta);
            byOffset[record] = result.Count;
            result.Add(new Foliage(record, meta, sprites, [], _materialReader?.GetFoliageMaterial(meta.TextureIndex)));
        }

        AttachPlacements(main, result, byOffset, FallOfManPlacements);
        LogSummary(result);
        return result;
    }

    private static List<FoliageSpriteCard> ReadFallOfManSprites(StreamHelper vertices, long cornerStart, long vertexEnd,
        StreamHelper main, uint anchorStart, int corners, in FoliageMetadata meta)
    {
        var cards = new List<FoliageSpriteCard>();
        int sprites = corners / 4;
        if (sprites == 0 || cornerStart + corners * (long)FallOfManCornerSize > vertexEnd
            || anchorStart == 0 || anchorStart + sprites * 16L > main.BaseStream.Length)
            return cards;

        // The quad's corners are top-left, top-right, bottom-left, bottom-right; the card mesh fans 0-1-2, 0-2-3.
        ReadOnlySpan<int> fan = [0, 1, 3, 2];
        for (int s = 0; s < sprites; s++)
        {
            var offsets = new Vector2[4];
            var uvs = new Vector2[4];
            for (int k = 0; k < 4; k++)
            {
                vertices.Seek(cornerStart + (s * 4 + fan[k]) * (long)FallOfManCornerSize);
                offsets[k] = new Vector2((float)vertices.ReadHalf(), (float)vertices.ReadHalf());
                uvs[k] = new Vector2((float)vertices.ReadHalf(), -(float)vertices.ReadHalf());
            }
            main.Seek(anchorStart + s * 16L);
            var anchor = new Vector3(main.ReadSingle(), main.ReadSingle(), main.ReadSingle());
            cards.Add(new FoliageSpriteCard(anchor, offsets, uvs, (0, 0), LodOf(meta, s * 4)));
        }
        return cards;
    }

    private static float ReadSingleAt(StreamHelper sh, long offset)
    {
        sh.Seek(offset);
        return sh.ReadSingle();
    }
}
