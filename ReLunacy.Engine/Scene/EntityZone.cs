using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering;
using NeoVeldrid;

namespace ReLunacy.Engine.Scene;

public class EntityZone : IDisposable
{
    public readonly EntityCluster TieInstances;
    public readonly EntityCluster UFrags;

    /// <summary>One per chunk of this zone's navmesh; empty for the zones that have none. Not an
    /// EntityCluster: a chunk has no mesh or material of its own (see EntityNavmesh).</summary>
    public readonly List<EntityNavmesh> Navmeshes = [];

    public int TieCount => TieInstances.Size;
    public int UFragsCount => UFrags.Size;
    public int NavmeshChunkCount => Navmeshes.Count;

    public bool allowRender = true;

    public string ZoneName;

    /// <summary>The name the file holds, set by the first <see cref="Rename"/>; null while as loaded.</summary>
    public string? LoadedName { get; private set; }

    /// <summary>The name of a zone the files hold was changed and no save has written it.</summary>
    public bool IsRenamed => !IsNew && LoadedName != null && LoadedName != ZoneName;

    public void Rename(string name)
    {
        LoadedName ??= ZoneName;
        ZoneName = name;
        foreach (var chunk in Navmeshes) chunk.ZoneName = name;
    }

    public void MarkNameSaved() { if (LoadedName != null) LoadedName = ZoneName; }
    public ulong ZoneTUID;

    /// <summary>Made in the editor and not saved yet: the file has no such zone, and a save makes it with its first new
    /// Tie. <see cref="ZoneTUID"/> is a placeholder until then.</summary>
    public bool IsNew { get; internal set; }

    /// <summary>An empty zone for the editor to put new Ties in (see <see cref="EntityRegion.CreateNewZone"/>).</summary>
    internal EntityZone(string name, ulong tuid, AssetManager assetManager)
    {
        ZoneName = name;
        ZoneTUID = tuid;
        IsNew = true;
        TieInstances = new EntityCluster([], assetManager);
        UFrags = new EntityCluster([], assetManager);
    }

    public EntityZone(IZone zone, GraphicsDevice gd, AssetManager assetManager)
    {
        ZoneName = zone.Name ?? "UnnamedZone";
        ZoneTUID = zone.Id;

        TieInstances = new EntityCluster([], assetManager);
        foreach (var tieInstance in zone.TieInstances)
            TieInstances.Add(tieInstance);

        UFrags = new EntityCluster([], assetManager);
        foreach (var ufrag in zone.UFrags)
            UFrags.Add(ufrag, gd);

        if (zone.Navmesh is { } navmesh)
            foreach (var chunk in navmesh.Chunks)
                Navmeshes.Add(new EntityNavmesh(chunk, ZoneName));
    }

    private int _boundsCount = -1;
    private (Vector3 Min, Vector3 Max)? _bounds;

    /// <summary>The box round the zone's Ties and UFrags (their bounding spheres); null for a zone with none.
    /// Kept until the number of them changes.</summary>
    public (Vector3 Min, Vector3 Max)? Bounds
    {
        get
        {
            int count = TieInstances.Size + UFrags.Size;
            if (count == _boundsCount) return _bounds;
            _boundsCount = count;
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            bool any = false;
            foreach (var e in TieInstances.Entities.Concat(UFrags.Entities))
            {
                var s = e.WorldBoundingSphere;
                var c = new Vector3(s.X, s.Y, s.Z);
                min = Vector3.Min(min, c - new Vector3(s.W));
                max = Vector3.Max(max, c + new Vector3(s.W));
                any = true;
            }
            _bounds = any ? (min, max) : null;
            return _bounds;
        }
    }

    /// <summary>How far <paramref name="point"/> is from the zone's box: 0 inside it, infinite for a zone without one.</summary>
    public float DistanceTo(Vector3 point)
    {
        if (Bounds is not { } box) return float.MaxValue;
        var outside = Vector3.Max(Vector3.Max(box.Min - point, point - box.Max), Vector3.Zero);
        return outside.Length();
    }

    public void Dispose()
    {
        TieInstances.Dispose();
        UFrags.Dispose();
        GC.SuppressFinalize(this);
    }
}
