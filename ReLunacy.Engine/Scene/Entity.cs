using System.Numerics;
using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Colors;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
using Bliss.CSharp.Transformations;
using Veldrith;

namespace ReLunacy.Engine.Scene;

public abstract class Entity : IDisposable
{
    public static int EntityIndex = 0;
    public static int EntitiesRenderedThisFrame = 0;

    public int ID { get; protected set; }
    public bool allowRender = true;
    public bool selected = false;

    private Transform transform;
    public Transform Transform
    {
        get => transform;
        set { transform = value; IsDirty = true; }
    }

    /// <summary>Bounding sphere in LOCAL space — center is an offset from this entity's own pivot, not an absolute world position. Set once at load and never needs updating when the entity moves; see <see cref="WorldBoundingSphere"/> for the world-space value used by culling/rendering.</summary>
    public abstract Vector4 BoundingSphere { get; set; }

    /// <summary>Current world-space bounding sphere, tracking <see cref="Transform"/> live — always reflects the entity's current position, including mid-drag via the gizmo.</summary>
    public Vector4 WorldBoundingSphere
    {
        get
        {
            var worldCenter = Transform.Translation + new Vector3(BoundingSphere.X, BoundingSphere.Y, BoundingSphere.Z);
            return new Vector4(worldCenter, BoundingSphere.W);
        }
    }

    public abstract string Name { get; protected set; }
    public bool IsDirty { get; set; } = true;

    protected List<Renderable> cachedRenderables = [];

    protected Entity()
    {
        ID = EntityIndex++;
    }

    public abstract void Draw(IRenderer renderer, OutputDescription outputDescription, CommandList commandList, Cam3D camera, ImmediateRenderer immediateRenderer);

    /// <summary>Meshes to draw for GPU picking, each with its own already-fully-world-baked
    /// transform, all tagged with this entity's own ID — populated from the last Draw() call. A
    /// Moby's bangles/submeshes all resolve back to the one Moby entity. Reads each Renderable's
    /// OWN Transform(s) rather than this entity's Transform directly: every entity (Moby/Tie/UFrag,
    /// and EntityVolume's 12 separate per-edge Renderables) constructs each Renderable with the
    /// exact Transform that Renderable should be drawn/picked at, so this is just trusting that
    /// directly instead of recomputing/assuming it's always equal to Entity.Transform — which lets
    /// an entity with more than one Renderable (like EntityVolume) report each one's real world
    /// position instead of collapsing them all onto one shared matrix. GetTransforms() returns a
    /// capacity-sized backing array (rounded up to a power of two, padded with default Transforms
    /// past the real count) — InstanceCount is the actual number of live entries, hence the
    /// explicit bound below rather than trusting the span's own length; this matters even for a
    /// non-instanced single-transform Renderable in principle, and is essential the moment
    /// anything in this codebase uses real GPU instancing (useInstancing: true) again. Materializes
    /// into a List rather than using yield return because ReadOnlySpan&lt;Transform&gt; can't be
    /// held live across a yield boundary.</summary>
    public IEnumerable<(Bliss.CSharp.Geometry.Meshes.IMesh mesh, Matrix4x4 world)> GetPickableMeshes()
    {
        var results = new List<(Bliss.CSharp.Geometry.Meshes.IMesh, Matrix4x4)>();
        foreach (var renderable in cachedRenderables)
        {
            var transforms = renderable.GetTransforms();
            int count = (int)renderable.InstanceCount;
            for (int i = 0; i < count; i++)
                results.Add((renderable.Mesh, transforms[i].GetMatrix()));
        }
        return results;
    }

    public virtual void DrawBoundingSphere(OutputDescription outputDescription, CommandList commandList, ImmediateRenderer immediateRenderer)
    {
        var sphere = WorldBoundingSphere;
        var center = new Vector3(sphere.X, sphere.Y, sphere.Z);
        immediateRenderer.DrawSphereWires(new Transform { Translation = center }, sphere.W, 8, 8, Color.Cyan);
    }

    public virtual void Dispose() { }
}
