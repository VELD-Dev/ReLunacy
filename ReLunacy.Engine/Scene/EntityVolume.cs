using System.Numerics;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Rendering;
using NeoVeldrid;

namespace ReLunacy.Engine.Scene;

public class EntityVolume : Entity
{
    public readonly Volume BaseVolume;

    public override Vector4 BoundingSphere { get; set; } = Vector4.Zero;
    /// <summary>The box's real (possibly non-uniform) half-extents. Only settable via
    /// <see cref="SetScale"/>, which keeps BoundingSphere and the edge instances in sync - never
    /// assign this field directly.</summary>
    public Vector3 scale { get; private set; }

    public override string Name { get; protected set; }

    // A volume has no mesh or material of its own; the renderer draws its wireframe edges (12 for a
    // box, rings and lines for the round shapes) directly from GetWorldEdgeTransforms and VolumeColour.

    private Transform[] _edgeTransforms = [];

    /// <summary>
    /// How large a volume's unit shape is, in its matrix's units: the new engine's are radius / half-size 1 - the game's
    /// containment test (A Crack in Time 0x0061A124) takes a point into the volume's frame and tests |x|, |y|, |z| (box),
    /// the distance from the Y axis and |y| (cylinder), or the distance from the centre (sphere) against 1.0 - twice
    /// the +-0.5 unit shapes the editor used to draw (the user saw moon areas and satellite_find / _keep twice as large
    /// in game; moon1b's two 75 boxes only enclose its radius-50 moon at +-1). The old engine keeps +-0.5. Set by the
    /// region before its volumes are built; each volume keeps the value it was built with.
    /// </summary>
    public static float NextShapeScale { get; set; } = 1f;

    /// <summary>This volume's shape scale (see <see cref="NextShapeScale"/>): what its matrix's scale is multiplied by
    /// to give the size of the shape the game tests against.</summary>
    public float ShapeScale { get; } = NextShapeScale;

    /// <summary>The shape's full size along each axis, as the game tests it: <see cref="scale"/> x <see cref="ShapeScale"/>.</summary>
    public Vector3 ShapeSize => scale * ShapeScale;

    public EntityVolume(Volume volume)
    {
        BaseVolume = volume;
        Name = !string.IsNullOrEmpty(volume.Name) ? volume.Name : $"Volume_{ID}";

        Matrix4x4.Decompose(volume.transform, out var initialScale, out var rotation, out var position);

        Transform = new Transform { Translation = position, Rotation = rotation, Scale = Vector3.One };

        SetScale(initialScale);
        LoadedMatrix = FillMatrix;
    }

    /// <summary><see cref="FillMatrix"/> as the file last held it: as loaded, then as last saved. A file matrix with
    /// shear (12 Clue volumes of Into the Nexus battle_arena) is not exactly what scale, rotation and translation
    /// rebuild, so a save compares with this to know whether the volume was edited, and moves the file's own matrix
    /// by the edit. It MUST follow every save (<see cref="MarkSaved"/>): the file then holds the edit, and moving
    /// it again from the load-time matrix applied the edit twice.</summary>
    public Matrix4x4 LoadedMatrix { get; private set; }

    /// <summary>The file now holds this volume as it is: later edits are measured from here.</summary>
    public void MarkSaved() => LoadedMatrix = FillMatrix;

    /// <summary>
    /// Places the volume from a whole matrix (a gizmo drag): its size goes to <see cref="scale"/>, its rotation and
    /// position to <see cref="Entity.Transform"/>, whose scale stays one. The wireframe's edges are built from the
    /// size in the volume's own frame, so they keep their thickness however the volume is sized; a scale left in
    /// the Transform would scale the edges' thickness with it.
    /// </summary>
    public void SetMatrix(Matrix4x4 matrix)
    {
        if (!Matrix4x4.Decompose(matrix, out var newScale, out var rotation, out var translation)) return;
        Transform = new Transform { Translation = translation, Rotation = rotation, Scale = Vector3.One };
        SetScale(newScale);
    }

