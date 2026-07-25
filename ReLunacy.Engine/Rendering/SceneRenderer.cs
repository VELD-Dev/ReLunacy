using System.Numerics;
using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Geometry;
using Bliss.CSharp.Geometry.Meshes;
using Bliss.CSharp.Graphics.Rendering;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
using Bliss.CSharp.Transformations;
using Veldrith;

namespace ReLunacy.Engine.Rendering;

public sealed class SceneRenderer : IDisposable
{
    private readonly IRenderer _renderer;
    private Frustum? _frustum;

    public int SubmittedCount { get; private set; }
    public int CulledCount { get; private set; }

    public SceneRenderer(GraphicsDevice graphicsDevice)
    {
        _renderer = new DecalAwareForwardRenderer(graphicsDevice);
    }

    public void BeginFrame(Cam3D camera)
    {
        _frustum = camera.GetFrustum();
        SubmittedCount = 0;
        CulledCount = 0;
    }

    public void Submit(IMesh mesh, Transform transform, bool copyMeshMaterial = false)
    {
        if (!IsVisible(mesh.GenBoundingBox(), transform))
        {
            CulledCount++;
            return;
        }

        SubmittedCount++;
        _renderer.DrawRenderable(new Renderable(mesh, transform, copyMeshMaterial));
    }

    private bool IsVisible(BoundingBox localBounds, Transform transform)
    {
        if (_frustum is null)
            return true;

        Matrix4x4 world = transform.GetMatrix();

        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new(
                (i & 1) == 0 ? localBounds.Min.X : localBounds.Max.X,
                (i & 2) == 0 ? localBounds.Min.Y : localBounds.Max.Y,
                (i & 4) == 0 ? localBounds.Min.Z : localBounds.Max.Z);
            Vector3 worldCorner = Vector3.Transform(corner, world);
            min = Vector3.Min(min, worldCorner);
            max = Vector3.Max(max, worldCorner);
        }

        return _frustum.ContainsBox(new BoundingBox(min, max));
    }

    public void Draw(CommandList commandList, OutputDescription output) => _renderer.Draw(commandList, output);

    public void Dispose() => _renderer.Dispose();
}
