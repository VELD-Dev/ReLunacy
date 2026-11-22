using System.Buffers.Binary;
using System.Text;

namespace ReLunacy.Engine.Packing;

public sealed partial class GpPriusDocument
{
    /// <summary>Why the layer cannot be removed (it still holds instances, or the file is not laid out as a layer addition
    /// leaves it), or null.</summary>
    public string? RemoveLayerProblem(int layer)
    {
        var layers = Section(LayerTable);
        var layerMeta = Section(LayerMetaTable);
        var root = Section(0x25005);
        var region = Section(0x25010);
        var list = Section(0x25014);
        if (layers == null || layerMeta == null || root == null || region == null || list == null)
            return "this region has no layer tables (0x25005 / 0x25008 / 0x2500C / 0x25010 / 0x25014)";
        int n = LayerCount;
        if (layer < 0 || layer >= n) return $"no layer {layer}";
        if (Word(root, 4) != n || Word(region, 0x44) != n || list.Data.Length != 2 * n)
            return "the layer counts of 0x25005 / 0x25010 / 0x25014 disagree with 0x25008";
        for (int l = 0; l < n; l++)
            if (Half(list, 2 * l) != l) return "the region does not list its layers in order (0x25014)";
        int slots = (layers.Element - 0x40) / 8;
        for (int s = 0; s < slots; s++)
        {
            // All 4 One / Into the Nexus's last slot is not a pointer and its meaning is not known.
            if (Pooled && s == slots - 1 ? Word(layers, layer * layers.Element + 0x40 + 8 * s + 4) != 0 || Word(layers, layer * layers.Element + 0x40 + 8 * s) != 0
                : Word(layers, layer * layers.Element + 0x40 + 8 * s + 4) != 0)
                return Pooled && s == slots - 1 ? "its last record slot is not empty" : "it still holds instances";
        }
        foreach (var info in Kinds)
        {
            if (MetadataOf(info.Kind) is not { } meta) continue;
            for (int i = 0; i < meta.Count; i++)
                if (Half(meta, i * 0x10 + 0x0C) == layer) return $"it still holds {info.Kind} instances";
        }
        return null;
    }

    /// <summary>
    /// The reverse of <see cref="AddLayer"/> for a layer of any place in the list, which must be empty (delete its instances and
    /// groups first). Every later layer moves down by one, so everything that holds a layer index follows:
    ///   - the metadata layer halfword (+0x0C) of every kind's every instance, and a Moby record's +0x02;
    ///   - A Crack in Time's inline reference words (kind, layer, index), wherever they are (Clue volume links, Pod / Area /
    ///     Scent lists, settings words): the ones that name an instance of a later layer have their layer field lowered. All 4
    ///     One / Into the Nexus name instances by TUID, which does not move;
    ///   - the layer's record in 0x25008 and 0x2500C, 0x25005 +4 and 0x25010 +0x44 (the counts), and the last entry of 0x25014,
    ///     which lists the layers 0..n-1.
    /// The caller brings metadata.dat, gameplay.dat and instances.lua along (<see cref="GpLayers.Remove"/>).
    /// </summary>
    public GpEditResult RemoveLayer(int layer)
    {
        if (RemoveLayerProblem(layer) is { } why) return GpEditResult.Refuse(why);
        var layers = Section(LayerTable)!;
        var layerMeta = Section(LayerMetaTable)!;
        var root = Section(0x25005)!;
        var region = Section(0x25010)!;
        var list = Section(0x25014)!;
        int n = LayerCount;
        string name = LayerName(layer);
        var result = new GpEditResult { Applied = true };

        // The words that name an instance of a later layer, found while the layout is still the old one.
        if (!Pooled && layer < n - 1)
            foreach (var (section, at) in ReferenceWords())
            {
                uint word = Word(section, at);
                if (DecodeInline(word) is not { } target || Layer(target) <= layer) continue;
                PutWord(section, at, word - 0x10000);
                result.ReferencesRewritten++;
            }

        // Every later instance's layer.
        foreach (var info in Kinds)
        {
            if (MetadataOf(info.Kind) is not { } meta) continue;
            var records = RecordsOf(info.Kind);
            for (int i = 0; i < meta.Count; i++)
            {
                ushort l = Half(meta, i * 0x10 + 0x0C);
                if (l <= layer) continue;
                PutHalf(meta, i * 0x10 + 0x0C, (ushort)(l - 1));
                if (info.Kind == GpKind.Moby && records != null) PutHalf(records, i * info.RecordSize + 2, (ushort)(l - 1));
            }
        }

        Splice(layers, layer * layers.Element, layers.Element, []);
        Splice(layerMeta, layer * layerMeta.Element, layerMeta.Element, []);
        PutWord(root, 4, (uint)(n - 1));
        PutWord(region, 0x44, (uint)(n - 1));
        Splice(list, list.Data.Length - 2, 2, []);

        result.Changes.Add($"layer {name} removed ({layer}); {n - 1 - layer} later layer(s) renumbered, {result.ReferencesRewritten} reference word(s) rewritten");
        return result;
    }
}