    /// <summary>Sets <see cref="scale"/> and recomputes the bounding sphere and the edge transforms
    /// together, synchronously.</summary>
    public void SetScale(Vector3 newScale)
    {
        scale = newScale;

        // BoundingSphere is LOCAL space per Entity's convention: center coincides with the volume's
        // own position, and the radius is the half-extents vector's length (distance to the cube's
        // furthest corner).
        BoundingSphere = new Vector4(Vector3.Zero, (ShapeSize / 2f).Length());

        RecomputeEdgeTransforms();
        IsDirty = false;
    }

    /// <summary>The world matrices of this volume's wireframe edges. Each places and stretches a
    /// unit-length edge (see RecomputeEdgeTransforms / ComposeEdgeTransform). The renderer draws its own
    /// thin-box edge geometry at these, so the wireframe matches the pick target exactly (and honours
    /// VolumeWireThickness).</summary>
    public IEnumerable<Matrix4x4> GetWorldEdgeTransforms()
    {
        // Not an iterator itself: the freshness check must run when called, not when first
        // enumerated, since a gizmo move or rotate only sets IsDirty.
        EnsureRenderables();
        return EnumerateWorldEdgeTransforms();
    }

    private IEnumerable<Matrix4x4> EnumerateWorldEdgeTransforms()
    {
        // _edgeTransforms are already world transforms; composing again here would double-apply the
        // volume's placement.
        foreach (var e in _edgeTransforms)
            yield return e.GetMatrix();
    }

    /// <summary>The volume's matrix as the file holds it: its unit shape scaled, turned and placed (the gizmo works on it
    /// and a save writes it). The shape the game tests is <see cref="ShapeSize"/>, drawn by <see cref="ShapeMatrix"/>.</summary>
        public Matrix4x4 FillMatrix => Matrix4x4.CreateScale(scale) * Transform.GetMatrix();

    /// <summary>The faces' matrix: the unit fill shapes (+-0.5) at the size the game tests (<see cref="ShapeSize"/>).</summary>
    public Matrix4x4 ShapeMatrix => Matrix4x4.CreateScale(ShapeSize) * Transform.GetMatrix();

    protected override void EnsureRenderables()
    {
        if (!IsDirty) return;
        RecomputeEdgeTransforms();
        IsDirty = false;
    }

    /// <summary>Current wireframe colour (RGBA, 0..1): the selected tint, else <see cref="KindColour"/>.</summary>
    public Vector4 VolumeColour => selected ? EntityManager.Singleton.VolumeSelectedColor : KindColour;

    /// <summary>The colour EntityManager.VolumeColorMode gives this volume (kind, layer or the volume colour),
    /// selected or not: its faces' colour, and the swatch the hierarchy and the volume panel show.</summary>
    public Vector4 KindColour
    {
        get
        {
            var em = EntityManager.Singleton;
            return em.VolumeColorMode switch
            {
                VolumeColorMode.Kind when BaseVolume.Clue is { } clue => new Vector4(ClueClasses.Colour(clue.ClassName), 1f),
                VolumeColorMode.Kind when BaseVolume.Role is { } role => new Vector4(ClueClasses.RoleColour(role), 1f),
                VolumeColorMode.Layer when BaseVolume.Layer is { } layer => new Vector4(ClueClasses.ColourOf(layer), 1f),
                _ => em.VolumeColor,
            };
        }
    }

