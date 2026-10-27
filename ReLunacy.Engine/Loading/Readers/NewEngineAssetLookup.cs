using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Objects;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>
/// Shared new-engine assetlookup.dat access.
///
/// Asset families such as shaders, cubemaps, TIEs, Mobys, zones and lighting all use the same
/// 0x10-byte IGHW pointer records (TUID, offset, length) into their sibling .dat payload file.
/// Keep that plumbing in one place so each semantic reader only has to decode its own payload.
/// </summary>
internal static class NewEngineAssetLookup
{
    // QForce retail dispatch (EBOOT 0x006AA48C) treats assetlookup as a fixed family directory: it
    // files every recognised section into a registry of 12-byte {pointerTable, regionMaskTable,
    // count} descriptors, using length>>4 as the count for pointer tables and length>>2 for the
    // 4-byte texture metadata.
    //
    // Each pointer family is shadowed by a section at id+1 holding ONE u32 PER RESOURCE: a bitmask
    // of the regions that need it, bit N being region N in gameplay.dat's region-name order. This
    // used to be modelled as an opaque "companion" table because a one-region level makes every
    // value 1. Over 88 881 values on 45 retail levels of three games, every value lies in
    // [1, 2^regionCount-1]; the zone family's masks equal the regions whose region.dat 0x1C010 lists
    // that zone (595/596); and assetstats.dat's eight family counters are exactly the per-region
    // peak of these masks (45/45). Texture metadata and high mips have no mask table of their own -
    // they are parallel to the resident texture table and inherit its masks.
    // See Docs/reverse/NEW_ENGINE_ASSETLOOKUP_ASSETSTATS_GAMEPLAY_2026-09-19.md.

    /// <summary>Single u32, 6 on every audited level. The retail dispatcher has no branch for it.</summary>
    public const uint Marker = 0x0001D000;

    public const uint ShaderPointers = 0x0001D100;
    public const uint ShaderRegionMasks = 0x0001D101;

    /// <summary>One 4-byte {format, mipCount, log2 width, log2 height} record per resident texture.</summary>
    public const uint TextureMetadata = 0x0001D140;

    public const uint TexturePointers = 0x0001D180;
    public const uint TextureRegionMasks = 0x0001D181;

    /// <summary>highmips.dat: the top mip of each texture, parallel to <see cref="TexturePointers"/>.</summary>
    public const uint HighmipPointers = 0x0001D1C0;

    public const uint CubemapPointers = 0x0001D200;
    public const uint CubemapRegionMasks = 0x0001D201;
    public const uint TiePointers = 0x0001D300;
    public const uint TieRegionMasks = 0x0001D301;

    /// <summary>foliages.dat. No QForce or Into the Nexus level ships it; 7 A Crack in Time levels do,
    /// and every one of their pointer ranges lies inside that file.</summary>
    public const uint FoliagePointers = 0x0001D400;
    public const uint FoliageRegionMasks = 0x0001D401;

    public const uint ShrubPointers = 0x0001D500;
    public const uint ShrubRegionMasks = 0x0001D501;
    public const uint MobyPointers = 0x0001D600;
    public const uint MobyRegionMasks = 0x0001D601;
    public const uint AnimsetPointers = 0x0001D700;
    public const uint AnimsetRegionMasks = 0x0001D701;

    /// <summary>cinematics.dat. Present on exactly the 31 audited levels that ship the file.</summary>
    public const uint CinematicPointers = 0x0001D800;
    public const uint CinematicRegionMasks = 0x0001D801;

    public const uint ZonePointers = 0x0001DA00;
    public const uint ZoneRegionMasks = 0x0001DA01;
    public const uint LightingPointers = 0x0001DB00;
    public const uint LightingRegionMasks = 0x0001DB01;
    public const uint HighlightPointers = 0x0001DB80;
    public const uint HighlightRegionMasks = 0x0001DB81;

    /// <summary>Distance from a pointer family's section id to its region-mask table.</summary>
    public const uint RegionMaskSectionOffset = 1;

    public static AssetPointer[] ReadPointers(FileManager fileManager, uint sectionId, string label)
    {
        if (fileManager.isOld)
            return [];

        if (!fileManager.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup) || assetlookup is null)
        {
            Console.WriteLine($"New-engine {label}: assetlookup.dat is unavailable.");
            return [];
        }

        IGFile.SectionHeader section = assetlookup.QuerySection(sectionId);
        if (section.id != sectionId || section.length == 0)
            return [];

        if (section.length % AssetPointer.Size != 0)
        {
            Console.WriteLine(
                $"New-engine {label}: assetlookup section 0x{sectionId:X} has byte length 0x{section.length:X}, " +
                $"not divisible by AssetPointer size 0x{AssetPointer.Size:X}; refusing a partial table.");
            return [];
        }

        uint count = section.length / AssetPointer.Size;
        assetlookup.sh.Seek(section.offset);
        return AssetPointer.ReadArray(assetlookup.sh, count);
    }

    /// <summary>
    /// Reads a family's per-resource region-mask table (the section at `pointers + 1`): one u32 per
    /// resource, bit N set when region N of gameplay.dat's region list needs it. Pass the mask
    /// section id, not the pointer section id.
    ///
    /// The returned array is positionally parallel to <see cref="ReadPointers"/> for the same
    /// family. Families without a mask table (texture metadata, high mips) return an empty array;
    /// they are parallel to the resident texture table and share its masks.
    /// </summary>
    public static uint[] ReadRegionMasks(FileManager fileManager, uint sectionId)
    {
        if (fileManager.isOld)
            return [];

        if (!fileManager.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup) || assetlookup is null)
            return [];

        IGFile.SectionHeader section = assetlookup.QuerySection(sectionId);
        if (section.id != sectionId || section.length == 0)
            return [];
        if (section.length % sizeof(uint) != 0)
            throw new InvalidDataException(
                $"assetlookup section 0x{sectionId:X} has byte length 0x{section.length:X}, not divisible by 4.");

        int count = checked((int)(section.length / sizeof(uint)));
        var values = new uint[count];
        assetlookup.sh.Seek(section.offset);
        for (int i = 0; i < values.Length; i++)
            values[i] = assetlookup.sh.ReadUInt32();
        return values;
    }

    public static bool TryGetPayloadStream(
        FileManager fileManager,
        string fileName,
        string label,
        out Stream? stream)
    {
        stream = null;
        if (!fileManager.rawfiles.TryGetValue(fileName, out Stream? source) || source is null)
        {
            Console.WriteLine($"New-engine {label}: {fileName} is unavailable.");
            return false;
        }

        stream = source;
        return true;
    }

    public static byte[] ReadPayload(Stream stream, in AssetPointer pointer, string label)
    {
        ulong end = (ulong)pointer.offset + pointer.length;
        if (end > (ulong)stream.Length)
        {
            throw new InvalidDataException(
                $"New-engine {label} {pointer.TUID:X16}: range 0x{pointer.offset:X}+0x{pointer.length:X} " +
                $"exceeds source length 0x{stream.Length:X}.");
        }

        int length = checked((int)pointer.length);
        var data = new byte[length];
        stream.Seek(pointer.offset, SeekOrigin.Begin);

        int read = 0;
        while (read < data.Length)
        {
            int n = stream.Read(data, read, data.Length - read);
            if (n == 0)
                throw new EndOfStreamException(
                    $"New-engine {label} {pointer.TUID:X16}: unexpected EOF after 0x{read:X}/0x{data.Length:X} bytes.");
            read += n;
        }

        return data;
    }
}
