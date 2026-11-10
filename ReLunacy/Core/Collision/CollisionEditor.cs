using System.Numerics;
using ReLunacy.Engine.Assets.Collision;
using ReLunacy.Engine.Rendering;
using ReLunacy.Engine.Rendering.Vulkan;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Collision;

public enum CollisionSelectMode
{
    /// <summary>Triangles of the collision meshes (primitive sets have no per-face data).</summary>
    Faces,
    /// <summary>Whole placed instances, meshes and primitive sets alike.</summary>
    Instances,
}

/// <summary>
/// The 3D viewport's collision panel and edit mode: how the overlay looks and what it filters, and in
/// edit mode, selecting faces or instances and changing their triangle flags, materials and instance
/// flags.
///
/// Picking runs on the CPU against the shapes already in memory (see <see cref="CollisionScene"/>):
/// the candidate triangles are rasterised into a small face-id buffer covering the click or the
/// marquee, nearest first, so what gets selected is what the overlay shows - after its filters - and
/// a face hidden behind other collision is not. "Through walls" drops the depth test for the marquee.
///
/// Edits change the shapes in memory and the uploaded attributes in place. A shape is stored once and
/// placed by every instance that uses it (and once per region file, see CollisionShapeEntry), so
/// editing a face edits it on all of those; the panel says how many. Nothing is written back to the
/// level files.
/// </summary>
public sealed class CollisionEditor
{
    public bool PanelOpen;
    public bool EditMode;
    public CollisionSelectMode Mode = CollisionSelectMode.Faces;
    public bool SelectThroughWalls;

    /// <summary>Regions shown in the Zones menu, as View3D works it out (bit N = Nth uploaded region).</summary>
    public uint RegionMask = uint.MaxValue;

    public CollisionScene? Scene { get; private set; }
    private VulkanRenderer? _renderer;

    // Filters. Per level, since which values exist depends on the level; reset on upload.
    private readonly uint[] _materialMask = new uint[8];
    private uint _triangleFlagMask;
    private readonly HashSet<ushort> _hiddenInstanceFlags = [];

    private readonly HashSet<(int Shape, int Triangle)> _faces = [];
    private readonly HashSet<int> _instances = [];

    // Originals of everything edited, for Revert and the "modified" count.
    private readonly Dictionary<CollisionMesh, (byte[] Materials, byte[] Flags)> _originalMeshes = [];
    private readonly Dictionary<CollisionInstance, ushort> _originalInstanceFlags = [];
    private int _modifiedFaces, _modifiedInstances;

    private byte _materialInput;

    // Tie -> the file collision instances placed with it, each with its offset from the Tie's editor
    // matrix, and the editor matrix last applied. Made the first time a Tie is followed, and kept
    // across rebuilds of the overlay for the same level: they are keyed by the file's instances, which
    // outlive the scene, so a Tie that already moved stays linked.
    private readonly Dictionary<EntityTie, (List<(CollisionInstance Source, Matrix4x4 Offset)> Links, Matrix4x4 Last)> _tieLinks = [];
    private object? _level;

    /// <summary>Frame tint of a flag checkbox some of the selection has and some does not.</summary>
    private static readonly Vector4 MixedColour = new(0.55f, 0.40f, 0.10f, 0.9f);

    public int SelectedFaceCount => _faces.Count;
    public int SelectedInstanceCount => _instances.Count;
    public bool HasSelection => _faces.Count > 0 || _instances.Count > 0;

    /// <summary>Takes a freshly uploaded scene (or none): selection, filters and edit history start
    /// over. Tie links survive when <paramref name="level"/> is the one already attached.</summary>
    public void Attach(CollisionScene? scene, VulkanRenderer renderer, object? level)
    {
        if (!ReferenceEquals(level, _level)) _tieLinks.Clear();
        _level = level;
        Scene = scene is { IsEmpty: false } ? scene : null;
        _renderer = renderer;
        _faces.Clear();
        _instances.Clear();
        _hiddenInstanceFlags.Clear();
        Array.Fill(_materialMask, uint.MaxValue);
        _triangleFlagMask = uint.MaxValue;
        _originalMeshes.Clear();
        _originalInstanceFlags.Clear();
        _modifiedFaces = _modifiedInstances = 0;
        if (Scene == null) EditMode = false;
    }

    /// <summary>The filters and edit state the renderer draws with, pushed every frame.</summary>
    public void Apply(VulkanRenderer renderer, EditorSettings settings)
    {
        _materialMask.CopyTo(renderer.CollisionMaterialMask, 0);
        renderer.CollisionTriangleFlagMask = _triangleFlagMask;
        renderer.CollisionPrimitiveKinds = settings.CollisionPrimitiveKinds;
        renderer.CollisionEditMode = EditMode;
        renderer.CollisionSelectionColor = settings.SelectionOutlineColor with { W = 1f };
    }

    // ---------------------------------------------------------------- Tie links

