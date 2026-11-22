using ReLunacy.Engine.Packing;

namespace ReLunacy.Engine.Scene;

/// <summary>
/// Tie classes imported into the region that the level's files do not hold yet (<see cref="PendingTieClass"/>), and the new shaders
/// and textures they use (<see cref="PendingShader"/>). The save writes the shaders first (<see cref="ShaderRegistrar"/>), then the
/// classes (<see cref="TieClassRegistrar"/>), then the new Ties that use them, and the level is read again afterwards.
/// </summary>
public partial class EntityRegion
{
    public List<PendingTieClass> PendingTieClasses { get; } = [];

    public PendingTieClass? FindPendingTieClass(ulong tuid) => PendingTieClasses.Find(c => c.Tuid == tuid);

    /// <summary>A class TUID for a model about to be built: above every class of the level and every pending one of this region.
    /// <paramref name="read"/> is <c>FileManager.ReadOriginal</c>.</summary>
    public ulong NewClassTuid(Func<string, byte[]?> read) =>
        TieClassRegistrar.FreshTuid(read, PendingTieClasses.Select(c => c.Tuid), PendingShaders.Select(s => s.Tuid));

    public List<PendingShader> PendingShaders { get; } = [];

    public PendingShader? FindPendingShader(ulong tuid) => PendingShaders.Find(s => s.Tuid == tuid);

    /// <summary>Queues a new shader for the next save; it loads in this region. False when its TUID is already queued.</summary>
    public bool AddPendingShader(PendingShader pending)
    {
        if (PendingShaders.Any(s => s.Tuid == pending.Tuid)) return false;
        if (!pending.Regions.Contains(RegionName)) pending.Regions.Add(RegionName);
        PendingShaders.Add(pending);
        return true;
    }

    /// <summary>Takes a queued shader back.</summary>
    public bool RemovePendingShader(ulong tuid) => PendingShaders.RemoveAll(s => s.Tuid == tuid) > 0;

    /// <summary>Queues a built class for the next save; it loads in this region. False when the class did not build or its TUID is
    /// already queued.</summary>
    public bool AddPendingTieClass(PendingTieClass pending)
    {
        if (pending.Render.Problem != null || PendingTieClasses.Any(c => c.Tuid == pending.Tuid)) return false;
        if (!pending.Regions.Contains(RegionName)) pending.Regions.Add(RegionName);
        PendingTieClasses.Add(pending);
        return true;
    }

    /// <summary>Takes a queued class back (nothing placed it any more).</summary>
    public bool RemovePendingTieClass(ulong tuid) => PendingTieClasses.RemoveAll(c => c.Tuid == tuid) > 0;
}
