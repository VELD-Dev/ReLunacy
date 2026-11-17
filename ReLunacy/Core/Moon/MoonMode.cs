using System.Numerics;
using ReLunacy.Core.Selection;
using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Scene;

namespace ReLunacy.Core.Moon;

/// <summary>
/// Moon edit mode for A Crack in Time's space sectors: one moon at a time, everything else hidden, the camera
/// orbiting the moon (<see cref="MoonOrbit"/>), new objects standing on the surface and the gizmo's Moon space
/// (see GizmoController). A moon is its SpaceMoon Clue (class 0x4BC): +0xD0 names its area, a sphere centred on the
/// moon, and +0xE0 is the surface radius. On space_sector_1 the area's centre is the Clue's own position and the
/// moon's Tie, to within 0.01.
/// </summary>
public static class MoonMode
{
    private const uint SpaceMoonClass = 0x4BC;
    private const float DefaultRadius = 50f;

    public static bool Active { get; private set; }
    public static EntityVolume? Clue { get; private set; }
    public static EntityRegion? Region { get; private set; }
    public static Vector3 Centre { get; private set; }

    /// <summary>The moon's surface radius.</summary>
    public static float Radius { get; private set; } = DefaultRadius;

    /// <summary>How far the moon's area reaches from the centre.</summary>
    public static float AreaRadius { get; private set; }

    /// <summary>What <see cref="Enter"/> hid, for <see cref="Exit"/> to show again.</summary>
    private static readonly List<Entity> Hidden = [];

    /// <summary>The SpaceMoon Clue's volume, when <paramref name="entity"/> is one.</summary>
    public static EntityVolume? MoonClue(Entity? entity)
    {
        if (entity is not EntityVolume { BaseVolume.Clue.Class: SpaceMoonClass } volume) return null;
        return LunaWindow.Instance.Level?.GameDefinition?.Id == Engine.Games.GameId.ACrackInTime ? volume : null;
    }

    /// <summary>Starts moon mode on <paramref name="clue"/>: finds the centre and radius, hides what is not the moon's.</summary>
    public static bool Enter(EntityVolume clue)
    {
        if (Active || MoonClue(clue) == null) return false;
        var region = EntityManager.Singleton.Regions.FirstOrDefault(r => r.Contains(clue));
        if (region == null) return false;
        var frame = MoonCreator.Resolve(region, clue, EntityManager.Singleton.Regions);
        var area = frame.Area;
        Radius = frame.Radius;
        Centre = frame.Centre;
        AreaRadius = area != null ? MathF.Max(area.ShapeSize.X, MathF.Max(area.ShapeSize.Y, area.ShapeSize.Z)) * 0.5f : Radius * 4f;
        Clue = clue;
        Region = region;
        HideUnrelated(region, frame);
        Active = true;
        return true;
    }

    /// <summary>Leaves moon mode and shows what it hid.</summary>
    public static void Exit()
    {
        if (!Active) return;
        foreach (var entity in Hidden) entity.allowRender = true;
        Hidden.Clear();
        EntityManager.Singleton.VisibilityVersion++;
        Active = false;
        Clue = null;
        Region = null;
    }

