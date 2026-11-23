using System.Numerics;
using System.Text;
using ReLunacy.Engine.Assets.Foliage;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Objects;
using ReLunacy.Engine.Loading.Vertices;

namespace ReLunacy.Engine.Loading.Readers;

public sealed partial class FoliageReader
{
    private const uint GeometryBuffer = 0xA000;
    private const uint ClassName = 0xA600;
    private const uint ShaderLookup = 0x5600;
    private const uint ZoneClassLookup = 0x7400;
    private const uint ZonePlacementTable = 0x7440;
    private const uint ZonePlacementNames = 0x74C0;
    private const uint PlacementSize = 0x80;

    private IReadOnlyList<Foliage> ReadAllNew(LevelLoadProgress? progress)
    {
        if (!_fileManager.rawfiles.TryGetValue("foliages.dat", out var payload) || payload is null)
            return [];

        var placements = new Dictionary<ulong, List<FoliagePlacement>>();
        if (_fileManager.rawfiles.TryGetValue("zones.dat", out var zones) && zones is not null)
        {
            foreach (var pointer in NewEngineAssetLookup.ReadPointers(_fileManager, NewEngineAssetLookup.ZonePointers, "zones"))
            {
                using var data = new MemoryStream(NewEngineAssetLookup.ReadPayload(zones, pointer, "zone"), writable: false);
                CollectNewPlacements(new IGFile(data), pointer.TUID, placements);
            }
        }

        return ReadNew(placements, progress);
    }

    internal IReadOnlyList<Foliage> ReadNew(
        IReadOnlyDictionary<ulong, List<FoliagePlacement>> placements, LevelLoadProgress? progress)
    {
        if (!_fileManager.rawfiles.TryGetValue("foliages.dat", out var payload) || payload is null || placements.Count == 0)
            return [];

        var pointers = NewEngineAssetLookup.ReadPointers(_fileManager, NewEngineAssetLookup.FoliagePointers, "foliage")
            .Where(p => placements.ContainsKey(p.TUID)).ToArray();
        progress?.SetTotal(pointers.Length);
        var result = new List<Foliage>(pointers.Length);
        foreach (var pointer in pointers)
        {
            if (ReadNewClass(payload, pointer, placements[pointer.TUID]) is { } foliage)
                result.Add(foliage);
            progress?.Advance();
        }

        Console.WriteLine($"Foliage: {result.Count} asset(s), {result.Sum(f => f.Placements.Count)} placement(s).");
        return result;
    }

    private Foliage? ReadNewClass(Stream payload, AssetPointer pointer, IReadOnlyList<FoliagePlacement> placements)
    {
        byte[] bytes = NewEngineAssetLookup.ReadPayload(payload, pointer, "foliage");
        using var data = new MemoryStream(bytes, writable: false);
        var file = new IGFile(data);
        var sh = file.sh;
        var definition = file.QuerySection(FoliageMetadata.ID);
        var buffer = file.QuerySection(GeometryBuffer);
        if (!HasRecords(definition, 0x80, data.Length) || !HasRecords(buffer, 1, data.Length))
            return null;

        // The corners start at the beginning of 0xA000. The anchors follow at the byte offset
        // stored in the definition. Each range addresses the same corner and anchor arrays.
        var metadata = new FoliageMetadata
        {
            TextureIndex = FoliageMetadata.NoTexture,
            BranchLods = [],
            SpriteAnchorOffset = sh.ReadUInt32(definition.offset + 0x50),
            SpriteLodRanges = new FoliageSpriteLodRange[FoliageMetadata.MaxSpriteLods],
        };
        sh.Seek(definition.offset + 0x58);
        int end = 0;
        for (int i = 0; i < metadata.SpriteLodRanges.Length; i++)
        {
            var range = new FoliageSpriteLodRange(sh.ReadUInt16(), sh.ReadUInt16(), sh.ReadSingle());
            metadata.SpriteLodRanges[i] = range;
            if (range.CornerCount == 0) continue;
            if (range.CornerBegin != end || range.CornerCount < 0 ||
                range.CornerCount % FoliageSpriteCorner.CornersPerSprite != 0)
                return null;
            end = range.CornerEnd;
            metadata.UsedSpriteLods++;
        }

        if ((long)metadata.TotalCorners * FoliageSpriteCorner.Size > metadata.SpriteAnchorOffset)
            return null;
        var sprites = ReadSprites(sh, buffer.offset, (long)buffer.offset + buffer.length, metadata, isOld: false);
        if (sprites.Count == 0) return null;

        var shaders = file.QuerySection(ShaderLookup);
        ulong? shaderTuid = null;
        if (HasRecords(shaders, sizeof(ulong), data.Length))
        {
            sh.Seek(shaders.offset);
            shaderTuid = sh.ReadUInt64();
        }
        var material = shaderTuid is ulong shader ? _materialReader?.GetMaterialByTuid(shader) : null;
        var names = file.QuerySection(ClassName);
        string? name = null;
        if (HasRecords(names, 1, data.Length))
        {
            var field = bytes.AsSpan((int)names.offset, (int)names.length);
            int terminator = field.IndexOf((byte)0);
            if (terminator > 0) name = Encoding.UTF8.GetString(field[..terminator]);
        }

        return new Foliage(pointer.TUID, metadata, sprites, placements, material, name ?? $"Foliage_{pointer.TUID:X}")
        {
            ShaderTuid = shaderTuid,
        };
    }

    internal static void CollectNewPlacements(
        IGFile zone, ulong zoneTuid, Dictionary<ulong, List<FoliagePlacement>> placements)
    {
        var classes = zone.QuerySection(ZoneClassLookup);
        var instances = zone.QuerySection(ZonePlacementTable);
        var sh = zone.sh;
        long length = sh.BaseStream.Length;
        if (!HasRecords(classes, sizeof(ulong), length) || !HasRecords(instances, PlacementSize, length))
            return;

        var tuids = new ulong[classes.count];
        sh.Seek(classes.offset);
        for (int i = 0; i < tuids.Length; i++)
        {
            sh.Seek(classes.offset + (long)i * classes.length);
            tuids[i] = sh.ReadUInt64();
        }

        var names = zone.QuerySection(ZonePlacementNames);
        bool hasNames = names.count == instances.count && HasRecords(names, AssetPointer.Size, length);
        for (int i = 0; i < instances.count; i++)
        {
            long record = instances.offset + (long)i * instances.length;
            sh.Seek(record + 0x70);
            uint classIndex = sh.ReadUInt32();
            if (classIndex >= tuids.Length) continue;

            sh.Seek(record);
            var transform = new Matrix4x4(
                sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
                sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
                sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
                sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());
            var sphere = new Vector4(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());

            string? name = null;
            if (hasNames)
            {
                sh.Seek(names.offset + (long)i * names.length + sizeof(ulong));
                uint nameOffset = sh.ReadUInt32();
                if (nameOffset > 0 && nameOffset < length)
                    name = sh.ReadString(nameOffset);
            }

            ulong tuid = tuids[classIndex];
            if (!placements.TryGetValue(tuid, out var list))
                placements[tuid] = list = [];
            list.Add(new FoliagePlacement(transform, sphere) { Name = name, ZoneTuid = zoneTuid });
        }
    }

    private static bool HasRecords(IGFile.SectionHeader section, uint size, long fileLength) =>
        section.count > 0 && section.length >= size &&
        (ulong)section.offset + (ulong)section.count * section.length <= (ulong)fileLength;
}
