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

    /// <summary>Bounding sphere in local space, offset from this entity's own pivot. Set once at load; see <see cref="WorldBoundingSphere"/> for the world-space value used by culling/rendering.</summary>
    public abstract Vector4 BoundingSphere { get; set; }

    /// <summary>World-space bounding sphere, tracking <see cref="Transform"/> live. This is what the
    /// renderer frustum-culls against. The local centre is transformed through the whole transform
    /// (not just translation), and the radius scales by the largest absolute scale component.</summary>
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

    /// <summary>Slack on the culling radius; the assets' own fitted radii are very slightly optimistic.</summary>
    private const float BoundingSphereMargin = 1.02f;

    protected List<Renderable> cachedRenderables = [];

    protected Entity()
    {
        ID = EntityIndex++;
    }

    /// <summary>Rebuilds <see cref="cachedRenderables"/> if <see cref="IsDirty"/>. Called from both
    /// Draw and GetRenderablesForVk so the raw-Vulkan renderer also sees rebuilds.</summary>
    protected virtual void EnsureRenderables() { }

    /// <summary>Every mesh this entity places, with the material that placement is drawn with. Material
    /// is per-renderable rather than per-mesh, since some placements (e.g. lit ties) need their own
    /// baked-lighting material.</summary>
    public virtual IEnumerable<(RenderMesh mesh, RenderMaterial material, Matrix4x4 world, Vector4 sphere)> GetRenderablesForVk()
    {
        EnsureRenderables();
        // Per-entity world bounding sphere (xyz centre, w radius), shared across all this entity's renderables.
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
