using System.Buffers.Binary;

namespace ReLunacy.Engine.Packing;

public sealed partial class GpPriusDocument
{
    /// <summary>
    /// The layout invariants every shipped file keeps (all 135 regions of the three games) and an edit must keep:
    /// record and metadata tables of a kind the same length, grouped by ascending layer; each layer's (pointer, count)
    /// and metadata pointer naming exactly its run (null when empty); every metadata entry named; 0x25005 / 0x25010
    /// layer counts; every pointer inside its target section. Empty when all hold.
    /// </summary>
    public List<string> Validate()
    {
        var bad = new List<string>();
        var layers = Section(LayerTable);
        var layerMeta = Section(LayerMetaTable);
        if (layers == null || layerMeta == null) { bad.Add("no layer tables"); return bad; }
        int slots = Math.Min((layers.Element - 0x40) / 8, layerMeta.Element / 4);
        foreach (var info in Kinds)
        {
            if (info.LayerSlot >= slots) continue;
            var records = RecordsOf(info.Kind);
            var meta = MetadataOf(info.Kind);
            if (records == null || meta == null)
            {
                for (int l = 0; l < LayerCount; l++)
                    if (Word(layers, l * layers.Element + 0x44 + 8 * info.LayerSlot) != 0) bad.Add($"layer {l}: {info.Kind} count without a table");
                continue;
            }
            if (records.Count != meta.Count) bad.Add($"{info.Kind}: {records.Count} records, {meta.Count} metadata");
            int previous = 0;
            for (int i = 0; i < meta.Count; i++)
            {
                int layer = Half(meta, i * 0x10 + 0x0C);
                if (layer < previous) { bad.Add($"{info.Kind}: not grouped by ascending layer at {i}"); break; }
                previous = layer;
                if (PointerAt(meta, i * 0x10 + 8) == null) bad.Add($"{info.Kind} {i}: no name");
                if (info.Kind == GpKind.Moby && Half(records, i * 0x50 + 2) != layer) bad.Add($"Moby {i}: record layer differs from metadata");
            }
            for (int l = 0; l < LayerCount; l++)
            {
                var (first, count) = LayerRun(info.Kind, l);
                int pair = l * layers.Element + 0x40 + 8 * info.LayerSlot;
                var p = PointerAt(layers, pair);
                var m = PointerAt(layerMeta, l * layerMeta.Element + 4 * info.LayerSlot);
                if (Word(layers, pair + 4) != count) bad.Add($"layer {l} {info.Kind}: count {Word(layers, pair + 4)} != {count}");
                if (count == 0)
                {
                    if (p != null || m != null || Word(layers, pair) != 0) bad.Add($"layer {l} {info.Kind}: empty but pointed");
                }
                else if (p == null || p.Target != records || p.TargetAt != first * info.RecordSize || m == null || m.Target != meta || m.TargetAt != first * 0x10)
                    bad.Add($"layer {l} {info.Kind}: first pointers wrong");
            }
        }
        if (Section(0x25005) is { } root && Word(root, 4) != LayerCount) bad.Add("0x25005 layer count stale");
        if (Section(0x25010) is { } region && Word(region, 0x44) != LayerCount) bad.Add("0x25010 layer count stale");
        foreach (var p in _pointers)
            if (p.At < 0 || p.At + 4 > p.Field.Data.Length || p.TargetAt < 0 || p.TargetAt > p.Target.Data.Length)
                bad.Add($"pointer {p.Field} 0x{p.At:X} -> {p.Target} 0x{p.TargetAt:X} out of range");
        return bad;
    }
}

