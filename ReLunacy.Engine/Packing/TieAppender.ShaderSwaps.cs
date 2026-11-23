using System.Buffers.Binary;
using ReLunacy.Engine.Loading.Meshes;
using ReLunacy.Engine.Loading.Objects;
using ReLunacy.Engine.Loading.Readers;

namespace ReLunacy.Engine.Packing;

public enum ShaderSwapKind { Moby, Tie, UFrag, Shrub }

/// <summary>Part is the displayed Moby bangle or the UFrag's index in the zone. Ties and shrubs use part zero.</summary>
public readonly record struct ShaderSwapSlot(ShaderSwapKind Kind, ulong Asset, int Part, ulong Original);

/// <summary>Mesh indices within the slot, captured from the loaded asset when the edit is staged.</summary>
public sealed record ShaderSwapEdit(ulong Replacement, IReadOnlyList<int> MeshIndices);

public static partial class TieAppender
{
    private const uint ClassShaderPalette = 0x5600, ZoneShaderPalette = 0x71A0;

    internal static Dictionary<string, byte[]> ApplyShaderSwaps(Func<string, byte[]?> read,
        IReadOnlyDictionary<ShaderSwapSlot, ShaderSwapEdit> swaps, List<string> changes)
    {
        var level = new Level { Read = read };
        const string lookupName = "assetlookup.dat";
        byte[] lookup = (byte[])(level.Get(lookupName) ?? throw new InvalidDataException("assetlookup.dat not found")).Clone();
        var shaders = Pointers(lookup, NewEngineAssetLookup.ShaderPointers);
        var shaderMasks = Masks(lookup, NewEngineAssetLookup.ShaderRegionMasks);
        if (shaders.Count != shaderMasks.Length) throw new InvalidDataException("the shader pointer and region mask tables differ in length");
        var availability = shaders.Select((row, i) => (row.Tuid, Mask: shaderMasks[i])).ToDictionary(s => s.Tuid, s => s.Mask);

        long residentGrowth = 0;
        foreach (var family in swaps.GroupBy(s => s.Key.Kind))
        {
            var (name, section) = family.Key switch
            {
                ShaderSwapKind.Moby => ("mobys.dat", NewEngineAssetLookup.MobyPointers),
                ShaderSwapKind.Tie => ("ties.dat", NewEngineAssetLookup.TiePointers),
                ShaderSwapKind.UFrag => ("zones.dat", NewEngineAssetLookup.ZonePointers),
                ShaderSwapKind.Shrub => ("shrubs.dat", NewEngineAssetLookup.ShrubPointers),
                _ => throw new InvalidDataException("unknown shader swap asset type"),
            };
            var pointers = Pointers(lookup, section);
            var masks = Masks(lookup, section + NewEngineAssetLookup.RegionMaskSectionOffset);
            if (pointers.Count != masks.Length) throw new InvalidDataException($"{name}: the pointer and region mask tables differ in length");
            byte[] original = level.Get(name) ?? throw new InvalidDataException($"{name} not found");
            byte[] file = (byte[])original.Clone();

            foreach (var asset in family.GroupBy(s => s.Key.Asset))
            {
                int row = pointers.FindIndex(p => p.Tuid == asset.Key);
                if (row < 0) throw new InvalidDataException($"{name}: asset {asset.Key:X16} is missing");
                var (_, offset, length) = pointers[row];
                if (pointers.Where((_, i) => i != row)
                    .Any(p => p.Length > 0 && p.Offset < (long)offset + length && (long)p.Offset + p.Length > offset))
                    throw new InvalidDataException($"{name}: asset {asset.Key:X16} overlaps another asset");
                byte[] blob = file.AsSpan(offset, length).ToArray();
                var edits = asset.ToArray();
                foreach (var (slot, edit) in edits)
                    if (!availability.TryGetValue(edit.Replacement, out uint shaderMask) || (shaderMask & masks[row]) != masks[row])
                        throw new InvalidDataException($"{slot.Kind} {slot.Asset:X16}: shader {edit.Replacement:X16} must be loaded in every region that uses this asset");

                byte[] patched = PatchShaderSlots(blob, family.Key, edits);
                if (patched.Length == length)
                    patched.CopyTo(file, offset);
                else
                    file = Splice(file, pointers, row, patched);

                foreach (var (slot, edit) in edits)
                    changes.Add($"{slot.Kind} {slot.Asset:X16}, slot {slot.Part}: shader {slot.Original:X16} → {edit.Replacement:X16}");
            }

            Rewrite(lookup, section, pointers);
            level.Set(name, file);
            residentGrowth += IghwRelayout.Align(file.Length, 0x80) - IghwRelayout.Align(original.Length, 0x80);
        }

        if (!lookup.AsSpan().SequenceEqual(level.Files[lookupName])) level.Set(lookupName, lookup);
        if (residentGrowth > 0)
        {
            byte[] stats = (byte[])(level.Get("assetstats.dat") ?? throw new InvalidDataException("assetstats.dat not found")).Clone();
            var resident = stats.AsSpan(StatsOffset(stats) + StatsResident);
            BinaryPrimitives.WriteUInt32BigEndian(resident, checked((uint)(BinaryPrimitives.ReadUInt32BigEndian(resident) + residentGrowth)));
            level.Set("assetstats.dat", stats);
        }
        if (level.Changed.Any(name => level.Files[name].Length != level.OriginalLength[name]))
        {
            var problems = new List<string>();
            UpdateToc(level, problems);
            if (problems.Count != 0) throw new InvalidDataException(string.Join("; ", problems));
        }
        return level.Changed.ToDictionary(name => name, name => level.Files[name], StringComparer.OrdinalIgnoreCase);
    }

