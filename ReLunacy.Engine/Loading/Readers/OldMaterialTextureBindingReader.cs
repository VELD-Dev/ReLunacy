using ReLunacy.Engine.Assets.Materials;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>
/// Reads the three old-engine material/texture binding tables from main.dat:
/// 0x3500 (per Tie definition), 0x6400 (per UFrag) and 0xD800 (per Moby prototype).
///
/// All three are the same 0x10-byte record and all three are handed to the same retail consumer;
/// see <see cref="OldMaterialTextureBinding"/> for the native route. This reader deliberately
/// parses the DISK form and never fabricates the four runtime texture ordinals that
/// 0x00584B50 writes over +0x08..+0x0F after load.
/// </summary>
public sealed class OldMaterialTextureBindingReader
{
    public const uint TieSectionId = 0x3500;
    public const uint UFragSectionId = 0x6400;
    public const uint MobyPrototypeSectionId = 0xD800;

    private const uint TieDefinitionSectionId = 0x3400;
    private const uint UFragSectionIdOwner = 0x6200;
    private const uint MobyPrototypeSectionIdOwner = 0xD100;
    private const uint MaterialSectionId = 0x5000;

    /// <summary>0x6200 +0x50, the UFrag's own primary material index that 0x6400 +0x08 mirrors.</summary>
    private const uint UFragPrimaryMaterialOffset = 0x50;

    private readonly FileManager _fileManager;

    public OldMaterialTextureBindingReader(FileManager fileManager)
    {
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
    }

    public OldMaterialTextureBindings Read()
    {
        if (!_fileManager.isOld) return OldMaterialTextureBindings.Empty;
        if (!_fileManager.igfiles.TryGetValue("main.dat", out IGFile? main) || main is null)
            return OldMaterialTextureBindings.Empty;
        return ReadFrom(main);
    }

    /// <summary>Parses the three tables out of an already-open main.dat container. Separate from
    /// <see cref="Read"/> so fixtures can exercise it without a FileManager and a level on disk.</summary>
    public static OldMaterialTextureBindings ReadFrom(IGFile main)
    {
        ArgumentNullException.ThrowIfNull(main);

        uint materialCount = main.QuerySection(MaterialSectionId).count;

        var tie = ReadTable(main, TieSectionId, OldMaterialTextureBindingOwner.Tie,
            main.QuerySection(TieDefinitionSectionId).count, materialCount);
        var uFrag = ReadTable(main, UFragSectionId, OldMaterialTextureBindingOwner.UFrag,
            main.QuerySection(UFragSectionIdOwner).count, materialCount);
        var moby = ReadTable(main, MobyPrototypeSectionId, OldMaterialTextureBindingOwner.MobyPrototype,
            main.QuerySection(MobyPrototypeSectionIdOwner).count, materialCount);

        var bindings = new OldMaterialTextureBindings(tie, uFrag, moby);
        if (!bindings.IsEmpty)
            Console.WriteLine(
                $"Material/texture bindings: 0x3500 {tie.Records.Count}, 0x6400 {uFrag.Records.Count}, " +
                $"0xD800 {moby.Records.Count} records over {materialCount} materials.");
        ReportUFragCompanionParity(main, uFrag);
        return bindings;
    }

    /// <summary>
    /// 0x6400 is a parallel array of 0x6200: record #i describes UFrag #i and retail resolves that
    /// UFrag's four physical texture slots from its +0x08 material index. Editor writes that changed one
    /// array without the other (a duplicate that did not append the companion, a delete that did not
    /// remove it) leave records describing the wrong UFrag, which is not visible in the editor - it
    /// shows up in game as UFrags sampling another material's textures. Report it at load; the repair
    /// itself lives in OldUFragCompanionRepairWriter.
    /// </summary>
    private static void ReportUFragCompanionParity(IGFile main, OldMaterialTextureBindingTable uFrag)
    {
        if (uFrag.Records.Count == 0) return;

        var definitions = main.QuerySection(UFragSectionIdOwner);
        if (definitions.id != UFragSectionIdOwner || definitions.count == 0 || definitions.length == 0) return;

        int mismatched = 0;
        uint shared = Math.Min(definitions.count, (uint)uFrag.Records.Count);
        for (uint i = 0; i < shared; i++)
        {
            main.sh.Seek(checked(definitions.offset + definitions.length * i + UFragPrimaryMaterialOffset));
            if (main.sh.ReadUInt16() != uFrag.Records[(int)i].MaterialIndex) mismatched++;
        }

        if (definitions.count == uFrag.Records.Count && mismatched == 0) return;

        Console.WriteLine(
            $"Material/texture binding 0x6400: {uFrag.Records.Count} record(s) for {definitions.count} UFrag(s)" +
            (mismatched == 0 ? string.Empty : $", and {mismatched} of the shared {shared} do not mirror their own " +
                                              "UFrag's 0x6200 +0x50 material") +
            ". Retail resolves every UFrag's texture slots from this table, so those UFrags bind the wrong " +
            "material's textures in game. Run the UFrag companion repair on this level before saving it again.");
    }

