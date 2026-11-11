using System.Numerics;
using ReLunacy.Engine.Rendering.Vulkan;
using ReLunacy.Engine.Scene;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// The 3D view's side of the paths: hands the renderer the vertices of every path's segments (and the
/// selected path's point markers) when they change, writes gizmo edits back to the paths, and picks paths
/// and points under the cursor. Picking is done here, on the CPU, in screen space - a few pixels around
/// each projected segment - rather than in the GPU pick target, which only knows meshes.
/// </summary>
internal sealed class PathOverlay
{
    private static readonly Vector4 PointColour = new(1f, 1f, 1f, 1f);
    private const float PickPixels = 6f;

    private float[] _vertices = [];
    private (VulkanRenderer? Renderer, bool Shown, long Versions, int Selected, int Point, Vector4 Highlight, int Selection, int Visibility) _built;

    private static IEnumerable<EntityPath> Paths(EntityManager em) =>
        em.Regions.Where(r => r.allowRender).SelectMany(r => r.Paths.Entities).OfType<EntityPath>().Where(p => p.allowRender && em.IsPathShown(p.Path));

    private static readonly Vector4 HandleColour = new(1f, 0.8f, 0.2f, 1f);

    /// <summary>The key a selected part belongs to (a handle's key), or -1.</summary>
    private static int SelectedKey(Entity? selected) =>
        selected is EntityPathPoint { Part: not PathPart.Point } part ? part.Index : -1;

    /// <summary>Writes this frame's gizmo edits back to the paths and, when anything changed, the lines to the renderer.</summary>
    public void Update(VulkanRenderer renderer, Entity? selected, bool hidden)
    {
        var em = EntityManager.Singleton;
        if (selected is EntityPathPoint point) point.Sync();
        long versions = 0;
        foreach (var path in Paths(em))
        {
            path.Sync();
            versions += path.Version;
        }

        bool shown = em.renderPaths && !hidden;
        var selectedPart = selected as EntityPathPoint;
        EntityPath? active = selected as EntityPath ?? selectedPart?.Owner;
        var key = (renderer, shown, versions, active?.ID ?? -1, selectedPart is { } sp ? sp.Index * 4 + (int)sp.Part : -1, em.VolumeSelectedColor,
            Core.Selection.SelectionManager.Singleton.Version, em.VisibilityVersion);
        if (key == _built) return;
        _built = key;

        if (!shown)
        {
            renderer.SetPathLines([], 0);
            return;
        }

        var vertices = new List<float>();
        Vector4 highlight = em.VolumeSelectedColor;
        foreach (var path in Paths(em))
        {
            var points = path.WorldPoints().ToArray();
            // Every selected path is highlighted; only the active one shows its keys.
            var colour = ReferenceEquals(path, active) || path.selected ? highlight : path.Colour;
            for (int i = 1; i < points.Length; i++) Segment(vertices, points[i - 1], points[i], colour);
            if (path.Closed && points.Length > 2) Segment(vertices, points[^1], points[0], colour);
        }
        if (active != null)
        {
            // The keys (every point, on a polyline), and the selected key's two handles on their arms.
            for (int i = 0; i < active.KeyCount; i++)
            {
                bool chosen = selectedPart is { Part: PathPart.Key or PathPart.Point } && selectedPart.Index == i;
                Marker(vertices, active.KeyAt(i), chosen ? highlight : PointColour, 1f);
            }
            int keyIndex = SelectedKey(selected);
            if (active.IsSpline && keyIndex >= 0)
            {
                Vector3 k = active.KeyAt(keyIndex);
                foreach (var (handle, part) in new[] { (active.InHandleAt(keyIndex), PathPart.InHandle), (active.OutHandleAt(keyIndex), PathPart.OutHandle) })
                {
                    if (handle is not { } h) continue;
                    Segment(vertices, k, h, HandleColour with { W = 0.7f });
                    Marker(vertices, h, selectedPart!.Part == part ? highlight : HandleColour, 2f);
                }
            }
        }
        _vertices = [.. vertices];
        renderer.SetPathLines(_vertices, _vertices.Length / VulkanRenderer.PathVertexFloats);
    }

    /// <summary>Forces the lines to be rebuilt (after an edit the versions do not show, such as a selection change).</summary>
    public void Invalidate() => _built = default;

