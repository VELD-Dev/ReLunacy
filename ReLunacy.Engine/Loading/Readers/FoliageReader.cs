using System.Numerics;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Objects;
using ReLunacy.Engine.Loading.Objects.Instances;
using ReLunacy.Engine.Loading.Vertices;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>Reads old-engine foliage: the card sets in section 0xA200 and their placements in
/// 0x9340. See <see cref="FoliageMetadata"/> and <see cref="FoliageSpriteCorner"/> for the format
/// and the capture that confirms it.
///
/// New engine is not handled. Its equivalents have different section IDs (InsomniaToolset's
/// FoliageV2 family) and nothing has been verified against them, so ReadAll returns empty rather
/// than reading old-engine offsets out of a new-engine file.</summary>
public sealed class FoliageReader
{
    private readonly FileManager _fileManager;
    private readonly MaterialReader? _materialReader;

    /// <param name="materialReader">Resolves each asset's atlas from its direct texture index
    /// (A200+0x08 into the 0x5200 table). Optional: when null (e.g. a standalone geometry-only read)
    /// foliage still loads, just with no material, and the renderer falls back to the default
    /// billboard texture.</param>
    public FoliageReader(FileManager fileManager, MaterialReader? materialReader = null)
    {
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
        _materialReader = materialReader;
    }

    public IReadOnlyList<Assets.Foliage.Foliage> ReadAll()
    {
        if (!_fileManager.isOld) return [];
        if (!_fileManager.igfiles.TryGetValue("main.dat", out IGFile? main) || main is null) return [];
        if (!_fileManager.igfiles.TryGetValue("vertices.dat", out IGFile? vertices) || vertices is null) return [];

        IGFile.SectionHeader assets;
        try
        {
            assets = main.QuerySection(FoliageMetadata.ID);
        }
        catch
        {
            // A level with no foliage simply has no 0xA200 section - not an error worth failing
            // the whole level load over.
            return [];
        }

        var vertSection = vertices.QuerySection(VertexFormat0.OldID);
        var byOffset = new Dictionary<uint, int>();
        var result = new List<Assets.Foliage.Foliage>();

        for (uint i = 0; i < assets.count; i++)
        {
            uint recordBase = (uint)(assets.offset + FoliageMetadata.Size * i);
            var meta = FoliageMetadata.Read(main.sh, recordBase);

            var sprites = ReadSprites(vertices.sh, (uint)vertSection.offset, meta);

            // Resolve the atlas straight from the record's direct texture index (A200+0x08 →
            // 0x5200[index], see FoliageMetadata.TextureIndex). Null for the 0xFFFFFFFF sentinel, an
            // out-of-range index, or a null materialReader — the asset then keeps a null material
            // and the renderer draws the default billboard texture rather than crashing.
            var material = _materialReader?.GetFoliageMaterial(meta.TextureIndex);

            byOffset[recordBase] = result.Count;
            result.Add(new Assets.Foliage.Foliage(
                id: recordBase,
                metadata: meta,
                sprites: sprites,
                placements: [],
                material: material));
        }

        AttachPlacements(main, result, byOffset);
        LogSummary(result);
        return result;
    }

    /// <summary>Prints what was actually decoded. This is the only thing that exercises the reader
    /// end to end - the format was verified offline against the same bytes, but a silent zero here
    /// would otherwise look identical to a level that genuinely has no foliage. For metropolis the
    /// expected line is 2 assets, 117 sprites each, LODs 58/30/17/11/1, 757 placements total.</summary>
    private static void LogSummary(List<Assets.Foliage.Foliage> foliages)
    {
        if (foliages.Count == 0)
        {
            Console.WriteLine("Foliage: none (no 0xA200 section, or new engine).");
            return;
        }

        int placements = foliages.Sum(f => f.Placements.Count);
        Console.WriteLine($"Foliage: {foliages.Count} asset(s), {placements} placement(s).");
        foreach (var f in foliages)
        {
            var lods = string.Join("/", f.Metadata.SpriteLodRanges.Where(r => r.CornerCount > 0).Select(r => r.SpriteCount));
            // Report how the direct texture index resolved — an unresolved index or a wrong 0x5200
            // ordering would otherwise be invisible until the atlas rendered wrong on screen. For
            // metropolis both assets should read 0x5200[0]/[1] as 512x512 DXT5.
            string tex = !f.Metadata.HasTexture
                ? "none (0xFFFFFFFF)"
                : f.Material?.AlbedoTexture is { } a
                    ? $"0x5200[{f.Metadata.TextureIndex}] → {a.Width}x{a.Height} {a.Format}"
                    : $"{f.Metadata.TextureIndex} (unresolved)";
            Console.WriteLine($"  {f.Name}: {f.Sprites.Count} sprite(s) [LODs {lods}], " +
                              $"{f.Placements.Count} placement(s), texture {tex}");
        }
    }