    private static OldMaterialTextureBindingTable ReadTable(
        IGFile main,
        uint sectionId,
        OldMaterialTextureBindingOwner owner,
        uint ownerCount,
        uint materialCount)
    {
        var section = main.QuerySection(sectionId);
        if (section.id != sectionId || section.count == 0) return OldMaterialTextureBindingTable.Empty;

        if (section.length != OldMaterialTextureBinding.Stride)
        {
            // Every audited retail level uses 0x10. A different stride is a container we have not
            // seen; refusing beats silently misreading every field by a shifting offset.
            Console.WriteLine(
                $"Material/texture binding 0x{sectionId:X4}: stride is 0x{section.length:X}, " +
                $"expected 0x{OldMaterialTextureBinding.Stride:X}; table skipped.");
            return OldMaterialTextureBindingTable.Empty;
        }

        var records = new List<OldMaterialTextureBinding>((int)section.count);
        int ownerOutOfRange = 0, secondaryOutOfRange = 0, materialOutOfRange = 0;
        int runtimeSlotsPopulated = 0, reservedNonZero = 0;

        for (uint i = 0; i < section.count; i++)
        {
            uint offset = checked(section.offset + OldMaterialTextureBinding.Stride * i);
            main.sh.Seek(offset);
            float metric = main.sh.ReadSingle();
            ushort reserved04 = main.sh.ReadUInt16();
            ushort field06 = main.sh.ReadUInt16();
            ushort materialIndex = main.sh.ReadUInt16();
            ushort slot1 = main.sh.ReadUInt16();
            ushort slot2 = main.sh.ReadUInt16();
            ushort slot3 = main.sh.ReadUInt16();

            if (reserved04 != 0) reservedNonZero++;
            if (slot1 != 0 || slot2 != 0 || slot3 != 0) runtimeSlotsPopulated++;
            // +0x06 is an owner ordinal only on 0x3500/0xD800. On 0x6400 it is the UFrag's
            // secondary material, so it is range-checked against the material table instead.
            if (owner == OldMaterialTextureBindingOwner.UFrag)
            {
                if (field06 != OldMaterialTextureBinding.NoSecondaryMaterial
                    && materialCount != 0 && field06 >= materialCount)
                    secondaryOutOfRange++;
            }
            else if (ownerCount != 0 && field06 >= ownerCount)
            {
                ownerOutOfRange++;
            }
            if (materialCount != 0 && materialIndex >= materialCount) materialOutOfRange++;

            records.Add(new OldMaterialTextureBinding
            {
                Table = owner,
                Index = (int)i,
                SourceOffset = offset,
                StreamingMetric = metric,
                Reserved04 = reserved04,
                Field06 = field06,
                MaterialIndex = materialIndex,
                ReservedRuntimeSlot1 = slot1,
                ReservedRuntimeSlot2 = slot2,
                ReservedRuntimeSlot3 = slot3,
            });
        }

        // These are reported, never repaired. A populated runtime slot on disk would mean the file
        // was saved after the loader's in-place fixup ran, which is exactly the corruption a
        // future writer must avoid producing.
        if (runtimeSlotsPopulated != 0)
            Console.WriteLine(
                $"Material/texture binding 0x{sectionId:X4}: {runtimeSlotsPopulated} of {section.count} records " +
                "have non-zero runtime texture slots at +0x0A..+0x0F; retail ships them zeroed.");
        if (reservedNonZero != 0)
            Console.WriteLine(
                $"Material/texture binding 0x{sectionId:X4}: {reservedNonZero} records have a non-zero +0x04, " +
                "which is 0 in every audited retail record.");
        if (ownerOutOfRange != 0)
            Console.WriteLine(
                $"Material/texture binding 0x{sectionId:X4}: {ownerOutOfRange} records name an owner ordinal " +
                $"outside the {ownerCount}-entry owner table.");
        if (secondaryOutOfRange != 0)
            Console.WriteLine(
                $"Material/texture binding 0x{sectionId:X4}: {secondaryOutOfRange} records name a secondary " +
                $"material outside the {materialCount}-entry 0x5000 table.");
        if (materialOutOfRange != 0)
            Console.WriteLine(
                $"Material/texture binding 0x{sectionId:X4}: {materialOutOfRange} records name a material ordinal " +
                $"outside the {materialCount}-entry 0x5000 table.");

        return new OldMaterialTextureBindingTable(sectionId, owner, section.offset, records);
    }
}
