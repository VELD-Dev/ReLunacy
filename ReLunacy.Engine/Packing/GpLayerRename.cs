using System.Buffers.Binary;
using System.Text;

namespace ReLunacy.Engine.Packing;

public sealed partial class GpPriusDocument
{
    /// <summary>Writes another name into the layer's 0x40-byte field of 0x25008 (same size, nothing moves).</summary>
    public GpEditResult SetLayerName(int layer, string name)
    {
        if (LayerNameProblem(name) is { } bad) return GpEditResult.Refuse(bad);
        var layers = Section(LayerTable);
        if (layers == null || layer < 0 || layer >= LayerCount) return GpEditResult.Refuse($"no layer {layer}");
        string old = LayerName(layer);
        var field = layers.Data.AsSpan(layer * layers.Element, 0x40);
        field.Clear();
        Encoding.ASCII.GetBytes(name).CopyTo(field);
        Edited = true;
        var result = new GpEditResult { Applied = true };
        result.Changes.Add($"layer {old} renamed {name}");
        return result;
    }

    /// <summary>Rewrites the NUL-terminated string a pointer field names inside <paramref name="pool"/>: in place (the later pointers
    /// move) when nothing else points into it, else a string of its own at the end of the pool.</summary>
    internal void RewriteString(GpSection fieldSection, int field, GpSection pool, string name)
    {
        var pointer = PointerAt(fieldSection, field) ?? throw new InvalidDataException("no name pointer");
        if (pointer.Target != pool) throw new InvalidDataException("the name is not in the expected pool");
        byte[] bytes = [.. Encoding.ASCII.GetBytes(name), 0];
        int at = pointer.TargetAt;
        int oldLength = Array.IndexOf(pool.Data, (byte)0, at) - at + 1;
        bool shared = oldLength <= 0 || _pointers.Any(p => !ReferenceEquals(p, pointer) && p.Target == pool && p.TargetAt >= at && p.TargetAt < at + oldLength);
        if (shared)
        {
            int end = pool.Data.Length;
            Splice(pool, end, 0, bytes);
            SetPointer(fieldSection, field, pool, end);
        }
        else Splice(pool, at, oldLength, bytes, pointer);
    }
}

/// <summary>What <see cref="GpLayers.Rename"/> did.</summary>
public sealed class GpLayerRenameResult
{
    public bool Applied { get; internal set; }
    public byte[]? Metadata { get; internal set; }
    public byte[]? Gameplay { get; internal set; }
    public List<string> Changes { get; } = [];
    public List<string> Problems { get; } = [];
}

public static partial class GpLayers
{
    /// <summary>
    /// Gives layers other names (by their index, so names may swap): the 0x40-byte record of gp_prius (<paramref name="doc"/>, edited in
    /// place), metadata.dat's name string and gameplay.dat's name string. The layer index, every mask bit and every reference are
    /// untouched (layers are matched by name only between these lists). One-region levels only. Nothing is changed when a problem
    /// is reported. Names are not hashed anywhere: they were searched for in every file of a level.
    /// </summary>
    public static GpLayerRenameResult Rename(GpPriusDocument doc, byte[] metadataDat, byte[] gameplayDat, IReadOnlyDictionary<int, string> renames)
    {
        var result = new GpLayerRenameResult();
        void Refuse(string why) => result.Problems.Add($"layers not renamed: {why}");
        int n = doc.LayerCount;
        var names = Enumerable.Range(0, n).Select(doc.LayerName).ToList();
        foreach (var (layer, name) in renames)
        {
            if (layer < 0 || layer >= n) { Refuse($"no layer {layer}"); return result; }
            if (GpPriusDocument.LayerNameProblem(name) is { } bad) { Refuse(bad); return result; }
        }
        var final = names.ToList();
        foreach (var (layer, name) in renames) final[layer] = name;
        if (final.Distinct().Count() != final.Count) { Refuse("two layers would have the same name"); return result; }

        GpPriusDocument meta;
        try { meta = GpPriusDocument.Parse(metadataDat); }
        catch (InvalidDataException e) { Refuse($"metadata.dat: {e.Message}"); return result; }
        var head = meta.Section(0x25098);
        var strings = meta.Section(0x25100);
        var pointers = meta.Section(0x25110);
        if (head == null || strings == null || pointers == null || GpPriusDocument.Word(head, 4) != n || pointers.Data.Length != 4 * n
            || !Enumerable.Range(0, n).All(l => GpPriusDocument.String(meta.PointerAt(pointers, 4 * l)) == names[l]))
        { Refuse("metadata.dat does not list the region's layers as gp_prius does"); return result; }

        Ighw02 gameplay;
        try { gameplay = Ighw02.Parse(gameplayDat); }
        catch (InvalidDataException e) { Refuse($"gameplay.dat: {e.Message}"); return result; }
        var gRoot = gameplay.Chunks.Find(c => c.Id == 0x25000);
        if (gRoot == null || gRoot.Data.Length is not (0x50 or 0x68)) { Refuse("gameplay.dat has no 0x25000 of a known size"); return result; }
        int regionAt = gRoot.Data.Length == 0x68 ? 0x58 : 0x40;
        var gNames = gameplay.PointerAt(gRoot, 0);
        if (gNames == null) { Refuse("gameplay.dat's layer list is missing"); return result; }
        if (GpPriusDocument.Word(gRoot.Data, regionAt) != 1) { Refuse("the level has several regions; layers are only renamed on one-region levels"); return result; }
        if (GpPriusDocument.Word(gRoot.Data, 4) != n
            || !Enumerable.Range(0, n).All(l => gameplay.String(gameplay.PointerAt(gNames.Target, gNames.TargetAt + 4 * l)) == names[l]))
        { Refuse("gameplay.dat does not list the region's layers as gp_prius does"); return result; }

        try
        {
            foreach (var (layer, name) in renames.OrderBy(r => r.Key))
            {
                var made = doc.SetLayerName(layer, name);
                result.Changes.AddRange(made.Changes);
                meta.RewriteString(pointers, 4 * layer, strings, name);
                var p = gameplay.PointerAt(gNames.Target, gNames.TargetAt + 4 * layer)!;
                var data = p.Target.Data;
                int at = p.TargetAt, oldLength = Array.IndexOf(data, (byte)0, at) - at + 1;
                if (oldLength <= 0) throw new InvalidDataException("a layer name is not terminated");
                gameplay.Splice(p.Target, at, oldLength, [.. Encoding.ASCII.GetBytes(name), 0]);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or InvalidDataException)
        { Refuse(e.Message); return result; }
        result.Metadata = meta.Serialize();
        result.Gameplay = gameplay.Serialize();
        result.Applied = true;
        return result;
    }
}

public sealed partial class GpPriusDocument
{
    /// <summary>Why <paramref name="name"/> can not name a zone (region.dat holds it in a pool), or null.</summary>
    public static string? ZoneNameProblem(string name)
    {
        if (string.IsNullOrEmpty(name)) return "empty";
        if (name.Length > 63) return "up to 63 characters";
        foreach (char c in name) if (c <= ' ' || c > '~') return "ASCII, no spaces";
        return null;
    }
}