    /// <summary>Expands the card set into flat sprite records. Each card is four corners of the
    /// per-corner array plus the one anchor record that four corners share - that grouping is the
    /// frequency=4 divisor from the capture, not an assumption about ordering.</summary>
    private static List<Assets.Foliage.FoliageSpriteCard> ReadSprites(StreamHelper sh, uint sectionOffset, in FoliageMetadata meta)
    {
        var cards = new List<Assets.Foliage.FoliageSpriteCard>();
        int total = meta.TotalCorners;
        if (total <= 0 || total % FoliageSpriteCorner.CornersPerSprite != 0) return cards;

        int spriteCount = meta.TotalSprites;
        long cornersEnd = sectionOffset + meta.SpriteCornerOffset + (long)total * FoliageSpriteCorner.Size;
        long anchorsEnd = sectionOffset + meta.SpriteAnchorOffset + (long)spriteCount * FoliageSpriteAnchor.Size;
        if (cornersEnd > sh.BaseStream.Length || anchorsEnd > sh.BaseStream.Length) return cards;

        var corners = new FoliageSpriteCorner[total];
        sh.Seek(sectionOffset + meta.SpriteCornerOffset);
        for (int i = 0; i < total; i++) corners[i] = FoliageSpriteCorner.Read(sh);

        var anchors = new FoliageSpriteAnchor[spriteCount];
        sh.Seek(sectionOffset + meta.SpriteAnchorOffset);
        for (int i = 0; i < spriteCount; i++) anchors[i] = FoliageSpriteAnchor.Read(sh);

        for (int s = 0; s < spriteCount; s++)
        {
            int c0 = s * FoliageSpriteCorner.CornersPerSprite;
            var offsets = new Vector2[FoliageSpriteCorner.CornersPerSprite];
            var uvs = new Vector2[FoliageSpriteCorner.CornersPerSprite];
            for (int k = 0; k < FoliageSpriteCorner.CornersPerSprite; k++)
            {
                var c = corners[c0 + k];
                offsets[k] = new Vector2(c.OffsetX, c.OffsetY);
                // V arrives negative (the file's atlas convention runs the opposite way to this
                // renderer's) - negate rather than clamp or abs, or a card samples the mirrored
                // quadrant instead of its own. See FoliageSpriteCorner.
                uvs[k] = new Vector2(c.U, -c.V);
            }

            var a = anchors[s];
            cards.Add(new Assets.Foliage.FoliageSpriteCard(
                Anchor: new Vector3(a.X, a.Y, a.Z),
                CornerOffsets: offsets,
                Uvs: uvs,
                Packed: (a.Packed0, a.Packed1),
                Lod: LodOf(meta, c0)));
        }

        return cards;
    }

    /// <summary>Which sprite LOD a corner index falls in. Ranges are consecutive and half-open, so
    /// the first range whose end is past the index owns it.</summary>
    private static int LodOf(in FoliageMetadata meta, int cornerIndex)
    {
        for (int i = 0; i < meta.SpriteLodRanges.Length; i++)
        {
            var r = meta.SpriteLodRanges[i];
            if (r.CornerCount > 0 && cornerIndex >= r.CornerBegin && cornerIndex < r.CornerEnd) return i;
        }
        return 0;
    }

    private static void AttachPlacements(IGFile main, List<Assets.Foliage.Foliage> assets, Dictionary<uint, int> byOffset)
    {
        IGFile.SectionHeader instances;
        try
        {
            instances = main.QuerySection(FoliageInstance.ID);
        }
        catch
        {
            return;
        }

        var lists = new List<Assets.Foliage.FoliagePlacement>[assets.Count];
        for (int i = 0; i < lists.Length; i++) lists[i] = [];

        for (uint i = 0; i < instances.count; i++)
        {
            uint recordBase = (uint)(instances.offset + FoliageInstance.Size * i);
            var inst = FoliageInstance.Read(main.sh, recordBase);

            // Instances whose pointer doesn't land on a parsed asset are dropped rather than
            // clamped to asset 0 - a mis-sized record would otherwise pile every placement onto
            // one plant and look like a loader that "worked".
            if (!byOffset.TryGetValue(inst.FoliageOffset, out int index)) continue;

            lists[index].Add(new Assets.Foliage.FoliagePlacement(inst.Transform, inst.BoundingSphere));
        }

        for (int i = 0; i < assets.Count; i++) assets[i].SetPlacements(lists[i]);
    }
}