    private static void Segment(List<float> v, Vector3 a, Vector3 b, Vector4 colour)
    {
        // Two triangles: (start,-1) (end,-1) (end,+1) / (start,-1) (end,+1) (start,+1).
        Vertex(v, a, b, 0f, -1f, 0f, colour); Vertex(v, a, b, 1f, -1f, 0f, colour); Vertex(v, a, b, 1f, 1f, 0f, colour);
        Vertex(v, a, b, 0f, -1f, 0f, colour); Vertex(v, a, b, 1f, 1f, 0f, colour); Vertex(v, a, b, 0f, 1f, 0f, colour);
    }

    private static void Marker(List<float> v, Vector3 p, Vector4 colour, float kind)
    {
        Vertex(v, p, p, 0f, -1f, kind, colour); Vertex(v, p, p, 1f, -1f, kind, colour); Vertex(v, p, p, 1f, 1f, kind, colour);
        Vertex(v, p, p, 0f, -1f, kind, colour); Vertex(v, p, p, 1f, 1f, kind, colour); Vertex(v, p, p, 0f, 1f, kind, colour);
    }

    private static void Vertex(List<float> v, Vector3 a, Vector3 b, float end, float side, float marker, Vector4 colour)
    {
        v.Add(a.X); v.Add(a.Y); v.Add(a.Z);
        v.Add(b.X); v.Add(b.Y); v.Add(b.Z);
        v.Add(end); v.Add(side); v.Add(marker);
        v.Add(colour.X); v.Add(colour.Y); v.Add(colour.Z); v.Add(colour.W);
    }

    /// <summary>
    /// What a click at <paramref name="mouse"/> (viewport pixels) lands on among the paths: a point of the
    /// selected path first, else the nearest path segment within a few pixels - returned with its distance
    /// from the camera, so the caller can keep whichever of it and the mesh under the cursor is nearer.
    /// </summary>
    public (Entity Entity, float Distance)? Pick(Vector2 mouse, Matrix4x4 viewProjection, Vector2 viewport, Vector3 camera, Entity? selected)
    {
        var em = EntityManager.Singleton;
        if (!em.renderPaths) return null;
        Vector2? Screen(Vector3 world)
        {
            var clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
            if (clip.W <= 1e-3f) return null;
            // As CollisionEditor.ToScreen: clip y up, viewport y down.
            return new Vector2((clip.X / clip.W + 1f) * 0.5f * viewport.X, (1f - clip.Y / clip.W) * 0.5f * viewport.Y);
        }

        if ((selected as EntityPath ?? (selected as EntityPathPoint)?.Owner) is { } active)
        {
            // The selected key's handles first (they sit off the line), then the keys (every point, on a polyline).
            var candidates = new List<(Vector3 Position, int Index, PathPart Part)>();
            int keyIndex = SelectedKey(selected);
            if (active.IsSpline && keyIndex >= 0)
            {
                if (active.InHandleAt(keyIndex) is { } inHandle) candidates.Add((inHandle, keyIndex, PathPart.InHandle));
                if (active.OutHandleAt(keyIndex) is { } outHandle) candidates.Add((outHandle, keyIndex, PathPart.OutHandle));
            }
            for (int i = 0; i < active.KeyCount; i++)
                candidates.Add((active.KeyAt(i), i, active.IsSpline ? PathPart.Key : PathPart.Point));
            int best = -1; float bestDistance = PickPixels + 2f;
            for (int c = 0; c < candidates.Count; c++)
                if (Screen(candidates[c].Position) is { } s && Vector2.Distance(s, mouse) is var d && d < bestDistance) { best = c; bestDistance = d; }
            if (best >= 0)
            {
                var (position, index, part) = candidates[best];
                return (new EntityPathPoint(active, index, part), Vector3.Distance(camera, position));
            }
        }

        (EntityPath Path, float Distance)? hit = null;
        float bestPixels = PickPixels;
        foreach (var path in Paths(em))
        {
            var points = path.WorldPoints().ToArray();
            int segments = points.Length - 1 + (path.Closed && points.Length > 2 ? 1 : 0);
            for (int i = 0; i < segments; i++)
            {
                Vector3 a = points[i], b = points[(i + 1) % points.Length];
                if (Screen(a) is not { } sa || Screen(b) is not { } sb) continue;
                Vector2 ab = sb - sa;
                float t = ab.LengthSquared() > 1e-6f ? Math.Clamp(Vector2.Dot(mouse - sa, ab) / ab.LengthSquared(), 0f, 1f) : 0f;
                float pixels = Vector2.Distance(mouse, sa + ab * t);
                if (pixels >= bestPixels) continue;
                bestPixels = pixels;
                hit = (path, Vector3.Distance(camera, Vector3.Lerp(a, b, t)));
            }
        }
        return hit is { } h ? (h.Path, h.Distance) : null;
    }
}