    /// <summary>Recomputes the per-edge Transforms from the current <see cref="scale"/> and the volume's
    /// shape: a box's 12 edges, or rings and lines for the round shapes. Each is a unit-length segment
    /// stretched along its own local X (see <see cref="ComposeEdgeTransform"/>). The shapes are the unit
    /// shapes the box is, scaled the same way: a sphere's diameter is the box's size along each axis, so a
    /// non-uniform scale makes an ellipsoid, as the matrix does in game.</summary>
    private void RecomputeEdgeTransforms()
    {
        var edges = new List<Transform>(12);
        Vector3 size = ShapeSize;
        Vector3 h = size * 0.5f;
        switch (BaseVolume.Shape)
        {
            case VolumeShape.Sphere:
                AddRing(edges, Vector3.Zero, new Vector3(h.X, 0f, 0f), new Vector3(0f, 0f, h.Z), 1f);
                AddRing(edges, Vector3.Zero, new Vector3(h.X, 0f, 0f), new Vector3(0f, h.Y, 0f), 1f);
                AddRing(edges, Vector3.Zero, new Vector3(0f, 0f, h.Z), new Vector3(0f, h.Y, 0f), 1f);
                // A gravity region (a moon's SpaceMoon sphere) is drawn as a globe, so it reads as one from any
                // side: meridians every 30 degrees and parallels at 30 and 60 degrees.
                if (BaseVolume.GravityRegion)
                {
                    foreach (float degrees in new[] { 30f, 60f, 120f, 150f })
                    {
                        float a = float.DegreesToRadians(degrees);
                        AddRing(edges, Vector3.Zero, new Vector3(h.X * MathF.Cos(a), 0f, h.Z * MathF.Sin(a)), new Vector3(0f, h.Y, 0f), 1f);
                    }
                    foreach (float degrees in new[] { -60f, -30f, 30f, 60f })
                    {
                        float a = float.DegreesToRadians(degrees);
                        AddRing(edges, new Vector3(0f, h.Y * MathF.Sin(a), 0f), new Vector3(h.X * MathF.Cos(a), 0f, 0f), new Vector3(0f, 0f, h.Z * MathF.Cos(a)), 1f);
                    }
                }
                break;
            case VolumeShape.Cylinder:
                AddRoundSides(edges, h, h.Y);
                break;
            case VolumeShape.Capsule:
            {
                // The caps are hemispheres of the smaller horizontal radius, inside the box like the rest:
                // the straight part is what the height leaves once both caps are in.
                float cap = MathF.Min(MathF.Min(h.X, h.Z), h.Y);
                float side = h.Y - cap;
                AddRoundSides(edges, h, side);
                foreach (float sy in Signs)
                {
                    var centre = new Vector3(0f, sy * side, 0f);
                    var up = new Vector3(0f, sy * cap, 0f);
                    AddRing(edges, centre, new Vector3(h.X, 0f, 0f), up, 0.5f);
                    AddRing(edges, centre, new Vector3(0f, 0f, h.Z), up, 0.5f);
                }
                break;
            }
            default:
                AddAxisEdges(Vector3.UnitX, size.X, Vector3.UnitY, size.Y, Vector3.UnitZ, size.Z, edges);
                AddAxisEdges(Vector3.UnitY, size.Y, Vector3.UnitX, size.X, Vector3.UnitZ, size.Z, edges);
                AddAxisEdges(Vector3.UnitZ, size.Z, Vector3.UnitX, size.X, Vector3.UnitY, size.Y, edges);
                break;
        }
        _edgeTransforms = [.. edges];
    }

    private static readonly float[] Signs = [-1f, 1f];

    /// <summary>Segments per full ring: enough to read as round at the sizes volumes come in.</summary>
    private const int RingSegments = 32;

    private void AddAxisEdges(Vector3 axisLength, float lengthExtent, Vector3 axisB, float extentB, Vector3 axisC, float extentC, List<Transform> edges)
    {
        foreach (float sb in Signs)
        {
            foreach (float sc in Signs)
            {
                Vector3 localCenter = axisB * (sb * extentB * 0.5f) + axisC * (sc * extentC * 0.5f);
                edges.Add(ComposeEdgeTransform(axisLength, MathF.Max(lengthExtent, 0.0001f), localCenter));
            }
        }
    }

    /// <summary>A cylinder's sides along local Y: its two end rings at +-<paramref name="halfLength"/> and
    /// four lines joining them.</summary>
    private void AddRoundSides(List<Transform> edges, Vector3 h, float halfLength)
    {
        var x = new Vector3(h.X, 0f, 0f);
        var z = new Vector3(0f, 0f, h.Z);
        foreach (float sy in Signs)
            AddRing(edges, new Vector3(0f, sy * halfLength, 0f), x, z, 1f);
        if (halfLength <= 0f)
            return;
        foreach (var p in (ReadOnlySpan<Vector3>)[x, -x, z, -z])
            AddSegment(edges, p - new Vector3(0f, halfLength, 0f), p + new Vector3(0f, halfLength, 0f));
    }

    /// <summary>The ellipse centre + a cos t + b sin t for t over <paramref name="turns"/> of a full turn
    /// from 0 (a half turn draws the arc on the +b side).</summary>
    private void AddRing(List<Transform> edges, Vector3 centre, Vector3 a, Vector3 b, float turns)
    {
        int count = Math.Max(1, (int)(RingSegments * turns));
        Vector3 previous = centre + a;
        for (int k = 1; k <= count; k++)
        {
            float t = MathF.Tau * turns * k / count;
            Vector3 next = centre + a * MathF.Cos(t) + b * MathF.Sin(t);
            AddSegment(edges, previous, next);
            previous = next;
        }
    }

