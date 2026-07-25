using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Bliss.CSharp.Geometry.Meshes;
using Veldrith;
using Veldrith.SPIRV;

namespace ReLunacy.Engine.Rendering;

// Draws a solid silhouette outline around the selected entity's mesh(es), replacing the old
// bounding-sphere wireframe highlight.
//
// This is a two-pass stencil "mask and inflate" technique, not the more common single-pass
// inflated-backface-hull trick — that one relies on either consistent triangle winding (to cull
// the hull's front faces at the GPU level) or consistent vertex normals (to discard them in the
// fragment shader instead). Both were tried here and both broke: AssetManager builds every
// Moby/Tie material with RasterizerStateDescription.CULL_NONE specifically because winding in
// these source assets isn't trustworthy, and it turns out vertex normals aren't consistently
// outward-facing either (same underlying data quality issue) — the normal-based version showed a
// correct rim on part of a mesh and a solid filled blob over the rest, exactly where normals
// were inconsistent.
//
// This version drops the winding dependency entirely:
//  - Pass 1 ("mask") redraws the real, un-inflated mesh with color writes disabled, stamping a
//    stencil value of 1 everywhere it's actually visible (depth-tested against the already-
//    rendered scene, so occluded parts correctly don't get marked). This is just the mesh's own
//    on-screen footprint — it doesn't care which way any triangle faces, so a selected object's
//    own texture can no longer be painted over by its own outline.
//  - Pass 2 ("outline") redraws the mesh again, inflated in clip space, with the stencil test set
//    to pass only where the buffer is NOT already 1 — i.e. everywhere the inflated hull sticks out
//    past the real mesh's footprint from pass 1. That's the rim.
// The inflation direction in pass 2 is still per-vertex-normal, so it inherits whatever normal
// inconsistency the source mesh has — on a model with unreliable normals this can still show up
// as a thin/patchy rim in places (never as a blob covering the object, since the mask makes that
// specific failure impossible). If that turns out to be visible, the fix is to inflate uniformly
// from the mesh's local bounding-sphere center instead of along normals.
// The stencil buffer is cleared to 0 once per frame by View3D's existing ClearDepthStencil call;
// nothing else in the normal render path writes to stencil, so no extra clear is needed here.
public sealed class SelectionOutlineRenderer : IDisposable
{
    private const string VertSource = """
        #version 450

        layout(std140, set = 0, binding = 0) uniform OutlineBuffer {
            mat4 uViewProjection;
            mat4 uWorld;
            vec4 uColor;
            vec4 uThickness; // x = clip-space inflate amount for this pass (0 for the mask pass)
        };

        layout(location = 0) in vec3 vPosition;
        layout(location = 1) in vec2 vTexCoords;
        layout(location = 2) in vec2 vTexCoords2;
        layout(location = 3) in vec3 vNormal;
        layout(location = 4) in vec4 vTangent;
        layout(location = 5) in vec4 vColor;

        void main() {
            vec4 clipPos = uViewProjection * uWorld * vec4(vPosition, 1.0);

            if (uThickness.x > 0.0) {
                vec4 clipNormal = uViewProjection * uWorld * vec4(vNormal, 0.0);
                if (length(clipNormal.xy) > 0.0001)
                    clipPos.xy += normalize(clipNormal.xy) * uThickness.x * clipPos.w;
            } else {
                // Mask pass (thickness == 0): redraws the same geometry the main opaque pass
                // already wrote depth for, and the mask's LessEqual test needs to reliably win
                // against that existing depth. Two separate draw calls of "the same" vertices
                // aren't guaranteed bit-identical depth after going through separate shader
                // invocations/pipelines, so without a bias the comparison intermittently fails
                // by camera angle — stencil doesn't get stamped, and the outline pass fills the
                // unmasked interior solid. Nudging slightly toward the camera fixes that; it's
                // far smaller than any real occlusion gap, so genuine occlusion still masks out.
                clipPos.z -= 0.0005 * clipPos.w;
            }

            gl_Position = clipPos;
        }
        """;

