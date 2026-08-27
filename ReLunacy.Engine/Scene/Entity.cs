using System.Numerics;
using ReLunacy.Engine.Rendering.Resources;

namespace ReLunacy.Engine.Scene;

public abstract class Entity : IDisposable
{
    public static int EntityIndex = 0;
    public static int EntitiesRenderedThisFrame = 0;

    public int ID { get; protected set; }
    public bool allowRender = true;
    public bool selected = false;

    private Transform transform = new();
    public Transform Transform
    {
        get => transform;
        set { transform = value; IsDirty = true; }
    }

    /// <summary>Bounding sphere in LOCAL space - center is an offset from this entity's own pivot, not an absolute world position. Set once at load and never needs updating when the entity moves; see <see cref="WorldBoundingSphere"/> for the world-space value used by culling/rendering.</summary>
    public abstract Vector4 BoundingSphere { get; set; }

    /// <summary>Current world-space bounding sphere, tracking <see cref="Transform"/> live, including
    /// mid-drag via the gizmo. This is what the renderer frustum-culls against.
    ///
    /// The local centre goes through the WHOLE transform, not just its translation. It is an offset in
    /// the entity's own space, so a rotated entity whose model origin is not at its centre needs that
    /// offset rotated with it, and a scaled one needs it scaled. Adding the raw offset to the
    /// translation (what this used to do) left the sphere in the wrong place for every rotated tie and
    /// every moby placed with a scale, which showed up as geometry vanishing while still on screen.
    ///
    /// The radius scales by the LARGEST absolute scale component: a sphere under non-uniform scale is
    /// bounded by one of radius r * max|s|, and over-estimating only costs a few draws that survive the
    /// cull, where under-estimating clips something the camera can see.</summary>
    public Vector4 WorldBoundingSphere
    {
        get
        {
            var transform = Transform;
            var localCenter = new Vector3(BoundingSphere.X, BoundingSphere.Y, BoundingSphere.Z);
            var worldCenter = Vector3.Transform(localCenter, transform.GetMatrix());
            float maxScale = MathF.Max(
                MathF.Abs(transform.Scale.X),
                MathF.Max(MathF.Abs(transform.Scale.Y), MathF.Abs(transform.Scale.Z)));
            return new Vector4(worldCenter, BoundingSphere.W * maxScale * BoundingSphereMargin);
        }
    }

    public abstract string Name { get; protected set; }
    public bool IsDirty { get; set; } = true;

    /// <summary>Slack on the culling radius. Measuring every instance's transformed vertices against
    /// its own sphere on metropolis put the tightest ties, mobys and UFrags at 1.000 to 1.006 of it, so
    /// the assets' own fitted radii are very slightly optimistic. A sliver of geometry outside the
    /// sphere is a visible pop at the screen edge; a 2% larger sphere is a handful of extra draws.</summary>
    private const float BoundingSphereMargin = 1.02f;

    protected List<Renderable> cachedRenderables = [];

    protected Entity()
    {
        ID = EntityIndex++;
    }

    /// <summary>Rebuilds <see cref="cachedRenderables"/> if <see cref="IsDirty"/>. Every entity type
    /// that caches renderables overrides this; Draw and GetRenderablesForVk both go through it.
    ///
    /// It has to be reachable from OUTSIDE Draw because the raw-Vulkan renderer never calls Draw. A
    /// gizmo edit ASSIGNS a new Transform (GizmoController) rather than mutating the existing one, so
    /// the Renderables built earlier keep pointing at the old Transform object and hand back stale
    /// matrices until they are rebuilt. Leaving that rebuild inside Draw meant edits never reached the
    /// VK path, and that the initial scene capture could only see entities that some earlier frame
    /// happened to have drawn.</summary>
    protected virtual void EnsureRenderables() { }

    /// <summary>Every mesh this entity places, with the material that placement is drawn with.
    ///
    /// The material is per-RENDERABLE, not per-mesh, which is what lit ties need: one tie model is
    /// shared across many placements, but each placement's baked lightmap lives on its own material
    /// (EntityTie builds <c>new Renderable(mesh, Transform, perInstanceMaterial)</c>).
    /// Rebuilds the cache first (see <see cref="EnsureRenderables"/>) so it never depends on anything
    /// else having run this frame.</summary>
    public virtual IEnumerable<(RenderMesh mesh, RenderMaterial material, Matrix4x4 world, Vector4 sphere)> GetRenderablesForVk()
    {
        EnsureRenderables();
        // The game's own per-entity world bounding sphere (xyz centre, w radius). Shared across the
        // entity's renderables, so the renderer culls at entity granularity like the game rather than
        // from a looser per-mesh AABB sphere.
        var sphere = WorldBoundingSphere;
        var results = new List<(RenderMesh, RenderMaterial, Matrix4x4, Vector4)>();
        foreach (var renderable in cachedRenderables)
        {
            var transforms = renderable.GetTransforms();
            int count = renderable.InstanceCount;
            for (int i = 0; i < count; i++)
                results.Add((renderable.Mesh, renderable.Material, transforms[i].GetMatrix(), sphere));
        }
        return results;
    }

    public virtual void Dispose() { }
}