    /// <summary>Moves a Tie's collision with it. Called every frame for the selected entity (whatever
    /// moved it: gizmo or inspector); does nothing unless it is a Tie whose matrix changed.</summary>
    public void FollowEntity(Entity entity)
    {
        if (Scene == null || entity is not EntityTie tie) return;
        var matrix = tie.Transform.GetMatrix();
        if (!_tieLinks.TryGetValue(tie, out var entry))
        {
            // Matched on the file's matrix, which the collision shares exactly; the editor's matrix is
            // rebuilt from a decompose and misses mirrored placements (220 of 3858 on cobalia). The
            // offset is taken from the editor's matrix AS LOADED, so the collision follows it exactly,
            // and a Tie moved before it was first linked is caught up on the spot.
            var loaded = Recompose(tie.PlacementMatrix);
            if (!Matrix4x4.Invert(loaded, out var inverse)) return;
            var links = Scene.FindTiePlacements(tie.PlacementMatrix, WorldSphere(tie, loaded))
                .SelectMany(p => p.Sources.Select(source => (source, p.Transform * inverse))).ToList();
            _tieLinks[tie] = entry = (links, loaded);
        }
        if (entry.Links.Count == 0 || matrix == entry.Last) return;

        var moved = new HashSet<CollisionPlacement>();
        foreach (var (source, offset) in entry.Links)
        {
            if (Scene.PlacementOf(source) is not { } placement || !moved.Add(placement)) continue;
            var transform = offset * matrix;
            Scene.MovePlacement(placement, transform);
            _renderer?.UpdateCollisionInstanceTransform(placement.Index, transform);
        }
        _tieLinks[tie] = (entry.Links, matrix);
    }

    /// <summary>How many collision instances move with this Tie (0 when none, or not linked yet).</summary>
    public int LinkedCollisionCount(Entity entity) =>
        entity is EntityTie tie && _tieLinks.TryGetValue(tie, out var entry) ? entry.Links.Count : 0;

    /// <summary>The editor's matrix for a placement matrix, built the way EntityTie builds its Transform.</summary>
    private static Matrix4x4 Recompose(Matrix4x4 m)
    {
        Matrix4x4.Decompose(m, out var scale, out var rotation, out var translation);
        return new Engine.Rendering.Resources.Transform { Translation = translation, Rotation = rotation, Scale = scale }.GetMatrix();
    }

    /// <summary>The Tie's world bounding sphere at <paramref name="m"/>, as Entity.WorldBoundingSphere
    /// computes it for the current transform.</summary>
    private static Vector4 WorldSphere(EntityTie tie, Matrix4x4 m)
    {
        Matrix4x4.Decompose(m, out var scale, out _, out _);
        var local = tie.BoundingSphere;
        float maxScale = MathF.Max(MathF.Abs(scale.X), MathF.Max(MathF.Abs(scale.Y), MathF.Abs(scale.Z)));
        return new Vector4(Vector3.Transform(new Vector3(local.X, local.Y, local.Z), m), local.W * maxScale);
    }

    // ---------------------------------------------------------------- visibility (mirrors the shader)

    private bool PlacementVisible(CollisionPlacement p, EditorSettings settings)
    {
        var category = p.Shape.IsMesh ? (p.IsTie ? CollisionCategories.TieMesh : CollisionCategories.WorldMesh) : CollisionCategories.Primitives;
        return (settings.CollisionCategories & category) != 0
            && (Scene!.Instances[p.Index].RegionMask & RegionMask) != 0
            && !_hiddenInstanceFlags.Contains(p.ShapeFlags);
    }

    private bool FaceVisible(uint attributes, EditorSettings settings)
    {
        uint kind = (attributes >> CollisionGeometry.KindShift) & 7u;
        if (kind != 0)
            return ((uint)settings.CollisionPrimitiveKinds & (1u << (int)(kind - 1))) != 0;
        uint material = attributes & 0xFF;
        return (_materialMask[material >> 5] & (1u << (int)(material & 31))) != 0
            && (_triangleFlagMask & (1u << (int)((attributes >> 8) & 0x1F))) != 0;
    }

    // ---------------------------------------------------------------- picking