    private const string FragSource = """
        #version 450

        layout(std140, set = 0, binding = 0) uniform OutlineBuffer {
            mat4 uViewProjection;
            mat4 uWorld;
            vec4 uColor;
            vec4 uThickness;
        };

        layout(location = 0) out vec4 fFragColor;

        void main() {
            fFragColor = uColor;
        }
        """;

    private readonly GraphicsDevice _gd;
    private readonly Shader[] _shaders;
    private readonly VertexLayoutDescription _vertexLayout;
    private readonly ResourceLayout _layout;
    private readonly DeviceBuffer _uniformBuffer;
    private readonly ResourceSet _resourceSet;

    private Pipeline? _maskPipeline;
    private Pipeline? _maskDebugPipeline;
    private Pipeline? _outlinePipeline;
    private OutputDescription _pipelineOutputDescription;

    public SelectionOutlineRenderer(GraphicsDevice gd)
    {
        _gd = gd;
        var factory = gd.ResourceFactory;

        _shaders = factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(VertSource), "main"),
            new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(FragSource), "main"));

        _vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("vPosition", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("vTexCoords", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("vTexCoords2", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("vNormal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("vTangent", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
            new VertexElementDescription("vColor", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4));

        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("OutlineBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment)));

        // 2 * mat4 (64 bytes each) + vec4 + vec4, all 16-byte aligned already.
        _uniformBuffer = factory.CreateBuffer(new BufferDescription(160, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(_layout, _uniformBuffer));
    }

    // Pipelines depend on the target framebuffer's format/sample count (MSAA), which can differ
    // between View3D's and AssetViewer's render textures — build/rebuild lazily instead of
    // assuming one fixed OutputDescription for the renderer's lifetime.
    private void EnsurePipelines(OutputDescription outputDescription)
    {
        if (_maskPipeline != null && _pipelineOutputDescription.Equals(outputDescription)) return;

        _maskPipeline?.Dispose();
        _maskDebugPipeline?.Dispose();
        _outlinePipeline?.Dispose();
        _pipelineOutputDescription = outputDescription;

        // No GPU face culling in either pass: source mesh winding isn't trustworthy for these
        // assets (see the class comment above), same reason the main renderer uses CULL_NONE.
        var rasterizerState = new RasterizerStateDescription(
            FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise,
            depthClipEnabled: true, scissorTestEnabled: false);

        var stampStencil = new StencilBehaviorDescription(StencilOperation.Keep, StencilOperation.Replace, StencilOperation.Keep, ComparisonKind.Always);
        var maskDepthStencil = new DepthStencilStateDescription
        {
            DepthTestEnabled = true,
            DepthWriteEnabled = false,
            DepthComparison = ComparisonKind.LessEqual,
            StencilTestEnabled = true,
            StencilFront = stampStencil,
            StencilBack = stampStencil,
            StencilReadMask = 0xFF,
            StencilWriteMask = 0xFF,
            StencilReference = 1,
        };

        var maskBlend = new BlendStateDescription(RgbaFloat.WHITE, new BlendAttachmentDescription
        {
            BlendEnabled = false,
            ColorWriteMask = ColorWriteMask.None,
            SourceColorFactor = BlendFactor.One,
            DestinationColorFactor = BlendFactor.Zero,
            ColorFunction = BlendFunction.Add,
            SourceAlphaFactor = BlendFactor.One,
            DestinationAlphaFactor = BlendFactor.Zero,
            AlphaFunction = BlendFunction.Add,
        });

        var maskPipelineDescription = new GraphicsPipelineDescription(
            maskBlend, maskDepthStencil, rasterizerState, PrimitiveTopology.TriangleList,
            new ShaderSetDescription([_vertexLayout], _shaders), [_layout], outputDescription, ResourceBindingModel.Default);
        _maskPipeline = _gd.ResourceFactory.CreateGraphicsPipeline(ref maskPipelineDescription);

        // Same as the mask pipeline but with normal color writes — diagnostic only, lets
        // DrawOutline's debugVisualizeMask flag show exactly what pass 1 actually covers,
        // instead of guessing whether a bad result is a masking failure or a stencil-exclusion
        // failure.
        var maskDebugPipelineDescription = new GraphicsPipelineDescription(
            BlendStateDescription.SINGLE_DISABLED, maskDepthStencil, rasterizerState, PrimitiveTopology.TriangleList,
            new ShaderSetDescription([_vertexLayout], _shaders), [_layout], outputDescription, ResourceBindingModel.Default);
        _maskDebugPipeline = _gd.ResourceFactory.CreateGraphicsPipeline(ref maskDebugPipelineDescription);

        var rimStencil = new StencilBehaviorDescription(StencilOperation.Keep, StencilOperation.Keep, StencilOperation.Keep, ComparisonKind.NotEqual);
        var outlineDepthStencil = new DepthStencilStateDescription
        {
            DepthTestEnabled = true,
            DepthWriteEnabled = false,
            DepthComparison = ComparisonKind.LessEqual,
            StencilTestEnabled = true,
            StencilFront = rimStencil,
            StencilBack = rimStencil,
            StencilReadMask = 0xFF,
            StencilWriteMask = 0x00,
            StencilReference = 1,
        };

        var outlinePipelineDescription = new GraphicsPipelineDescription(
            BlendStateDescription.SINGLE_DISABLED, outlineDepthStencil, rasterizerState, PrimitiveTopology.TriangleList,
            new ShaderSetDescription([_vertexLayout], _shaders), [_layout], outputDescription, ResourceBindingModel.Default);
        _outlinePipeline = _gd.ResourceFactory.CreateGraphicsPipeline(ref outlinePipelineDescription);
    }

    /// <summary>Draws a solid-color outline around the given meshes. Call after the main opaque pass has written depth, on the same command list/framebuffer.</summary>
    /// <param name="debugVisualizeMask">Diagnostic override: skips the outline pass and draws pass 1's mask directly in solid color, so it's visible whether the mask itself covers the object correctly instead of guessing from the (potentially broken) final composite.</param>
    public void DrawOutline(CommandList commandList, OutputDescription outputDescription, Matrix4x4 viewProjection, IEnumerable<(IMesh mesh, Matrix4x4 world)> entries, Vector4 color, float thickness = 0.006f, bool debugVisualizeMask = false)
    {
        EnsurePipelines(outputDescription);

        var meshes = entries.Where(e => e.mesh.IndexCount > 0).ToList();
        if (meshes.Count == 0) return;

        Span<byte> uniformData = stackalloc byte[160];
        MemoryMarshal.Write(uniformData[128..], in color);

        commandList.SetPipeline(debugVisualizeMask ? _maskDebugPipeline : _maskPipeline);
        MemoryMarshal.Write(uniformData[144..], new Vector4(0f, 0f, 0f, 0f));
        DrawMeshes(commandList, meshes, viewProjection, uniformData);

        if (debugVisualizeMask) return;

        commandList.SetPipeline(_outlinePipeline);
        MemoryMarshal.Write(uniformData[144..], new Vector4(thickness, 0f, 0f, 0f));
        DrawMeshes(commandList, meshes, viewProjection, uniformData);
    }

    private void DrawMeshes(CommandList commandList, List<(IMesh mesh, Matrix4x4 world)> meshes, Matrix4x4 viewProjection, Span<byte> uniformData)
    {
        MemoryMarshal.Write(uniformData, in viewProjection);
        foreach (var (mesh, world) in meshes)
        {
            MemoryMarshal.Write(uniformData[64..], in world);

            commandList.UpdateBuffer(_uniformBuffer, 0, uniformData.ToArray());
            commandList.SetGraphicsResourceSet(0, _resourceSet);
            commandList.SetVertexBuffer(0, mesh.VertexBuffer);
            commandList.SetIndexBuffer(mesh.IndexBuffer, IndexFormat.UInt32);
            commandList.DrawIndexed(mesh.IndexCount);
        }
    }

    public void Dispose()
    {
        _maskPipeline?.Dispose();
        _maskDebugPipeline?.Dispose();
        _outlinePipeline?.Dispose();
        _resourceSet.Dispose();
        _uniformBuffer.Dispose();
        _layout.Dispose();
        foreach (var shader in _shaders) shader.Dispose();
        GC.SuppressFinalize(this);
    }
}