public sealed class GpLayerRemoveResult
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
    /// Takes the empty layer <paramref name="name"/> out of the three lists a layer joins (<see cref="Add"/> reversed), later layers
    /// moving down: gp_prius (<see cref="GpPriusDocument.RemoveLayer"/>, edited in place), metadata.dat (its name, pointer and zero
    /// word) and gameplay.dat (the name pointer and the name's own chunk; the masks lose the bit, which is the layer's index in the
    /// name list). One-region levels only, as for Add. Nothing is changed when a problem is reported.
    /// </summary>
    public static GpLayerRemoveResult Remove(GpPriusDocument doc, byte[] metadataDat, byte[] gameplayDat, string name)
    {
        var result = new GpLayerRemoveResult();
        void Refuse(string why) => result.Problems.Add($"layer {name} not removed: {why}");
        int n = doc.LayerCount;
        int layer = Enumerable.Range(0, n).ToList().FindIndex(l => doc.LayerName(l) == name);
        if (layer < 0) { Refuse("the file has no such layer"); return result; }
        if (doc.RemoveLayerProblem(layer) is { } blocked) { Refuse(blocked); return result; }
        var names = Enumerable.Range(0, n).Select(doc.LayerName).ToList();

        GpPriusDocument meta;
        try { meta = GpPriusDocument.Parse(metadataDat); }
        catch (InvalidDataException e) { Refuse($"metadata.dat: {e.Message}"); return result; }
        var head = meta.Section(0x25098);
        var strings = meta.Section(0x25100);
        var pointers = meta.Section(0x25110);
        var words = meta.Section(0x25104);
        if (head == null || strings == null || pointers == null || words == null || head.Data.Length < 0x10)
        { Refuse("metadata.dat has no layer list"); return result; }
        if (GpPriusDocument.Word(head, 4) != n || pointers.Data.Length != 4 * n || words.Data.Length != 4 * n
            || !Enumerable.Range(0, n).All(l => GpPriusDocument.String(meta.PointerAt(pointers, 4 * l)) == names[l]))
        { Refuse("metadata.dat does not list the region's layers as gp_prius does"); return result; }

        Ighw02 gameplay;
        try { gameplay = Ighw02.Parse(gameplayDat); }
        catch (InvalidDataException e) { Refuse($"gameplay.dat: {e.Message}"); return result; }
        var gRoot = gameplay.Chunks.Find(c => c.Id == 0x25000);
        if (gRoot == null || gRoot.Data.Length is not (0x50 or 0x68)) { Refuse("gameplay.dat has no 0x25000 of a known size"); return result; }
        int regionAt = gRoot.Data.Length == 0x68 ? 0x58 : 0x40;
        var gNames = gameplay.PointerAt(gRoot, 0);
        var gRegions = gameplay.PointerAt(gRoot, regionAt + 8);
        if (gNames == null || gRegions == null) { Refuse("gameplay.dat's layer or region list is missing"); return result; }
        if (GpPriusDocument.Word(gRoot.Data, regionAt) != 1) { Refuse("the level has several regions; layers are only removed on one-region levels"); return result; }
        if (GpPriusDocument.Word(gRoot.Data, 4) != n
            || !Enumerable.Range(0, n).All(l => gameplay.String(gameplay.PointerAt(gNames.Target, gNames.TargetAt + 4 * l)) == names[l]))
        { Refuse("gameplay.dat does not list the region's layers as gp_prius does"); return result; }
        ulong all = n == 64 ? ulong.MaxValue : (1UL << n) - 1;
        if (BinaryPrimitives.ReadUInt64BigEndian(gRoot.Data.AsSpan(8)) != all
            || BinaryPrimitives.ReadUInt64BigEndian(gRegions.Target.Data.AsSpan(gRegions.TargetAt)) != all)
        { Refuse("gameplay.dat's layer masks do not enable every layer"); return result; }
        if (gNames.TargetAt + 4 * n != gNames.Target.Data.Length) { Refuse("gameplay.dat's layer name pointers do not end their chunk"); return result; }

        // gp_prius (in place; nothing above can fail past this point).
        var removed = doc.RemoveLayer(layer);
        if (!removed.Applied) { result.Problems.AddRange(removed.Problems); return result; }
        result.Changes.AddRange(removed.Changes);

        // metadata.dat: the pointer first (a target inside a cut is refused), then the string, then the zero word.
        var namePointer = meta.PointerAt(pointers, 4 * layer)!;
        int start = namePointer.TargetAt;
        int end = Array.IndexOf(strings.Data, (byte)0, start) + 1;
        meta.Splice(pointers, 4 * layer, 4, []);
        meta.Splice(strings, start, end - start, []);
        meta.Splice(words, 4 * layer, 4, []);
        meta.PutWord(head, 4, (uint)(n - 1));
        result.Metadata = meta.Serialize();

        // gameplay.dat: the pointer, the name's chunk when nothing else points into it, the count and the masks.
        var nameChunk = gameplay.PointerAt(gNames.Target, gNames.TargetAt + 4 * layer)!.Target;
        gameplay.Splice(gNames.Target, gNames.TargetAt + 4 * layer, 4, []);
        if (!gameplay.Pointers.Any(p => p.Target == nameChunk)) gameplay.Chunks.Remove(nameChunk);
        BinaryPrimitives.WriteUInt32BigEndian(gRoot.Data.AsSpan(4), (uint)(n - 1));
        ulong remaining = n - 1 == 64 ? ulong.MaxValue : (1UL << (n - 1)) - 1;
        BinaryPrimitives.WriteUInt64BigEndian(gRoot.Data.AsSpan(8), remaining);
        BinaryPrimitives.WriteUInt64BigEndian(gRegions.Target.Data.AsSpan(gRegions.TargetAt), remaining);
        result.Gameplay = gameplay.Serialize();
        result.Changes.Add($"metadata.dat and gameplay.dat no longer list layer {name}");
        result.Applied = true;
        return result;
    }
}