    /// <summary>Related: in the moon's layers, named by the Clue (or in a Pod it names), or inside the moon's area.</summary>
    private static void HideUnrelated(EntityRegion region, MoonCreator.Frame frame)
    {
        var area = frame.Area;
        var clue = Clue!.BaseVolume.Clue!;
        // "moon1a_moonclue" -> "moon1a": the layers, zones and Pods of the moon carry it in their names.
        string key = MoonCreator.KeyOf(clue.Name).ToLowerInvariant();
        bool Named(string? name) => name != null && (key.Length > 0 && name.Contains(key, StringComparison.OrdinalIgnoreCase));

        var layers = new HashSet<string>();
        if (region.LayerOf(Clue) is { } clueLayer) layers.Add(clueLayer);
        if (area != null && region.LayerOf(area) is { } areaLayer) layers.Add(areaLayer);
        foreach (var layer in region.Layers) if (Named(layer)) layers.Add(layer);
        // A zone is named like its layer.
        foreach (var r in EntityManager.Singleton.Regions)
            foreach (var zone in r.Zones) if (Named(zone.ZoneName)) layers.Add(zone.ZoneName);

        var related = new HashSet<Entity>(ReferenceEqualityComparer.Instance) { Clue };
        if (frame.Tie != null) related.Add(frame.Tie);
        foreach (var reference in clue.References)
        {
            if (reference.Kind == "Pod")
            {
                if (reference.Index >= 0 && reference.Index < region.Pods.Count)
                    foreach (var member in region.Pods[reference.Index].Members) related.Add(member);
            }
            else if (region.Find(reference.Kind, reference.Index) is { } named) related.Add(named);
        }
        foreach (var pod in region.Pods)
            if (Named(pod.Name) || (frame.Beam != null && pod.Members.Contains(frame.Beam)) || (pod.Layer != null && layers.Contains(pod.Layer)))
                foreach (var member in pod.Members) related.Add(member);

        float reach = AreaRadius * AreaRadius;
        bool Keep(Entity e, string? layer = null)
        {
            if (related.Contains(e)) return true;
            if (layer != null && layers.Contains(layer)) return true;
            var sphere = e.WorldBoundingSphere;
            return Vector3.DistanceSquared(new Vector3(sphere.X, sphere.Y, sphere.Z), Centre) <= reach;
        }

        void Apply(IEnumerable<Entity> entities, Func<Entity, string?> layerOf)
        {
            foreach (var e in entities)
                if (e.allowRender && !Keep(e, layerOf(e))) { e.allowRender = false; Hidden.Add(e); }
        }

        foreach (var r in EntityManager.Singleton.Regions)
        {
            Apply(r.MobyInstances.Entities, r.LayerOf);
            Apply(r.Volumes.Entities, r.LayerOf);
            Apply(r.Cameras.Entities, r.LayerOf);
            Apply(r.ControllerInstances.Entities, r.LayerOf);
            Apply(r.Paths.Entities, r.LayerOf);
            foreach (var zone in r.Zones)
            {
                Apply(zone.TieInstances.Entities, _ => zone.ZoneName);
                Apply(zone.UFrags.Entities, _ => zone.ZoneName);
            }
        }
        Apply(EntityManager.Singleton.Foliage, _ => null);
        EntityManager.Singleton.VisibilityVersion++;
    }

    // --- Geometry ------------------------------------------------------------------------------------

    /// <summary>The direction from the centre to <paramref name="point"/>.</summary>
    public static Vector3 Radial(Vector3 point)
    {
        var d = point - Centre;
        return d.LengthSquared() > 1e-8f ? Vector3.Normalize(d) : Vector3.UnitY;
    }

    /// <summary>The rotation that turns an object's +Y onto the radial direction at <paramref name="point"/>, its +Z kept as
    /// near <paramref name="heading"/> as the surface allows.</summary>
    public static Quaternion UpRotation(Vector3 point, Vector3 heading)
    {
        var up = Radial(point);
        var forward = heading - up * Vector3.Dot(heading, up);
        if (forward.LengthSquared() < 1e-6f)
        {
            forward = Vector3.Cross(up, MathF.Abs(up.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ);
        }
        forward = Vector3.Normalize(forward);
        var right = Vector3.Cross(up, forward);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            right.X, right.Y, right.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            forward.X, forward.Y, forward.Z, 0f,
            0f, 0f, 0f, 1f)));
    }

    /// <summary>The tangent frame at <paramref name="point"/>: X east, Y radial, Z north, as a right-handed rotation.</summary>
    public static Quaternion TangentFrame(Vector3 point)
    {
        var up = Radial(point);
        var north = Vector3.UnitY - up * up.Y;
        if (north.LengthSquared() < 1e-6f) north = -Vector3.UnitZ;
        north = Vector3.Normalize(north);
        var east = Vector3.Cross(up, north);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            east.X, east.Y, east.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            north.X, north.Y, north.Z, 0f,
            0f, 0f, 0f, 1f)));
    }

    /// <summary>The nearer point where the ray meets the surface sphere, or null.</summary>
    public static Vector3? RaySurface(Vector3 origin, Vector3 direction)
    {
        var m = origin - Centre;
        float b = Vector3.Dot(m, direction);
        float c = m.LengthSquared() - Radius * Radius;
        float disc = b * b - c;
        if (disc < 0f) return null;
        float t = -b - MathF.Sqrt(disc);
        return t > 0f ? origin + direction * t : null;
    }
}
