using System.Numerics;
using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Graphics;
using Bliss.CSharp.Graphics.Pipelines;
using Bliss.CSharp.Graphics.Pipelines.Buffers;
using Bliss.CSharp.Graphics.Pipelines.Textures;
using Bliss.CSharp.Graphics.Rendering;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
using Bliss.CSharp.Materials;
using Veldrith;

namespace ReLunacy.Engine.Rendering;

/// <summary>
/// Drop-in replacement for Bliss's BasicForwardRenderer that disables depth *writes* (but keeps
/// depth testing) for translucent renderables. BasicForwardRenderer hardcodes
/// DepthStencilStateDescription.DEPTH_ONLY_LESS_EQUAL (depth write ON) for every renderable
/// regardless of RenderMode, and never exposes a way to change that per-material/per-draw.
/// That's fine for opaque geometry, but any AlphaBlend "decal" mesh that sits flush against
/// (or very close to) the opaque surface it's meant to fade into — e.g. UFrag vine/moss decals
/// painted directly onto terrain chunks — z-fights against that surface once it writes its own
/// depth, since both surfaces occupy nearly the same depth value. The result: instead of a soft
/// alpha fade, large parts of the decal randomly fail the depth test and are never drawn at all,
/// which reads as a hard rectangular clip rather than a gradient. Verified against Bliss 1.6.15's
/// real source (BasicForwardRenderer.cs on GitHub, matches the shipped DLL) — this isn't
/// configurable there, and BasicForwardRenderer's methods aren't virtual, so it can't be patched
/// via inheritance; hence this parallel implementation via the small IRenderer interface.
/// </summary>
public class DecalAwareForwardRenderer : IRenderer
{
    private readonly List<Renderable> _opaqueRenderables = [];
    private readonly List<Renderable> _translucentRenderables = [];
    private SimplePipelineDescription _pipelineDescription;

    public GraphicsDevice GraphicsDevice { get; }

    public DecalAwareForwardRenderer(GraphicsDevice graphicsDevice)
    {
        GraphicsDevice = graphicsDevice;
        _pipelineDescription = new SimplePipelineDescription
        {
            PrimitiveTopology = PrimitiveTopology.TriangleList
        };
    }

    public void DrawRenderable(Renderable renderable)
    {
        if (renderable.Material.RenderMode == RenderMode.Translucent)
            _translucentRenderables.Add(renderable);
        else
            _opaqueRenderables.Add(renderable);
    }

    public void Draw(CommandList commandList, OutputDescription output)
    {
        var cam3D = Cam3D.ActiveCamera;
        if (cam3D == null)
            return;

        _opaqueRenderables.Sort((a, b) => Vector3.DistanceSquared(a.GetTransforms()[0].Translation, cam3D.Position).CompareTo(Vector3.DistanceSquared(b.GetTransforms()[0].Translation, cam3D.Position)));
        _translucentRenderables.Sort((a, b) => Vector3.DistanceSquared(b.GetTransforms()[0].Translation, cam3D.Position).CompareTo(Vector3.DistanceSquared(a.GetTransforms()[0].Translation, cam3D.Position)));

        _pipelineDescription.Outputs = output;

        foreach (var renderable in _opaqueRenderables)
            UpdateRenderableBuffer(commandList, renderable);
        foreach (var renderable in _translucentRenderables)
            UpdateRenderableBuffer(commandList, renderable);

        _pipelineDescription.DepthStencilState = DepthStencilStateDescription.DEPTH_ONLY_LESS_EQUAL;
        foreach (var renderable in _opaqueRenderables)
            DrawPreparedRenderable(commandList, cam3D, renderable);

        // No depth WRITE for translucent/decal geometry — depth TEST still applies (so decals
        // still occlude correctly behind opaque geometry in front of them), it just stops
        // polluting the depth buffer against the near-coplanar surface it's blending onto.
        _pipelineDescription.DepthStencilState = DepthStencilStateDescription.DEPTH_ONLY_LESS_EQUAL_READ;
        foreach (var renderable in _translucentRenderables)
            DrawPreparedRenderable(commandList, cam3D, renderable);

        _opaqueRenderables.Clear();
        _translucentRenderables.Clear();
    }