    /// <summary>A click at <paramref name="mouse"/> (viewport pixels). With neither modifier the
    /// selection is replaced; Shift adds, Ctrl removes.</summary>
    public void Click(Vector2 mouse, Matrix4x4 viewProjection, Vector2 viewport, bool add, bool remove, EditorSettings settings)
    {
        if (Scene == null) return;
        const int tolerance = 2;
        var rect = (X0: (int)mouse.X - tolerance, Y0: (int)mouse.Y - tolerance, X1: (int)mouse.X + tolerance + 1, Y1: (int)mouse.Y + tolerance + 1);
        var hits = Rasterize(rect, viewProjection, viewport, settings, depthTest: true, out var buffer, out var candidates);
        (int Placement, int Triangle)? hit = null;
        if (hits)
        {
            // The pixel under the cursor, else the nearest covered one in the window.
            int width = rect.X1 - rect.X0, best = int.MaxValue;
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].Id < 0) continue;
                int dx = i % width - tolerance, dy = i / width - tolerance, d = dx * dx + dy * dy;
                if (d < best) { best = d; hit = candidates[buffer[i].Id]; }
            }
        }

        if (!add && !remove) ClearSelection();
        if (hit is not { } h) return;
        var placement = Scene.Placements[h.Placement];
        if (Mode == CollisionSelectMode.Instances)
            SetInstanceSelected(placement.Index, !remove);
        else if (placement.Shape.IsMesh)
            SetFaceSelected(placement.Shape, h.Triangle, !remove);
    }

    /// <summary>A marquee from <paramref name="a"/> to <paramref name="b"/> (viewport pixels): every face
    /// (or instance with a face) showing inside it, or with Through walls every one whose face centre
    /// is inside.</summary>
    public void Marquee(Vector2 a, Vector2 b, Matrix4x4 viewProjection, Vector2 viewport, bool add, bool remove, EditorSettings settings)
    {
        if (Scene == null) return;
        var min = Vector2.Max(Vector2.Min(a, b), Vector2.Zero);
        var max = Vector2.Min(Vector2.Max(a, b), viewport);
        var rect = ((int)min.X, (int)min.Y, (int)MathF.Ceiling(max.X), (int)MathF.Ceiling(max.Y));
        if (rect.Item3 <= rect.Item1 || rect.Item4 <= rect.Item2) return;

        var found = new HashSet<(int Placement, int Triangle)>();
        if (SelectThroughWalls)
            CentresInside(rect, viewProjection, viewport, settings, found);
        else if (Rasterize(rect, viewProjection, viewport, settings, depthTest: true, out var buffer, out var candidates))
            foreach (var pixel in buffer)
                if (pixel.Id >= 0) found.Add(candidates[pixel.Id]);

        if (!add && !remove) ClearSelection();
        foreach (var (p, t) in found)
        {
            var placement = Scene.Placements[p];
            if (Mode == CollisionSelectMode.Instances)
                SetInstanceSelected(placement.Index, !remove);
            else if (placement.Shape.IsMesh)
                SetFaceSelected(placement.Shape, t, !remove);
        }
    }

    private struct Pixel { public float Depth; public int Id; }

    /// <summary>Placements whose world box can reach the rectangle on screen. Boxes reaching behind the
    /// camera are kept, since their projection is meaningless.</summary>
    private IEnumerable<CollisionPlacement> Candidates((int X0, int Y0, int X1, int Y1) rect, Matrix4x4 viewProjection, Vector2 viewport, EditorSettings settings)
    {
        foreach (var p in Scene!.Placements)
        {
            if (!PlacementVisible(p, settings)) continue;
            float sx0 = float.MaxValue, sy0 = float.MaxValue, sx1 = float.MinValue, sy1 = float.MinValue;
            bool behind = false, allBehind = true;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) != 0 ? p.WorldMax.X : p.WorldMin.X, (i & 2) != 0 ? p.WorldMax.Y : p.WorldMin.Y, (i & 4) != 0 ? p.WorldMax.Z : p.WorldMin.Z);
                var clip = Vector4.Transform(new Vector4(corner, 1f), viewProjection);
                if (clip.Z < 0f || clip.W <= 1e-5f) { behind = true; continue; }
                allBehind = false;
                var s = ToScreen(clip, viewport);
                sx0 = MathF.Min(sx0, s.X); sy0 = MathF.Min(sy0, s.Y);
                sx1 = MathF.Max(sx1, s.X); sy1 = MathF.Max(sy1, s.Y);
            }
            if (allBehind) continue;
            if (behind || (sx1 >= rect.X0 && sx0 <= rect.X1 && sy1 >= rect.Y0 && sy0 <= rect.Y1))
                yield return p;
        }
    }

    private static Vector2 ToScreen(Vector4 clip, Vector2 viewport) =>
        new((clip.X / clip.W + 1f) * 0.5f * viewport.X, (1f - clip.Y / clip.W) * 0.5f * viewport.Y);

    /// <summary>Rasterises every visible candidate triangle into a face-id buffer the size of the
    /// rectangle, nearest first: the faces left in it are the ones showing. Two-sided, like the overlay.</summary>
    private bool Rasterize((int X0, int Y0, int X1, int Y1) rect, Matrix4x4 viewProjection, Vector2 viewport, EditorSettings settings,
        bool depthTest, out Pixel[] buffer, out List<(int Placement, int Triangle)> candidates)
    {
        int width = rect.X1 - rect.X0, height = rect.Y1 - rect.Y0;
        buffer = new Pixel[width * height];
        for (int i = 0; i < buffer.Length; i++) buffer[i] = new Pixel { Depth = float.MaxValue, Id = -1 };
        candidates = [];
        var vertices = Scene!.Vertices;
        Span<Vector4> polygon = stackalloc Vector4[4];
        Span<Vector4> clipped = stackalloc Vector4[4];
        bool any = false;

        foreach (var p in Candidates(rect, viewProjection, viewport, settings))
        {
            var toClip = p.Transform * viewProjection;
            var shape = p.Shape;
            for (int t = 0; t < shape.TriangleCount; t++)
            {
                int v = (int)shape.FirstVertex + t * 3;
                if (!FaceVisible(vertices[v].Attributes, settings)) continue;
                polygon[0] = Vector4.Transform(new Vector4(vertices[v].Position, 1f), toClip);
                polygon[1] = Vector4.Transform(new Vector4(vertices[v + 1].Position, 1f), toClip);
                polygon[2] = Vector4.Transform(new Vector4(vertices[v + 2].Position, 1f), toClip);
                int count = ClipNear(polygon[..3], clipped);
                if (count < 3) continue;

                // The near clip leaves a triangle or a quad: a fan, all under one id.
                int id = -1;
                for (int k = 1; k + 1 < count; k++)
                    RasterTriangle(clipped[0], clipped[k], clipped[k + 1], rect, viewport, buffer, ref id, candidates.Count, depthTest);
                if (id >= 0)
                {
                    candidates.Add((p.Index, t));
                    any = true;
                }
            }
        }
        return any;
    }

    /// <summary>Clips a triangle against the near plane (clip z &gt;= 0), giving up to four vertices.</summary>
    private static int ClipNear(ReadOnlySpan<Vector4> input, Span<Vector4> output)
    {
        int n = 0;
        for (int i = 0; i < 3; i++)
        {
            Vector4 a = input[i], b = input[(i + 1) % 3];
            bool aIn = a.Z >= 0f, bIn = b.Z >= 0f;
            if (aIn) output[n++] = a;
            if (aIn != bIn && n < 4)
            {
                float f = a.Z / (a.Z - b.Z);
                output[n++] = Vector4.Lerp(a, b, f);
            }
        }
        return n;
    }

    /// <summary>One screen triangle into the buffer. Writes <paramref name="id"/> (allocated as
    /// <paramref name="nextId"/> on the first pixel won) on every pixel where it is nearest.</summary>
    private static void RasterTriangle(Vector4 c0, Vector4 c1, Vector4 c2, (int X0, int Y0, int X1, int Y1) rect, Vector2 viewport,
        Pixel[] buffer, ref int id, int nextId, bool depthTest)
    {
        if (c0.W <= 1e-6f || c1.W <= 1e-6f || c2.W <= 1e-6f) return;
        Vector2 s0 = ToScreen(c0, viewport), s1 = ToScreen(c1, viewport), s2 = ToScreen(c2, viewport);
        float z0 = c0.Z / c0.W, z1 = c1.Z / c1.W, z2 = c2.Z / c2.W;
        float area = (s1.X - s0.X) * (s2.Y - s0.Y) - (s1.Y - s0.Y) * (s2.X - s0.X);
        if (MathF.Abs(area) < 1e-8f) return;

        int x0 = Math.Max(rect.X0, (int)MathF.Floor(MathF.Min(s0.X, MathF.Min(s1.X, s2.X))));
        int x1 = Math.Min(rect.X1 - 1, (int)MathF.Ceiling(MathF.Max(s0.X, MathF.Max(s1.X, s2.X))));
        int y0 = Math.Max(rect.Y0, (int)MathF.Floor(MathF.Min(s0.Y, MathF.Min(s1.Y, s2.Y))));
        int y1 = Math.Min(rect.Y1 - 1, (int)MathF.Ceiling(MathF.Max(s0.Y, MathF.Max(s1.Y, s2.Y))));
        if (x0 > x1 || y0 > y1) return;

        int width = rect.X1 - rect.X0;
        float inv = 1f / area;
        for (int y = y0; y <= y1; y++)
        {
            float py = y + 0.5f;
            for (int x = x0; x <= x1; x++)
            {
                float px = x + 0.5f;
                float b0 = ((s1.X - px) * (s2.Y - py) - (s1.Y - py) * (s2.X - px)) * inv;
                float b1 = ((s2.X - px) * (s0.Y - py) - (s2.Y - py) * (s0.X - px)) * inv;
                float b2 = 1f - b0 - b1;
                if (b0 < 0f || b1 < 0f || b2 < 0f) continue;
                float depth = b0 * z0 + b1 * z1 + b2 * z2;
                ref Pixel pixel = ref buffer[(y - rect.Y0) * width + (x - rect.X0)];
                if (depthTest && depth >= pixel.Depth) continue;
                if (id < 0) id = nextId;
                pixel.Depth = depth;
                pixel.Id = id;
            }
        }
    }

    /// <summary>Through walls: every visible face whose centre projects inside the rectangle, in front
    /// of the camera.</summary>
    private void CentresInside((int X0, int Y0, int X1, int Y1) rect, Matrix4x4 viewProjection, Vector2 viewport, EditorSettings settings,
        HashSet<(int, int)> found)
    {
        var vertices = Scene!.Vertices;
        foreach (var p in Candidates(rect, viewProjection, viewport, settings))
        {
            var toClip = p.Transform * viewProjection;
            for (int t = 0; t < p.Shape.TriangleCount; t++)
            {
                int v = (int)p.Shape.FirstVertex + t * 3;
                if (!FaceVisible(vertices[v].Attributes, settings)) continue;
                var centre = (vertices[v].Position + vertices[v + 1].Position + vertices[v + 2].Position) / 3f;
                var clip = Vector4.Transform(new Vector4(centre, 1f), toClip);
                if (clip.Z < 0f || clip.W <= 1e-5f) continue;
                var s = ToScreen(clip, viewport);
                if (s.X >= rect.X0 && s.X < rect.X1 && s.Y >= rect.Y0 && s.Y < rect.Y1)
                {
                    found.Add((p.Index, t));
                    // One face is enough to take a whole instance.
                    if (Mode == CollisionSelectMode.Instances) break;
                }
            }
        }
    }

    // ---------------------------------------------------------------- selection

    public void ClearSelection()
    {
        if (Scene == null) { _faces.Clear(); _instances.Clear(); return; }
        foreach (var (shape, t) in _faces.ToList())
            SetFaceSelected(Scene.Shapes[shape], t, false);
        foreach (int i in _instances.ToList())
            SetInstanceSelected(i, false);
    }

    private void SetFaceSelected(CollisionShapeEntry shape, int triangle, bool selected)
    {
        bool changed = selected ? _faces.Add((shape.Index, triangle)) : _faces.Remove((shape.Index, triangle));
        if (changed) WriteFace(shape, triangle);
    }

    private void SetInstanceSelected(int index, bool selected)
    {
        bool changed = selected ? _instances.Add(index) : _instances.Remove(index);
        if (changed) WriteInstance(Scene!.Placements[index]);
    }

    /// <summary>Rewrites a face's three attribute words, CPU copy and GPU, from the mesh and the selection.</summary>
    private void WriteFace(CollisionShapeEntry shape, int triangle)
    {
        if (shape.Copies[0] is not CollisionMesh mesh) return;
        uint attributes = CollisionGeometry.MeshFaceAttributes(mesh.Materials[triangle], mesh.TriangleFlags[triangle]);
        if (_faces.Contains((shape.Index, triangle))) attributes |= CollisionGeometry.SelectedFaceBit;
        int first = (int)shape.FirstVertex + triangle * 3;
        var vertices = Scene!.Vertices;
        for (int k = 0; k < 3; k++)
            vertices[first + k] = vertices[first + k] with { Attributes = attributes };
        _renderer?.UpdateCollisionVertexAttributes((uint)first, [attributes, attributes, attributes]);
    }

    private void WriteInstance(CollisionPlacement placement)
    {
        uint attributes = CollisionGeometry.InstanceAttributes(placement, _instances.Contains(placement.Index), _hiddenInstanceFlags.Contains(placement.ShapeFlags));
        Scene!.Instances[placement.Index].Attributes = attributes;
        _renderer?.UpdateCollisionInstanceAttributes(placement.Index, attributes);
    }

    /// <summary>Everything visible: every mesh face, or every instance.</summary>
    private void SelectAllVisible(EditorSettings settings)
    {
        foreach (var p in Scene!.Placements)
        {
            if (!PlacementVisible(p, settings)) continue;
            if (Mode == CollisionSelectMode.Instances) { SetInstanceSelected(p.Index, true); continue; }
            if (!p.Shape.IsMesh) continue;
            for (int t = 0; t < p.Shape.TriangleCount; t++)
                if (FaceVisible(Scene.Vertices[p.Shape.FirstVertex + t * 3].Attributes, settings))
                    SetFaceSelected(p.Shape, t, true);
        }
    }

    /// <summary>Every visible face with a material (or flags value) the selection already has.</summary>
    private void SelectSimilarFaces(EditorSettings settings, bool byMaterial)
    {
        var values = new HashSet<byte>();
        foreach (var (shape, t) in _faces)
            if (Scene!.Shapes[shape].Copies[0] is CollisionMesh mesh)
                values.Add(byMaterial ? mesh.Materials[t] : mesh.TriangleFlags[t]);
        foreach (var p in Scene!.Placements)
        {
            if (!p.Shape.IsMesh || !PlacementVisible(p, settings) || p.Shape.Copies[0] is not CollisionMesh mesh) continue;
            for (int t = 0; t < mesh.TriangleCount; t++)
                if (values.Contains(byMaterial ? mesh.Materials[t] : mesh.TriangleFlags[t]) &&
                    FaceVisible(Scene.Vertices[p.Shape.FirstVertex + t * 3].Attributes, settings))
                    SetFaceSelected(p.Shape, t, true);
        }
    }

    /// <summary>Instances: every visible placement of a selected one's shape, or with its flags.</summary>
    private void SelectSimilarInstances(EditorSettings settings, bool byShape)
    {
        var shapes = _instances.Select(i => Scene!.Placements[i].Shape).ToHashSet();
        var flags = _instances.Select(i => Scene!.Placements[i].ShapeFlags).ToHashSet();
        foreach (var p in Scene!.Placements)
            if (PlacementVisible(p, settings) && (byShape ? shapes.Contains(p.Shape) : flags.Contains(p.ShapeFlags)))
                SetInstanceSelected(p.Index, true);
    }

    /// <summary>Instances to Faces: every face of the selected instances' meshes.</summary>
    private void FacesOfSelectedInstances()
    {
        var shapes = _instances.Select(i => Scene!.Placements[i].Shape).Where(s => s.IsMesh).Distinct().ToList();
        ClearSelection();
        Mode = CollisionSelectMode.Faces;
        foreach (var shape in shapes)
            for (int t = 0; t < shape.TriangleCount; t++)
                SetFaceSelected(shape, t, true);
    }

    // ---------------------------------------------------------------- edits

    /// <summary>The faces an edit applies to: the selected faces, or every face of the selected
    /// instances' meshes.</summary>
    private IEnumerable<(CollisionShapeEntry Shape, int Triangle)> TargetFaces()
    {
        if (Mode == CollisionSelectMode.Faces)
            return _faces.Select(f => (Scene!.Shapes[f.Shape], f.Triangle));
        return _instances.Select(i => Scene!.Placements[i].Shape).Where(s => s.IsMesh).Distinct()
            .SelectMany(s => Enumerable.Range(0, s.TriangleCount).Select(t => (s, t)));
    }

    private void EditFaces(Func<byte, byte> material, Func<byte, byte> flags)
    {
        foreach (var (shape, t) in TargetFaces())
        {
            // Every copy of the shape (one per region file that holds it), so they stay alike.
            foreach (var copy in shape.Copies)
            {
                if (copy is not CollisionMesh mesh) continue;
                if (!_originalMeshes.ContainsKey(mesh))
                    _originalMeshes[mesh] = ((byte[])mesh.Materials.Clone(), (byte[])mesh.TriangleFlags.Clone());
                mesh.Materials[t] = material(mesh.Materials[t]);
                mesh.TriangleFlags[t] = (byte)(flags(mesh.TriangleFlags[t]) & 0x1F);
            }
            WriteFace(shape, t);
        }
        AfterEdit();
    }

    private void EditInstanceFlags(ushort bit, bool on)
    {
        // 0x400 says the shape is a primitive set: it describes the data, it is not a setting.
        if (bit == 0x400) return;
        foreach (int i in _instances)
        {
            var placement = Scene!.Placements[i];
            foreach (var source in placement.Sources)
            {
                _originalInstanceFlags.TryAdd(source, source.ShapeFlags);
                source.ShapeFlags = on ? (ushort)(source.ShapeFlags | bit) : (ushort)(source.ShapeFlags & ~bit);
            }
            WriteInstance(placement);
        }
        AfterEdit();
    }

    private void Revert()
    {
        foreach (var (mesh, (materials, flags)) in _originalMeshes)
        {
            materials.CopyTo(mesh.Materials, 0);
            flags.CopyTo(mesh.TriangleFlags, 0);
        }
        foreach (var (instance, flags) in _originalInstanceFlags)
            instance.ShapeFlags = flags;
        var meshes = _originalMeshes.Keys.ToHashSet();
        foreach (var shape in Scene!.Shapes)
            if (shape.Copies.Any(c => c is CollisionMesh m && meshes.Contains(m)))
                for (int t = 0; t < shape.TriangleCount; t++)
                    WriteFace(shape, t);
        foreach (var p in Scene.Placements)
            if (p.Sources.Any(_originalInstanceFlags.ContainsKey))
                WriteInstance(p);
        _originalMeshes.Clear();
        _originalInstanceFlags.Clear();
        AfterEdit();
    }

    private void AfterEdit()
    {
        _modifiedFaces = 0;
        foreach (var (mesh, (materials, flags)) in _originalMeshes)
            for (int t = 0; t < materials.Length; t++)
                if (materials[t] != mesh.Materials[t] || flags[t] != mesh.TriangleFlags[t])
                    _modifiedFaces++;
        _modifiedInstances = _originalInstanceFlags.Count(kv => kv.Key.ShapeFlags != kv.Value);
        Scene!.Recount(Scene.Summary.DuplicateInstances);
        EntityManager.Singleton.CollisionSummary = Scene.Summary;
    }

    // ---------------------------------------------------------------- panel

    /// <summary>The panel hanging from the toolbar's collision button.</summary>
    public void DrawPanel(ViewportOverlay overlay, float anchorLeft, EditorSettings settings, ref Entity? selectedEntity)
    {
        if (!PanelOpen || !overlay.BeginPanel("collision", new Vector2(360f, 0f), anchorLeft)) return;
        var em = EntityManager.Singleton;
        var summary = em.CollisionSummary;
        if (summary == null || Scene == null)
        {
            ImGui.TextDisabled(LM.Get("GUI_Viewport_NoCollision"));
            overlay.EndPanel();
            return;
        }

        bool persist = false;
        if (ImGui.Checkbox(LM.Get("GUI_Collision_Show"), ref em.renderCollision))
            MenuBar.RenderMenuDraw.PersistRenderSettings();
        ImGui.SameLine();
        ImGui.TextDisabled(LM.Get("GUI_Label_CollisionSummary", summary.Instances, summary.Shapes, summary.PlacedTriangles));

        // Edit mode.
        bool edit = EditMode;
        if (ImGui.Checkbox(LM.Get("GUI_Collision_EditMode"), ref edit))
        {
            EditMode = edit;
            if (edit)
            {
                em.renderCollision = true;
                selectedEntity = null;
            }
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Collision_EditModeHint"));
        if (EditMode)
            DrawEditSection(settings);

        // Display.
        if (ImGui.CollapsingHeader(LM.Get("GUI_Collision_Display")))
        {
            ImGui.SetNextItemWidth(160f);
            if (ImGui.BeginCombo(LM.Get("GUI_Label_CollisionColorBy"), LM.Get("GUI_MenuItem_CollisionColor_" + settings.CollisionColorMode)))
            {
                foreach (CollisionColorMode mode in Enum.GetValues<CollisionColorMode>())
                    if (ImGui.Selectable(LM.Get("GUI_MenuItem_CollisionColor_" + mode), settings.CollisionColorMode == mode))
                    {
                        settings.CollisionColorMode = mode;
                        persist = true;
                    }
                ImGui.EndCombo();
            }
            ImGui.SetNextItemWidth(160f);
            persist |= ImGui.SliderFloat(LM.Get("GUI_MenuItem_CollisionOpacity"), ref settings.CollisionOpacity, 0.05f, 1f, "%.2f");
            persist |= ImGui.Checkbox(LM.Get("GUI_MenuItem_CollisionOutlines"), ref settings.CollisionOutlines);
        }

        // Filters, each list doubling as its colour mode's legend.
        if (ImGui.CollapsingHeader(LM.Get("GUI_Collision_FilterType")))
            persist |= DrawTypeFilter(settings, summary);
        if (summary.Materials.Count > 0 && ImGui.CollapsingHeader(LM.Get("GUI_Collision_FilterMaterial")))
        {
            DrawValueFilter("mat", summary.Materials.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)),
                v => CollisionGeometry.IdColor(v), v => LM.Get("GUI_Label_CollisionMaterial", v.Key, v.Value),
                v => (_materialMask[v >> 5] & (1u << (v & 31))) != 0,
                (v, on) => _materialMask[v >> 5] = on ? _materialMask[v >> 5] | (1u << (v & 31)) : _materialMask[v >> 5] & ~(1u << (v & 31)));
            ImGui.TextDisabled(LM.Get("GUI_Label_CollisionMaterialNote"));
        }
        if (summary.TriangleFlags.Count > 0 && ImGui.CollapsingHeader(LM.Get("GUI_Collision_FilterTriangleFlags")))
        {
            DrawValueFilter("tri", summary.TriangleFlags.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)),
                v => CollisionGeometry.FlagColor(v), v => LM.Get("GUI_Label_CollisionTriangleFlags", v.Key, FlagNames(v.Key), v.Value),
                v => (_triangleFlagMask & (1u << v)) != 0,
                (v, on) => _triangleFlagMask = on ? _triangleFlagMask | (1u << v) : _triangleFlagMask & ~(1u << v));
            ImGui.TextDisabled(LM.Get("GUI_Label_CollisionTriangleFlagsNote"));
        }
        if (ImGui.CollapsingHeader(LM.Get("GUI_Collision_FilterInstanceFlags")))
        {
            bool changed = false;
            DrawValueFilter("inst", summary.InstanceFlags.OrderBy(kv => kv.Key).Select(kv => (kv.Key, (long)kv.Value)),
                v => CollisionGeometry.IdColor(v), v => LM.Get("GUI_Label_CollisionInstanceFlags", v.Key, v.Value),
                v => !_hiddenInstanceFlags.Contains((ushort)v),
                (v, on) => { changed = true; if (on) _hiddenInstanceFlags.Remove((ushort)v); else _hiddenInstanceFlags.Add((ushort)v); });
            ImGui.TextDisabled(LM.Get("GUI_Label_CollisionInstanceFlagsNote"));
            if (changed)
                foreach (var p in Scene.Placements)
                    WriteInstance(p);
        }

        if (persist) settings.SaveSettingsToFile();
        overlay.EndPanel();
    }

    private void DrawEditSection(EditorSettings settings)
    {
        ImGui.Indent();
        int mode = (int)Mode;
        bool modeChanged = ImGui.RadioButton(LM.Get("GUI_Collision_ModeFaces"), ref mode, (int)CollisionSelectMode.Faces);
        ImGui.SameLine();
        modeChanged |= ImGui.RadioButton(LM.Get("GUI_Collision_ModeInstances"), ref mode, (int)CollisionSelectMode.Instances);
        if (modeChanged && mode != (int)Mode)
        {
            ClearSelection();
            Mode = (CollisionSelectMode)mode;
        }
        ImGui.SameLine();
        ImGui.Checkbox(LM.Get("GUI_Collision_ThroughWalls"), ref SelectThroughWalls);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Collision_ThroughWallsHint"));
        ImGui.TextDisabled(LM.Get("GUI_Collision_SelectHint"));

        // Batch selection.
        if (ImGui.SmallButton(LM.Get("GUI_Collision_SelectAll"))) SelectAllVisible(settings);
        ImGui.SameLine();
        if (Mode == CollisionSelectMode.Faces)
        {
            if (ImGui.SmallButton(LM.Get("GUI_Collision_SameMaterial"))) SelectSimilarFaces(settings, byMaterial: true);
            ImGui.SameLine();
            if (ImGui.SmallButton(LM.Get("GUI_Collision_SameFlags"))) SelectSimilarFaces(settings, byMaterial: false);
        }
        else
        {
            if (ImGui.SmallButton(LM.Get("GUI_Collision_SameShape"))) SelectSimilarInstances(settings, byShape: true);
            ImGui.SameLine();
            if (ImGui.SmallButton(LM.Get("GUI_Collision_SameInstanceFlags"))) SelectSimilarInstances(settings, byShape: false);
            ImGui.SameLine();
            if (ImGui.SmallButton(LM.Get("GUI_Collision_ToFaces"))) FacesOfSelectedInstances();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton(LM.Get("GUI_Collision_Clear"))) ClearSelection();

        // What is selected, and the edits.
        var faces = TargetFaces().ToList();
        if (Mode == CollisionSelectMode.Faces)
        {
            int shapes = _faces.Select(f => f.Shape).Distinct().Count();
            int placements = _faces.Select(f => f.Shape).Distinct().Sum(s => Scene!.Shapes[s].PlacementCount);
            ImGui.TextUnformatted(LM.Get("GUI_Collision_SelectedFaces", _faces.Count, shapes, placements));
        }
        else
        {
            ImGui.TextUnformatted(LM.Get("GUI_Collision_SelectedInstances", _instances.Count, faces.Count));
        }

        if (faces.Count > 0)
        {
            var meshes = faces.Select(f => (Mesh: (CollisionMesh)f.Shape.Copies[0], f.Triangle)).ToList();
            var materials = meshes.Select(f => f.Mesh.Materials[f.Triangle]).Distinct().OrderBy(m => m).ToList();
            ImGui.TextUnformatted(LM.Get("GUI_Collision_Materials", string.Join(", ", materials.Take(12)) + (materials.Count > 12 ? ", ..." : "")));
            if (materials.Count == 1 && !ImGui.IsAnyItemActive()) _materialInput = materials[0];
            int input = _materialInput;
            ImGui.SetNextItemWidth(100f);
            if (ImGui.InputInt("##material", ref input)) _materialInput = (byte)Math.Clamp(input, 0, 255);
            ImGui.SameLine();
            if (ImGui.Button(LM.Get("GUI_Collision_SetMaterial")))
            {
                byte m = _materialInput;
                EditFaces(_ => m, f => f);
            }

            // One checkbox per flag bit: ticked when every target face has it, tinted when only some do
            // (clicking a mixed one sets the bit on all).
            ImGui.TextUnformatted(LM.Get("GUI_Collision_TriangleFlags"));
            ImGui.SameLine();
            ImGui.TextDisabled(LM.Get("GUI_Collision_FlagNone"));
            for (int bit = 0; bit < 5; bit++)
            {
                int with = meshes.Count(f => (f.Mesh.TriangleFlags[f.Triangle] & (1 << bit)) != 0);
                bool all = with == meshes.Count, mixed = with > 0 && !all;
                bool value = all;
                if (mixed) ImGui.PushStyleColor(ImGuiCol.FrameBg, MixedColour);
                if (ImGui.Checkbox($"{bit}: {FlagName(bit)}##triflag{bit}", ref value))
                {
                    byte mask = (byte)(1 << bit);
                    bool on = mixed || value;
                    EditFaces(m => m, f => on ? (byte)(f | mask) : (byte)(f & ~mask));
                }
                if (mixed) ImGui.PopStyleColor();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get(bit == 0 ? "GUI_Collision_FlagBit0" : "GUI_Collision_FlagBitUnknown", with, meshes.Count));
                ImGui.SameLine();
                ImGui.TextDisabled(LM.Get("GUI_Collision_FlagCount", with, meshes.Count));
            }
        }

        if (Mode == CollisionSelectMode.Instances && _instances.Count > 0)
        {
            ImGui.TextUnformatted(LM.Get("GUI_Collision_InstanceFlags"));
            var selected = _instances.Select(i => Scene!.Placements[i].ShapeFlags).ToList();
            for (int bit = 0; bit < 16; bit++)
            {
                ushort mask = (ushort)(1 << bit);
                int with = selected.Count(f => (f & mask) != 0);
                bool all = with == selected.Count, mixed = with > 0 && !all;
                bool value = all;
                if (bit % 8 != 0) ImGui.SameLine();
                bool readOnly = mask == 0x400;
                if (readOnly) ImGui.BeginDisabled();
                if (mixed) ImGui.PushStyleColor(ImGuiCol.FrameBg, MixedColour);
                if (ImGui.Checkbox($"{mask:X}##instflag", ref value))
                    EditInstanceFlags(mask, mixed || value);
                if (mixed) ImGui.PopStyleColor();
                if (readOnly) ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(LM.Get(readOnly ? "GUI_Collision_InstanceFlagPrimitive" : "GUI_Collision_InstanceFlagBit", mask, with, selected.Count));
            }
        }

        if (_originalMeshes.Count > 0 || _originalInstanceFlags.Count > 0)
        {
            ImGui.TextColored(new Vector4(1f, 0.75f, 0.3f, 1f), LM.Get("GUI_Collision_Modified", _modifiedFaces, _modifiedInstances));
            ImGui.SameLine();
            if (ImGui.SmallButton(LM.Get("GUI_Collision_Revert"))) Revert();
        }
        ImGui.Unindent();
        ImGui.Separator();
    }

    /// <summary>The Type filter: categories and primitive kinds, each in its Type colour.</summary>
    private static bool DrawTypeFilter(EditorSettings settings, CollisionSummary summary)
    {
        bool changed = false;
        void Category(CollisionCategories category, string label, Vector3 colour, int count)
        {
            bool on = settings.CollisionCategories.HasFlag(category);
            Swatch(colour, label);
            ImGui.SameLine();
            if (ImGui.Checkbox($"{LM.Get(label)} ({count})###{label}", ref on))
            {
                settings.CollisionCategories = on ? settings.CollisionCategories | category : settings.CollisionCategories & ~category;
                changed = true;
            }
        }
        Category(CollisionCategories.WorldMesh, "GUI_Label_CollisionWorldMesh", CollisionGeometry.WorldMeshColor,
            summary.InstancesByCategory.GetValueOrDefault(CollisionCategories.WorldMesh));
        Category(CollisionCategories.TieMesh, "GUI_Label_CollisionTieMesh", CollisionGeometry.TieMeshColor,
            summary.InstancesByCategory.GetValueOrDefault(CollisionCategories.TieMesh));
        if (summary.InstancesByCategory.ContainsKey(CollisionCategories.Primitives))
        {
            Category(CollisionCategories.Primitives, "GUI_Label_CollisionPrimitives", CollisionGeometry.PrimitiveColor(CollisionPrimitiveKind.Box),
                summary.InstancesByCategory[CollisionCategories.Primitives]);
            ImGui.Indent();
            foreach (var (kind, count) in summary.PrimitivesByKind.OrderBy(kv => kv.Key))
            {
                var bit = (CollisionPrimitiveKinds)(1u << (int)kind);
                bool on = settings.CollisionPrimitiveKinds.HasFlag(bit);
                Swatch(CollisionGeometry.PrimitiveColor(kind), $"kind{kind}");
                ImGui.SameLine();
                if (ImGui.Checkbox(LM.Get("GUI_Label_CollisionPrimitive_" + kind, count) + $"###kind{kind}", ref on))
                {
                    settings.CollisionPrimitiveKinds = on ? settings.CollisionPrimitiveKinds | bit : settings.CollisionPrimitiveKinds & ~bit;
                    changed = true;
                }
            }
            ImGui.Unindent();
        }
        return changed;
    }

    /// <summary>A checkbox per value the level holds, with its colour and count, plus All / None.</summary>
    private static void DrawValueFilter(string id, IEnumerable<(int Key, long Value)> values, Func<int, Vector3> colour,
        Func<(int Key, long Value), string> label, Func<int, bool> isOn, Action<int, bool> set)
    {
        var list = values.ToList();
        if (ImGui.SmallButton($"{LM.Get("GUI_Collision_All")}##{id}")) foreach (var v in list) set(v.Key, true);
        ImGui.SameLine();
        if (ImGui.SmallButton($"{LM.Get("GUI_Collision_None")}##{id}")) foreach (var v in list) set(v.Key, false);
        // A level can hold a few dozen materials; past a screenful, scroll instead of growing the panel.
        float rowHeight = ImGui.GetFrameHeightWithSpacing();
        bool scroll = list.Count > 10;
        if (scroll) ImGui.BeginChild($"##{id}list", new Vector2(0f, rowHeight * 10), ImGuiChildFlags.None);
        foreach (var v in list)
        {
            bool on = isOn(v.Key);
            Swatch(colour(v.Key), $"{id}{v.Key}");
            ImGui.SameLine();
            if (ImGui.Checkbox($"{label(v)}##{id}{v.Key}", ref on)) set(v.Key, on);
        }
        if (scroll) ImGui.EndChild();
    }

    /// <summary>What a triangle flag bit does, as far as it is decoded.</summary>
    private static string FlagName(int bit) => LM.Get(bit == 0 ? "GUI_Collision_FlagName0" : "GUI_Collision_FlagNameUnknown");

    /// <summary>A flags value by its bits' names; no bit set is the default walkable surface.</summary>
    private static string FlagNames(int flags) =>
        flags == 0 ? LM.Get("GUI_Collision_FlagNone") :
        string.Join(" + ", Enumerable.Range(0, 5).Where(b => (flags & (1 << b)) != 0).Select(b => $"{b} {FlagName(b)}"));

    private static void Swatch(Vector3 colour, string id)
    {
        float size = ImGui.GetTextLineHeight();
        ImGui.ColorButton($"##swatch_{id}", new Vector4(colour, 1f),
            ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoPicker | ImGuiColorEditFlags.NoDragDrop, new Vector2(size, size));
    }
}