    private static byte[] PatchShaderSlots(byte[] blob, ShaderSwapKind kind,
        IReadOnlyList<KeyValuePair<ShaderSwapSlot, ShaderSwapEdit>> edits)
    {
        var sections = IghwRelayout.Sections(blob);
        uint paletteId = kind == ShaderSwapKind.UFrag ? ZoneShaderPalette : ClassShaderPalette;
        var paletteSection = Section(paletteId);
        if (paletteSection.Size <= 0 || paletteSection.Size % sizeof(ulong) != 0)
            throw new InvalidDataException("the shader palette does not contain whole TUIDs");
        var original = new List<ulong>();
        for (int at = paletteSection.Offset; at < paletteSection.End; at += sizeof(ulong))
            original.Add(BinaryPrimitives.ReadUInt64BigEndian(blob.AsSpan(at, sizeof(ulong))));

        if (kind == ShaderSwapKind.Shrub)
        {
            var (slot, edit) = edits.Single();
            ulong replacement = edit.Replacement;
            if (original.Count != 1 || (original[0] != slot.Original && original[0] != replacement))
                throw new InvalidDataException($"Shrub {slot.Asset:X16}: the original material has changed");
            BinaryPrimitives.WriteUInt64BigEndian(blob.AsSpan(paletteSection.Offset), replacement);
            return blob;
        }

        var palette = new List<ulong>(original);
        var writes = new Dictionary<int, (ushort Index, bool IsByte)>();
        var bindings = kind switch
        {
            ShaderSwapKind.Moby => MobyShaderBindings(blob, Section(NewMoby.ID)),
            ShaderSwapKind.Tie => TieShaderBindings(blob, Section(TieMetadataNew.ID),
                IghwRelayout.Find(sections, 0x3900), original.Count),
            ShaderSwapKind.UFrag => UFragShaderBindings(Section(UFragMetadata.ID), edits),
            _ => throw new InvalidDataException("unknown shader swap asset type"),
        };

        var sharedBindings = bindings.Values.SelectMany(fields => fields).GroupBy(field => field.Offset)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        foreach (var (slot, edit) in edits)
        {
            ulong replacement = edit.Replacement;
            if (!bindings.TryGetValue(slot.Part, out var fields))
                throw new InvalidDataException($"{kind} {slot.Asset:X16}: slot {slot.Part} is missing");
            int replacementIndex = palette.IndexOf(replacement);
            if (replacementIndex < 0)
            {
                replacementIndex = palette.Count;
                palette.Add(replacement);
            }

            if (edit.MeshIndices.Count == 0)
                throw new InvalidDataException($"{kind} {slot.Asset:X16}: no meshes were selected");
            foreach (int mesh in edit.MeshIndices)
            {
                if (mesh < 0 || mesh >= fields.Count)
                    throw new InvalidDataException($"{kind} {slot.Asset:X16}: mesh {mesh} is missing");
                var (offset, isByte, mirror) = fields[mesh];
                int index = isByte ? blob[offset] : BinaryPrimitives.ReadUInt16BigEndian(blob.AsSpan(offset));
                if (index >= original.Count || (original[index] != slot.Original && original[index] != replacement))
                    throw new InvalidDataException($"{kind} {slot.Asset:X16}: the original material has changed");
                if (original[index] == replacement) continue;
                if (sharedBindings.Contains(offset))
                    throw new InvalidDataException($"{kind} {slot.Asset:X16}: this mesh record is shared by multiple bangles");
                if (replacementIndex > (isByte ? byte.MaxValue : ushort.MaxValue - 1))
                    throw new InvalidDataException($"{kind} {slot.Asset:X16}: the shader palette is full");
                Write(offset, checked((ushort)replacementIndex), isByte);
                if (mirror is int at && BinaryPrimitives.ReadUInt16BigEndian(blob.AsSpan(at)) == index)
                    Write(at, checked((ushort)replacementIndex), false);
            }
        }

        foreach (var (offset, value) in writes)
        {
            if (value.IsByte) blob[offset] = (byte)value.Index;
            else BinaryPrimitives.WriteUInt16BigEndian(blob.AsSpan(offset), value.Index);
        }
        if (palette.Count == original.Count) return blob;

        var bytes = new byte[palette.Count * sizeof(ulong)];
        for (int i = 0; i < palette.Count; i++)
            BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(i * sizeof(ulong)), palette[i]);
        return IghwRelayout.Relayout(blob, new Dictionary<uint, byte[]> { [paletteId] = bytes });