    private void DrawPreparedRenderable(CommandList commandList, Cam3D camera, Renderable renderable)
    {
        _pipelineDescription.BlendState = renderable.Material.BlendState;
        _pipelineDescription.RasterizerState = renderable.Material.RasterizerState;
        _pipelineDescription.BufferLayouts = renderable.Material.Effect.GetBufferLayouts();
        _pipelineDescription.TextureLayouts = renderable.Material.Effect.GetTextureLayouts();
        _pipelineDescription.ShaderSet = new ShaderSetDescription(renderable.Mesh.VertexFormat.Layouts, renderable.Mesh.Material.Effect.Shaders);

        commandList.SetPipeline(renderable.Material.Effect.GetPipeline(_pipelineDescription).Pipeline);

        commandList.SetGraphicsResourceSet(renderable.Material.Effect.GetBufferLayoutSlot("MatrixBuffer"), camera.GetMatrixBuffer().GetResourceSet(renderable.Material.Effect.GetBufferLayout("MatrixBuffer")));
        commandList.SetGraphicsResourceSet(renderable.Material.Effect.GetBufferLayoutSlot("TransformBuffer"), renderable.GetTransformBuffer().GetResourceSet(renderable.Material.Effect.GetBufferLayout("TransformBuffer")));

        if (renderable.HasBones)
        {
            var boneBuffer = renderable.GetBoneBuffer();
            if (boneBuffer != null)
                commandList.SetGraphicsResourceSet(renderable.Material.Effect.GetBufferLayoutSlot("BoneBuffer"), boneBuffer.GetResourceSet(renderable.Material.Effect.GetBufferLayout("BoneBuffer")));
        }

        commandList.SetGraphicsResourceSet(renderable.Material.Effect.GetBufferLayoutSlot("MaterialBuffer"), renderable.GetMaterialBuffer().GetResourceSet(renderable.Material.Effect.GetBufferLayout("MaterialBuffer")));

        foreach (SimpleTextureLayout textureLayout in renderable.Material.Effect.GetTextureLayouts())
        {
            foreach (var materialMapKey in renderable.Material.GetMaterialMapKeys())
            {
                if (textureLayout.Name != materialMapKey.Name)
                    continue;

                string name = textureLayout.Name;
                var materialMap = renderable.Material.GetMaterialMap(materialMapKey);
                var textureResourceSet = materialMap!.GetTextureResourceSet(materialMap.Sampler ?? GraphicsHelper.GetSampler(GraphicsDevice, SamplerType.PointWrap), renderable.Material.Effect.GetTextureLayout(name));
                if (textureResourceSet != null)
                    commandList.SetGraphicsResourceSet(renderable.Material.Effect.GetTextureLayoutSlot(name), textureResourceSet);
            }
        }

        renderable.Material.Effect.Apply(commandList, renderable.Material);

        if (renderable.Mesh.IndexCount != 0)
        {
            commandList.SetVertexBuffer(0, renderable.Mesh.VertexBuffer);
            commandList.SetIndexBuffer(renderable.Mesh.IndexBuffer, IndexFormat.UInt32);

            if (renderable.UseInstancing)
            {
                commandList.SetVertexBuffer(1, renderable.GetInstanceVertexBuffer());
                commandList.DrawIndexed(renderable.Mesh.IndexCount, renderable.InstanceCount, 0, 0, 0);
            }
            else
            {
                commandList.DrawIndexed(renderable.Mesh.IndexCount);
            }
        }
        else
        {
            commandList.SetVertexBuffer(0, renderable.Mesh.VertexBuffer);

            if (renderable.UseInstancing)
            {
                commandList.SetVertexBuffer(1, renderable.GetInstanceVertexBuffer());
                commandList.Draw(renderable.Mesh.VertexCount, renderable.InstanceCount, 0, 0);
            }
            else
            {
                commandList.Draw(renderable.Mesh.VertexCount);
            }
        }
    }

    private static void UpdateRenderableBuffer(CommandList commandList, Renderable renderable)
    {
        if (renderable.IsTransformBufferDirty)
            renderable.UpdateTransformBuffer(commandList);
        if (renderable.IsInstanceVertexBufferDirty)
            renderable.UpdateInstanceVertexBuffer(commandList);
        if (renderable.IsBoneBufferDirty)
            renderable.UpdateBoneBuffer(commandList);
        if (renderable.IsMaterialBufferDirty)
            renderable.UpdateMaterialBuffer(commandList);
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
