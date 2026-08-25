using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Objects;
using ReLunacy.Engine.Loading.Vertices;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>Reads old-engine "Shrub" assets: main.dat section 0xB100 (see
/// <see cref="ShrubMetadataOld"/> for the naming note) and its spatial container at 0x9540 (see
/// <see cref="ShrubContainerHeaderOld"/>).
///
/// PHASE 1 READER — metadata and material resolution only, deliberately. No main.dat containing
/// both 0xB100 and 0x9540 was available while writing this (checked: neither this machine's game
/// data nor any Game Browser-scanned level had either section — see
/// <see cref="ScanForShrubSections"/>, added specifically so whoever DOES have real Tools of
/// Destruction level files can find one). Without a real sample there is no way to verify:
///   - the 9540 spatial container's record-&gt;0xB100 index mapping (so no placements/instances),
///   - the vertex layout at Resource9000Offset (so no geometry/meshes).
/// Building either would mean inventing exactly what the EBOOT reverse could not establish.
/// Everything below IS backed directly by EBOOT instructions or an existing, already-shipped
/// ReLunacy convention (0x9000/0x9100 as vertices.dat's shared old-engine vertex/index pools, per
/// TieMetadataOld/Moby.cs) — see each field's remarks in <see cref="ShrubMetadataOld"/> /
/// <see cref="ShrubContainerHeaderOld"/> for the specific proof.
///
/// New engine is not handled: 0xB100/0x9540 are old-engine section ids, and nothing has been
/// checked against a new-engine equivalent (if one even exists) — ReadAll returns empty rather
/// than guessing.</summary>
public sealed class ShrubReader
{
    private readonly FileManager _fileManager;
    private readonly MaterialReader _materialReader;

    public ShrubReader(FileManager fileManager, MaterialReader materialReader)
    {
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
        _materialReader = materialReader ?? throw new ArgumentNullException(nameof(materialReader));
    }

    public IReadOnlyList<Assets.Shrubs.Shrub> ReadAll()
    {
        if (!_fileManager.isOld) return [];
        if (!_fileManager.igfiles.TryGetValue("main.dat", out IGFile? main) || main is null) return [];

        var assets = main.QuerySection(ShrubMetadataOld.ID);
        if (assets.id != ShrubMetadataOld.ID || assets.count == 0)
        {
            Console.WriteLine("[Shrubs] none (no 0xB100 section, or new engine).");
            return [];
        }

        Console.WriteLine($"[Shrubs] B100 assets: {assets.count}");

        uint vertexPoolLength = GetVertexPoolLength();
        var result = new List<Assets.Shrubs.Shrub>((int)assets.count);
        uint minMaterial = uint.MaxValue, maxMaterial = 0;
        int valid9000Offsets = 0;

        for (uint i = 0; i < assets.count; i++)
        {
            uint recordBase = (uint)(assets.offset + ShrubMetadataOld.Size * i);
            var meta = ShrubMetadataOld.Read(main.sh, recordBase);

            if (meta.MaterialIndex < minMaterial) minMaterial = meta.MaterialIndex;
            if (meta.MaterialIndex > maxMaterial) maxMaterial = meta.MaterialIndex;
            if (vertexPoolLength > 0 && meta.Resource9000Offset < vertexPoolLength) valid9000Offsets++;

            // GetMaterialByIndex is the SAME resolver Tie/Moby/UFrag old-engine meshes already use
            // for their oldShaderIndex - MaterialIndex is proven to address the identical 0x5000
            // table the same way (see ShrubMetadataOld.MaterialIndex). No Shrub-specific texture or
            // shader lookup here on purpose. Never null: an out-of-range index falls back to
            // MaterialReader's default material, same as every other old-engine mesh type.
            var material = _materialReader.GetMaterialByIndex(meta.MaterialIndex);
            result.Add(new Assets.Shrubs.Shrub(recordBase, meta, material));
        }

        Console.WriteLine($"[Shrubs] material indices range {minMaterial}..{maxMaterial}");
        if (vertexPoolLength > 0)
            Console.WriteLine($"[Shrubs] 9000 offsets valid: {valid9000Offsets}/{assets.count}");

        ReadSpatialContainer(main);

        return result;
    }

    /// <summary>Length of vertices.dat's shared old-engine vertex pool (section 0x9000 — the same
    /// pool Tie/Moby/UFrag read from), used only as a bounds reference for
    /// <see cref="ShrubMetadataOld.Resource9000Offset"/> diagnostics. 0 if vertices.dat or that
    /// section isn't present.</summary>
    private uint GetVertexPoolLength()
    {
        if (!_fileManager.igfiles.TryGetValue("vertices.dat", out IGFile? vertices) || vertices is null)
            return 0;

        var vertexPool = vertices.QuerySection(VertexFormat0.OldID);
        return vertexPool.id == VertexFormat0.OldID ? vertexPool.length : 0;
    }

    /// <summary>Parses the 9540 header (if present) purely for diagnostics — see the class remarks
    /// for why this stops short of enumerating the 0x40-byte record table it points to.</summary>
    private static void ReadSpatialContainer(IGFile main)
    {
        var section = main.QuerySection(ShrubContainerHeaderOld.ID);
        if (section.id != ShrubContainerHeaderOld.ID)
        {
            Console.WriteLine("[Shrubs] 9540 not found (no spatial container - instances cannot be resolved for this level).");
            return;
        }

        var header = ShrubContainerHeaderOld.Read(main.sh, section.offset);
        if (header.Version != ShrubContainerHeaderOld.ExpectedVersion)
        {
            Console.WriteLine($"[Shrubs] WARNING: 9540 found but version={header.Version} (expected {ShrubContainerHeaderOld.ExpectedVersion}) - layout for this version is unverified, so the spatial container is skipped for this level. B100 assets/materials above still loaded normally.");
            return;
        }

        Console.WriteLine($"[Shrubs] 9540 found, version={header.Version}");

        if (header.Relative04 >= section.length)
        {
            Console.WriteLine($"[Shrubs] WARNING: 9540 Relative04 (0x{header.Relative04:X}) falls outside the section (length 0x{section.length:X}) - skipping table0.");
            return;
        }

        uint table0Offset = section.offset + header.Relative04;
        Console.WriteLine($"[Shrubs] table0 offset=0x{table0Offset:X}, record count candidate=unknown (not established from the EBOOT reverse - see ShrubContainerHeaderOld.Relative04)");
    }

    /// <summary>Opens <paramref name="mainDatPath"/> just far enough to read its section table (no
    /// texture/shader/geometry decode) and reports whether it has BOTH 0xB100 and 0x9540 - the two
    /// sections needed to actually finish this format. Intended for scanning a Game Browser's
    /// discovered Tools of Destruction levels to find a usable sample; see GameBrowserFrame's
    /// "Scan for Shrub sections" button. Never throws: any failure to open/parse the file is reported
    /// as "not usable" via the return value rather than propagating.</summary>
    public static bool ScanForShrubSections(string mainDatPath, out uint b100Count, out ushort headerVersion)
    {
        b100Count = 0;
        headerVersion = 0;

        try
        {
            using var stream = File.OpenRead(mainDatPath);
            var main = new IGFile(stream);

            var assets = main.QuerySection(ShrubMetadataOld.ID);
            var header = main.QuerySection(ShrubContainerHeaderOld.ID);

            bool hasB100 = assets.id == ShrubMetadataOld.ID && assets.count > 0;
            bool has9540 = header.id == ShrubContainerHeaderOld.ID;

            if (hasB100) b100Count = assets.count;
            if (has9540) headerVersion = main.sh.ReadUInt16(header.offset + 0x2C);

            return hasB100 && has9540;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Shrubs] scan of '{mainDatPath}' failed: {ex.Message}");
            return false;
        }
    }
}
