using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>
/// Zone names, which live in the REGION and not in the zone.
///
/// MEASURED 2026-11-06 over every level of all four new-engine games (A4O, Into the Nexus, QForce,
/// A Crack in Time): each region.dat carries two positionally parallel tables - 0x1C010, the TUIDs of
/// the zones that region contains (8 bytes each, already read by RegionReader for membership), and
/// 0x1C000, one 4-byte offset per zone to its name string in the same file. Taking the union over a
/// level's regions names 1010 of 1010 zones, with zero disagreement where several regions list the
/// same zone - so a zone's name is a property of the zone, merely stored region-side.
///
/// This replaces ZoneMetadata, which could never have worked. It was reading section 0x74C0 of the
/// zone's own blob as (TUID, name pointer, padding), but in a zone blob:
///   0x72C0 is the TIE instance name table   (TieInstance.NameTableID - the same record shape)
///   0x74C0 is the SHRUB instance name table (ShrubInstance.NameTableID)
/// Neither is zone metadata, and neither exists on the old engine. So a zone with shrubs was named
/// after its first shrub placement ("nb_cr_tree_curved_2_5"), and a zone without them fell to
/// IGFile.QuerySection's all-zero miss, read offset 0 of the blob - the IGHW header - and ended up
/// with the Zone_TUID fallback. Every zone name in the editor was wrong, on every game.
/// </summary>
internal static class NewEngineZoneNames
{
    /// <summary>One u32 per zone: the offset of that zone's name string within region.dat.</summary>
    public const uint NamePointers = 0x1C000;

    /// <summary>One u64 per zone: which zones this region contains. Parallel to <see cref="NamePointers"/>.</summary>
    public const uint ZoneTuids = 0x1C010;

    /// <summary>Zone name by TUID, over every region of the level. Empty on the old engine, which has
    /// no zone list and no name table - its single synthetic zone keeps its fallback name.</summary>
    public static Dictionary<ulong, string> Read(FileManager fileManager)
    {
        var names = new Dictionary<ulong, string>();
        if (fileManager.isOld) return names;
        if (!fileManager.igfiles.TryGetValue("gameplay.dat", out IGFile? gameplay) || gameplay is null)
            return names;

        int conflicts = 0;
        foreach (string regionName in RegionReader.ReadRegionNames(gameplay))
        {
            // RegionReader opens the same file moments later, so both sides go through
            // GetOrLoadIGFile: LoadFile registers a new entry and throws on the second call.
            if (fileManager.GetOrLoadIGFile($"{regionName}/region.dat") is not IGFile region)
                continue;

            IGFile.SectionHeader tuids = region.QuerySection(ZoneTuids);
            IGFile.SectionHeader pointers = region.QuerySection(NamePointers);
            if (tuids.count == 0 || pointers.count == 0)
                continue;

            for (int i = 0; i < tuids.count && i < pointers.count; i++)
            {
                region.sh.Seek(tuids.offset + (long)i * 8);
                ulong tuid = region.sh.ReadUInt64();
                region.sh.Seek(pointers.offset + (long)i * 4);
                uint nameOffset = region.sh.ReadUInt32();
                if (nameOffset == 0) continue;

                string name = region.sh.ReadString(nameOffset);
                if (string.IsNullOrEmpty(name)) continue;
                if (names.TryGetValue(tuid, out string? existing) && existing != name)
                    conflicts++;
                names[tuid] = name;
            }
        }

        if (conflicts > 0)
            Console.WriteLine(
                $"[Zones] {conflicts} zone name(s) disagreed between regions; the last region read wins. " +
                "Measured as 0 on all 55 retail new-engine levels, so this is worth looking at.");
        return names;
    }
}