        IghwRelayout.Section Section(uint id) => IghwRelayout.Find(sections, id)
            ?? throw new InvalidDataException($"asset has no 0x{id:X} section");

        void Write(int offset, ushort index, bool isByte)
        {
            if (writes.TryGetValue(offset, out var existing) && existing.Index != index)
                throw new InvalidDataException("two edits target the same shader reference");
            writes[offset] = (index, isByte);
        }
    }

    private readonly record struct ShaderBinding(int Offset, bool IsByte = false, int? Mirror = null);

    private static Dictionary<int, List<ShaderBinding>> MobyShaderBindings(byte[] blob, IghwRelayout.Section header)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(blob.AsSpan(header.Offset + 0x18));
        int bangles = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(header.Offset + 0x24));
        var bindings = new Dictionary<int, List<ShaderBinding>>();
        for (int i = 0; i < count; i++)
        {
            int bangle = checked(bangles + i * (int)MobyBangle.Size);
            int meshes = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(bangle));
            int meshCount = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(bangle + 4));
            if (meshCount == 0) continue;
            var fields = new List<ShaderBinding>();
            for (int mesh = 0; mesh < meshCount; mesh++)
                fields.Add(new ShaderBinding(checked(meshes + mesh * (int)MobyMesh.Size + 0x08)));
            bindings.Add(bindings.Count, fields);
        }
        return bindings;
    }

    private static Dictionary<int, List<ShaderBinding>> TieShaderBindings(byte[] blob, IghwRelayout.Section header,
        IghwRelayout.Section? materialSets, int paletteCount)
    {
        int count = blob[header.Offset + 0x0F];
        int meshes = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(header.Offset));
        int[] records = Enumerable.Range(0, count).Select(i => checked(meshes + i * (int)TieMesh.Size)).ToArray();
        int materials = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(header.Offset + 0x74));
        bool hasSet = materialSets != null && materials >= materialSets.Offset && materials <= materialSets.End - 4 * count;
        // The later layout carries 0x3900. Its alternate byte is still the shader index when every primitive uses slot zero.
        bool alternate = (hasSet || records.Any(at => blob[at + 0x26] != 0))
            && records.All(at => blob[at + 0x2A] == 0 && blob[at + 0x26] < paletteCount);
        var fields = new List<ShaderBinding>();
        for (int i = 0; i < records.Length; i++)
            fields.Add(new ShaderBinding(records[i] + (alternate ? 0x26 : 0x2A), true, hasSet ? materials + 4 * i : null));
        return new() { [0] = fields };
    }

    private static Dictionary<int, List<ShaderBinding>> UFragShaderBindings(IghwRelayout.Section records,
        IReadOnlyList<KeyValuePair<ShaderSwapSlot, ShaderSwapEdit>> edits)
    {
        var fields = new Dictionary<int, List<ShaderBinding>>();
        foreach (int index in edits.Select(e => e.Key.Part).Distinct())
        {
            if (index < 0 || index >= records.Size / (int)UFragMetadata.Size)
                throw new InvalidDataException($"UFrag {index}: record missing");
            fields.Add(index, [new ShaderBinding(records.Offset + index * (int)UFragMetadata.Size + 0x50)]);
        }
        return fields;
    }
}