    private void AddSegment(List<Transform> edges, Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float length = d.Length();
        if (length < 1e-6f)
            return;
        edges.Add(ComposeEdgeTransform(d / length, length, (from + to) * 0.5f));
    }

    /// <summary>Builds one edge's world Transform by composing its volume-local placement with this
    /// volume's own Transform. Falls back to the volume's own placement if Decompose fails (not
    /// expected here, since Transform.Scale is always Vector3.One for a volume).</summary>
    private Transform ComposeEdgeTransform(Vector3 lengthAxis, float length, Vector3 localCenter)
    {
        var local = new Transform
        {
            // Scale is applied before rotation, so Scale.X always stretches the edge mesh's own
            // length axis regardless of rotation.
            Scale = new Vector3(length, 1f, 1f),
            Rotation = AlignUnitXTo(lengthAxis),
            Translation = localCenter,
        };

        var combined = local.GetMatrix() * Transform.GetMatrix();
        if (!Matrix4x4.Decompose(combined, out var decomposedScale, out var decomposedRotation, out var decomposedTranslation))
            return new Transform { Translation = Transform.Translation, Rotation = Transform.Rotation };

        return new Transform { Scale = decomposedScale, Rotation = decomposedRotation, Translation = decomposedTranslation };
    }

    /// <summary>
    /// The edges of a globe of <paramref name="radius"/> at <paramref name="centre"/> (equator, meridians every 30
    /// degrees, parallels at 30 and 60), as world matrices of the unit edge mesh, the way a volume's own edges are
    /// placed: a preview sphere drawn with the volumes' wireframe (a moon's radius, ClueClasses.RadiiOf).
    /// </summary>
    public static IEnumerable<Matrix4x4> GlobeEdges(Vector3 centre, float radius)
    {
        const int segments = 32;
        var rings = new List<(Vector3 Centre, Vector3 A, Vector3 B)> { (Vector3.Zero, new(radius, 0, 0), new(0, 0, radius)) };
        for (int degrees = 0; degrees < 180; degrees += 30)
        {
            float a = float.DegreesToRadians(degrees);
            rings.Add((Vector3.Zero, new(radius * MathF.Cos(a), 0, radius * MathF.Sin(a)), new(0, radius, 0)));
        }
        foreach (float degrees in new[] { -60f, -30f, 30f, 60f })
        {
            float a = float.DegreesToRadians(degrees);
            float r = radius * MathF.Cos(a);
            rings.Add((new(0, radius * MathF.Sin(a), 0), new(r, 0, 0), new(0, 0, r)));
        }
        foreach (var (c, a, b) in rings)
        {
            Vector3 previous = c + a;
            for (int k = 1; k <= segments; k++)
            {
                float t = MathF.Tau * k / segments;
                Vector3 next = c + a * MathF.Cos(t) + b * MathF.Sin(t);
                Vector3 d = next - previous;
                float length = d.Length();
                if (length > 1e-6f)
                    yield return Matrix4x4.CreateScale(length, 1f, 1f)
                               * Matrix4x4.CreateFromQuaternion(AlignUnitXTo(d / length))
                               * Matrix4x4.CreateTranslation(centre + (previous + next) * 0.5f);
                previous = next;
            }
        }
    }

    /// <summary>Rotation aligning the edge mesh's local +X (its length axis) to the unit vector
    /// <paramref name="axis"/>. Only the axis line matters, not polarity, since the edge mesh is
    /// radially symmetric.</summary>
    private static Quaternion AlignUnitXTo(Vector3 axis)
    {
        float dot = Vector3.Dot(Vector3.UnitX, axis);
        if (dot > 0.99999f || dot < -0.99999f) return Quaternion.Identity;
        Vector3 cross = Vector3.Normalize(Vector3.Cross(Vector3.UnitX, axis));
        return Quaternion.CreateFromAxisAngle(cross, MathF.Acos(Math.Clamp(dot, -1f, 1f)));
    }

    public override void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
