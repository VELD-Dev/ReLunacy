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

    // A volume has no mesh or material of its own; the renderer draws its 12 wireframe edges
    // directly from GetWorldEdgeTransforms and VolumeColour.

    private Transform[] _edgeTransforms = [];

    public EntityVolume(Volume volume)
    {
        BaseVolume = volume;
        Name = !string.IsNullOrEmpty(volume.Name) ? volume.Name : $"Volume_{ID}";

        Matrix4x4.Decompose(volume.transform, out var initialScale, out var rotation, out var position);

        Transform = new Transform { Translation = position, Rotation = rotation, Scale = Vector3.One };

        SetScale(initialScale);
    }

    /// <summary>Sets <see cref="scale"/> and recomputes the bounding sphere and the 12 edge transforms
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

    /// <summary>The 12 world matrices of this volume's wireframe edges. Each places and stretches a
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

    protected override void EnsureRenderables()
    {
        if (!IsDirty) return;
        RecomputeEdgeTransforms();
        IsDirty = false;
    }

    /// <summary>Current wireframe colour (RGBA, 0..1): the selected or unselected volume tint.</summary>
    public Vector4 VolumeColour => selected ? EntityManager.Singleton.VolumeSelectedColor : EntityManager.Singleton.VolumeColor;

    /// <summary>Recomputes the 12 per-edge local Transforms (position/orientation/length) from the
    /// current <see cref="scale"/>. Each edge is a unit-length segment stretched along its own local X (see
    /// <see cref="ComposeEdgeTransform"/>) and placed at one of the box's 4 corners parallel to
    /// that axis, same layout the old single-mesh CreateWireBox used.</summary>
    private void RecomputeEdgeTransforms()
    {
        var edges = new Transform[12];
        int i = 0;
        AddAxisEdges(Vector3.UnitX, scale.X, Vector3.UnitY, scale.Y, Vector3.UnitZ, scale.Z, edges, ref i);
        AddAxisEdges(Vector3.UnitY, scale.Y, Vector3.UnitX, scale.X, Vector3.UnitZ, scale.Z, edges, ref i);
        AddAxisEdges(Vector3.UnitZ, scale.Z, Vector3.UnitX, scale.X, Vector3.UnitY, scale.Y, edges, ref i);
        _edgeTransforms = edges;
    }

    private static readonly float[] Signs = [-1f, 1f];

    private void AddAxisEdges(Vector3 axisLength, float lengthExtent, Vector3 axisB, float extentB, Vector3 axisC, float extentC, Transform[] edges, ref int i)
    {
        foreach (float sb in Signs)
        {
            foreach (float sc in Signs)
            {
                Vector3 localCenter = axisB * (sb * extentB * 0.5f) + axisC * (sc * extentC * 0.5f);
                edges[i++] = ComposeEdgeTransform(axisLength, MathF.Max(lengthExtent, 0.0001f), localCenter);
            }
        }
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

    /// <summary>Rotation aligning the edge mesh's local +X (its length axis) to <paramref name="axis"/>
    /// (always UnitX/UnitY/UnitZ). Only the axis line matters, not polarity, since the edge mesh is
    /// radially symmetric.</summary>
    private static Quaternion AlignUnitXTo(Vector3 axis)
    {
        if (axis == Vector3.UnitY) return Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
        if (axis == Vector3.UnitZ) return Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2f);
        return Quaternion.Identity;
    }

    public override void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
