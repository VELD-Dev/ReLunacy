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

    /// <summary>Meshes to draw for GPU picking, all tagged with this entity's own ID — populated from the last Draw() call. A Moby's bangles/submeshes all resolve back to the one Moby entity.</summary>
    public IEnumerable<Bliss.CSharp.Geometry.Meshes.IMesh> GetPickableMeshes() => cachedRenderables.Select(r => r.Mesh);

    public virtual void DrawBoundingSphere(OutputDescription outputDescription, CommandList commandList, ImmediateRenderer immediateRenderer)
    {
        var sphere = WorldBoundingSphere;
        var center = new Vector3(sphere.X, sphere.Y, sphere.Z);
        immediateRenderer.DrawSphereWires(new Transform { Translation = center }, sphere.W, 8, 8, Color.Cyan);
    }

    public virtual void Dispose() { }
}
