namespace ReLunacy.Engine.Packing;

public sealed partial class GpPriusDocument
{
    /// <summary>The layer a new Pod or Area may go in: any from the last layer that already holds one of the kind. A group in
    /// an earlier layer would sit before later groups in the table and renumber them, which Clue references to Areas and Pods
    /// and the Area indexes of volumes and paths do not follow. Null when the layer is fine.</summary>
    public string? GroupLayerProblem(GpKind kind, int layer)
    {
        if (kind is not (GpKind.Pod or GpKind.Area or GpKind.Scent)) return $"a {kind} is not a group";
        if (layer < 0 || layer >= LayerCount) return $"layer {layer} does not exist (the region has {LayerCount})";
        var meta = MetadataOf(kind);
        if (meta == null || RecordsOf(kind) == null) return $"this region has no {kind} tables";
        int last = meta.Count == 0 ? 0 : Layer(new GpInstance(kind, meta.Count - 1));
        return layer >= last ? null : $"{kind}s of {LayerName(last)} come after layer {LayerName(layer)}, so a new one there would renumber them";
    }

    /// <summary>
    /// Appends an empty Pod, Area or Scent (no members; add them with <see cref="AddMember"/>). It goes at the end of its layer's
    /// run, which is the end of the table (<see cref="GroupLayerProblem"/>), so no existing Pod or Area is renumbered. An
    /// empty list is a null pointer and a zero count, as every shipped file has it. The counts in gameplay.dat are budgets
    /// the caller raises (<see cref="GameplayBudgets"/>).
    /// </summary>
    public GpEditResult AppendGroup(GpKind kind, ushort layer, string name, ulong? tuid = null, Random? random = null)
    {
        if (GroupLayerProblem(kind, layer) is { } layerProblem) return GpEditResult.Refuse(layerProblem);
        if (CheckCommon(layer, name, tuid) is { } common) return GpEditResult.Refuse(common);
        if (LayerRun(kind, layer).Count >= 0x1000) return GpEditResult.Refuse($"the layer already holds 4096 {kind}s (a handle's index is 12 bits)");
        var created = InsertInstance(kind, layer, new byte[Info(kind).RecordSize], [], tuid ?? NewTuid(random), name);
        var result = new GpEditResult();
        return Done(result, created, $"{kind} {name} appended to layer {LayerName(layer)}");
    }
}