/// <summary>
/// gameplay.dat's 0x25000 section sizes what every region of the level may hold: +0x10..+0x3C (and on All 4 One /
/// Into the Nexus +0x40..+0x50) are the MAXIMA over the level's regions (on all 40 levels of the three games), so they
/// are budgets, not per-region counts:
///   +0x10 gp_persist size, +0x14 gp_prius size, +0x18 volumes, +0x1C paths, +0x20 path points (each path rounded up
///   to even), +0x24 Areas, +0x28 Area list entries (each Area's paths + volumes rounded up to 16), +0x2C controllers,
///   +0x30 controller settings bytes, +0x34 Mobys, +0x38 Pods, +0x3C Pod entries;
///   All 4 One / Into the Nexus: +0x40 Squadrons, +0x44 Squadron entries, +0x48 Squadron-related (formula not found;
///   never changed here), +0x4C Clues, +0x50 Scents.
/// An edited region's values only ever raise a budget (a larger one is always safe); nothing is lowered, so an
/// unedited level writes identical bytes.
/// </summary>
public static class GameplayBudgets
{
    /// <summary>gameplay.dat with every budget raised to what the edited regions' gp_prius files need.</summary>
    public static (byte[] Bytes, List<string> Changes, List<string> Problems) Update(byte[] gameplayDat, IEnumerable<GpPriusDocument> editedRegions)
    {
        byte[] file = (byte[])gameplayDat.Clone();
        var changes = new List<string>();
        var problems = new List<string>();
        int section = -1, length = 0;
        if (file.Length >= 0x10 && BinaryPrimitives.ReadUInt32BigEndian(file) == GpPriusDocument.Magic)
        {
            uint count = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(8));
            for (int k = 0; k < count && 0x20 + k * 0x10 <= file.Length; k++)
                if (BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(0x10 + k * 0x10)) == 0x25000)
                {
                    section = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(0x10 + k * 0x10 + 4));
                    length = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(0x10 + k * 0x10 + 8));
                    break;
                }
        }
        if (section < 0 || section + Math.Max(length, 0x40) > file.Length)
        {
            problems.Add("gameplay.dat has no 0x25000 section; its budgets were not updated");
            return (gameplayDat, changes, problems);
        }
        foreach (var doc in editedRegions)
            foreach (var (field, value, label) in Needs(doc))
            {
                if (field + 4 > length) continue;
                uint held = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(section + field));
                if (value <= held) continue;
                BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(section + field), value);
                changes.Add($"{label} budget (+0x{field:X2}) {held} -> {value}");
            }
        return (file, changes, problems);
    }

    /// <summary>What one region needs of each budget: (field, value, name).</summary>
    public static List<(int Field, uint Value, string Label)> Needs(GpPriusDocument doc)
    {
        uint Count(GpKind k) => (uint)doc.Count(k);
        uint pathPoints = 0, areaEntries = 0, controllerBytes = 0, podEntries = 0, squadronEntries = 0;
        if (doc.RecordsOf(GpKind.Path) is { } paths)
            for (int i = 0; i < paths.Count; i++) pathPoints += (uint)((GpPriusDocument.Half(paths, i * 0x10 + 0x0E) + 1) & ~1);
        if (doc.RecordsOf(GpKind.Area) is { } areas)
            for (int i = 0; i < areas.Count; i++)
                areaEntries += (uint)((GpPriusDocument.Half(areas, i * 0x10 + 8) + GpPriusDocument.Half(areas, i * 0x10 + 0x0A) + 15) / 16 * 16);
        if (doc.RecordsOf(GpKind.Controller) is { } controllers)
            for (int i = 0; i < controllers.Count; i++) controllerBytes += GpPriusDocument.Word(controllers, i * 0x30 + 4);
        if (doc.RecordsOf(GpKind.Pod) is { } pods)
            for (int i = 0; i < pods.Count; i++) podEntries += GpPriusDocument.Word(pods, i * 0x10 + 4);
        if (doc.RecordsOf(GpKind.Squadron) is { } squadrons)
            for (int i = 0; i < squadrons.Count; i++) squadronEntries += GpPriusDocument.Word(squadrons, i * 0x10 + 4);
        var needs = new List<(int, uint, string)>
        {
            (0x14, (uint)doc.Serialize().Length, "gp_prius size"),
            (0x18, Count(GpKind.Volume), "volumes"), (0x1C, Count(GpKind.Path), "paths"), (0x20, pathPoints, "path points"),
            (0x24, Count(GpKind.Area), "Areas"), (0x28, areaEntries, "Area list entries"), (0x2C, Count(GpKind.Controller), "controllers"),
            (0x30, controllerBytes, "controller settings bytes"), (0x34, Count(GpKind.Moby), "Mobys"), (0x38, Count(GpKind.Pod), "Pods"),
            (0x3C, podEntries, "Pod entries"),
        };
        if (doc.Pooled)
            needs.AddRange([(0x40, Count(GpKind.Squadron), "Squadrons"), (0x44, squadronEntries, "Squadron entries"),
                (0x4C, Count(GpKind.Clue), "Clues"), (0x50, Count(GpKind.Scent), "Scents")]);
        return needs;
    }
}
