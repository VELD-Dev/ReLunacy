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

    public EntityVolume(Volume volume)
    {
        BaseVolume = volume;
        Name = !string.IsNullOrEmpty(volume.Name) ? volume.Name : $"Volume_{ID}";

        Matrix4x4.Decompose(volume.transform, out var initialScale, out var rotation, out var position);

        Transform = new Transform { Translation = position, Rotation = rotation, Scale = Vector3.One };

        SetScale(initialScale);
        LoadedMatrix = FillMatrix;
    }

    /// <summary><see cref="FillMatrix"/> as loaded. A file matrix with shear (12 Clue volumes of Into the Nexus
    /// battle_arena) is not exactly what scale, rotation and translation rebuild, so a save compares with this to
    /// know whether the volume was edited, and moves the file's own matrix by the edit.</summary>
    public Matrix4x4 LoadedMatrix { get; }

    /// <summary>Sets <see cref="scale"/> and recomputes the bounding sphere and the edge transforms
    /// together, synchronously.</summary>
    public void SetScale(Vector3 newScale)
    {
        scale = newScale;

        // BoundingSphere is LOCAL space per Entity's convention: center coincides with the volume's
        // own position, and the radius is the half-extents vector's length (distance to the cube's
        // furthest corner).
        BoundingSphere = new Vector4(Vector3.Zero, (scale / 2f).Length());

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

    /// <summary>The world matrix of the volume's unit shape (+-0.5 box, radius-0.5 sphere, cylinder along Y),
    /// for its translucent faces.</summary>
    public Matrix4x4 FillMatrix => Matrix4x4.CreateScale(scale) * Transform.GetMatrix();

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
        Vector3 h = scale * 0.5f;
        switch (BaseVolume.Shape)
        {
            case VolumeShape.Sphere:
                AddRing(edges, Vector3.Zero, new Vector3(h.X, 0f, 0f), new Vector3(0f, 0f, h.Z), 1f);
                AddRing(edges, Vector3.Zero, new Vector3(h.X, 0f, 0f), new Vector3(0f, h.Y, 0f), 1f);
                AddRing(edges, Vector3.Zero, new Vector3(0f, 0f, h.Z), new Vector3(0f, h.Y, 0f), 1f);
                // A sphere with a role (a moon's gravity) is drawn as a globe, so it reads as one from any side:
                // meridians every 30 degrees and parallels at 30 and 60 degrees.
                if (BaseVolume.Role != null)
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
                AddAxisEdges(Vector3.UnitX, scale.X, Vector3.UnitY, scale.Y, Vector3.UnitZ, scale.Z, edges);
                AddAxisEdges(Vector3.UnitY, scale.Y, Vector3.UnitX, scale.X, Vector3.UnitZ, scale.Z, edges);
                AddAxisEdges(Vector3.UnitZ, scale.Z, Vector3.UnitX, scale.X, Vector3.UnitY, scale.Y, edges);
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
