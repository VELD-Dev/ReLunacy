using System.Numerics;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Rendering;
using Veldrith;

namespace ReLunacy.Engine.Scene;

public class EntityVolume : Entity
{
    public readonly Volume BaseVolume;

    public override Vector4 BoundingSphere { get; set; } = Vector4.Zero;
    /// <summary>The box's real (possibly non-uniform) half-extents source - kept separate from
    /// Transform.Scale (always 1,1,1 for a Volume) because each edge instance takes this as an
    /// explicit length rather than folding it into the placement transform. Only settable via
    /// <see cref="SetScale"/>, which keeps BoundingSphere and the edge instances in sync with it -
    /// never assign this field directly.</summary>
    public Vector3 scale { get; private set; }

    public override string Name { get; protected set; }

    // A volume has no mesh and no material of its own. The renderer draws its 12 wireframe edges
    // straight from GetWorldEdgeTransforms and VolumeColour, with its own thin-box edge geometry (see
    // VulkanRenderer's edge cube, which matches what Primitives.CreateWireEdge used to build) and its
    // own flat-colour pipeline.
    //
    // This used to be a shared unit-length edge mesh plus a pair of tinted materials, rebuilt whenever
    // the colour settings or the wire thickness changed. All of it fed a render path that no longer
    // exists, and none of it was ever reached by the current one: the edge mesh was never registered
    // with the capture registry, so the scene walk skipped these renderables outright. What remains is
    // the part that was always doing the work, the 12 edge transforms.

    private Transform[] _edgeTransforms = [];

    public EntityVolume(Volume volume)
    {
        BaseVolume = volume;
        Name = !string.IsNullOrEmpty(volume.Name) ? volume.Name : $"Volume_{ID}";

        Matrix4x4.Decompose(volume.transform, out var initialScale, out var rotation, out var position);

        Transform = new Transform { Translation = position, Rotation = rotation, Scale = Vector3.One };

        SetScale(initialScale);
    }

    /// <summary>Sets <see cref="scale"/> and, in the same step, recomputes the local-space bounding
    /// sphere and the 12 edge transforms. The three always have to change together, so this is the
    /// only way to change the volume's size (from the Property Inspector or otherwise). Rebuilds
    /// synchronously rather than leaving it to the IsDirty check, so a resize can never report the
    /// pre-resize size for a frame.</summary>
    public void SetScale(Vector3 newScale)
    {
        scale = newScale;

        // BoundingSphere is LOCAL space per Entity's convention (offset from Transform.Translation)
        // - center coincides with the volume's own position (zero local offset), and the radius is
        // the distance from that center to the cube's furthest corner: the half-extents vector's
        // length, since one corner sits at exactly (sx/2, sy/2, sz/2) from center.
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
        // Not an iterator itself: the freshness check has to run when this is CALLED, not when it is
        // first enumerated. Resizing rebuilds synchronously (SetScale), but a gizmo move or rotate only
        // sets IsDirty, so the rebuild has to happen somewhere the renderer actually reaches.
        EnsureRenderables();
        return EnumerateWorldEdgeTransforms();
    }

    private IEnumerable<Matrix4x4> EnumerateWorldEdgeTransforms()
    {
        // _edgeTransforms are ALREADY world transforms: ComposeEdgeTransform folds this volume's own
        // Transform in when it builds them. Composing again here would apply the volume's placement
        // twice and offset every box.
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

    /// <summary>Builds one edge's full WORLD Transform by composing its volume-local placement
    /// (length/orientation/offset) with this volume's own Transform, matching the same
    /// Matrix4x4.Decompose-based composition already used to derive the volume's own Transform
    /// from its source data in the constructor. Matrix4x4.Decompose can theoretically fail on a
    /// degenerate input (never expected here - this volume's own Transform.Scale is always
    /// Vector3.One, so there's no shear/reflection to trip it up), in which case the edge falls
    /// back to this volume's own placement with a zero local offset rather than leaving it at a
    /// stale or default Transform.</summary>
    private Transform ComposeEdgeTransform(Vector3 lengthAxis, float length, Vector3 localCenter)
    {
        var local = new Transform
        {
            // Scale is applied in local mesh space BEFORE rotation (see Transform.GetMatrix()'s
            // Scale*Rotation*Translation order), so Scale.X always stretches SharedEdgeMesh's own
            // local length axis regardless of the rotation below - this is what keeps the
            // thickness axes (Y/Z, left at 1) constant no matter how long the edge is.
            Scale = new Vector3(length, 1f, 1f),
            Rotation = AlignUnitXTo(lengthAxis),
            Translation = localCenter,
        };

        var combined = local.GetMatrix() * Transform.GetMatrix();
        if (!Matrix4x4.Decompose(combined, out var decomposedScale, out var decomposedRotation, out var decomposedTranslation))
            return new Transform { Translation = Transform.Translation, Rotation = Transform.Rotation };

        return new Transform { Scale = decomposedScale, Rotation = decomposedRotation, Translation = decomposedTranslation };
    }

    /// <summary>Rotation aligning SharedEdgeMesh's local +X (its length axis) to point along
    /// <paramref name="axis"/> (always UnitX/UnitY/UnitZ). Only the axis LINE matters, not its
    /// polarity - the edge mesh is symmetric about its own center and radially symmetric in
    /// cross-section, so a +90 deg/-90 deg sign mismatch here would still produce an identical result.</summary>
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
