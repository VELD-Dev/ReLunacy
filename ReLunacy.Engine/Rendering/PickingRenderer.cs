using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Bliss.CSharp.Geometry.Meshes;
using Veldrith;
using Veldrith.SPIRV;

namespace ReLunacy.Engine.Rendering;

// Off-screen GPU object-ID picking: renders every pickable mesh into a dedicated color+depth
// target with the fragment shader outputting the entity's ID packed as RGBA8, then reads back a
// small window of pixels around the cursor and takes the nearest non-background hit. Replaces
// CPU ray/bounding-sphere picking so overlapping/occluded geometry resolves correctly. Bangles/
// submeshes of a Moby all draw with the same entity ID (the caller passes one ID per entity, not
// per mesh) so only whole entities are selectable in the 3D view.
public sealed class PickingRenderer : IDisposable
{
    private const string VertSource = """
        #version 450

        layout(std140, set = 0, binding = 0) uniform PickingBuffer {
            mat4 uViewProjection;
            mat4 uWorld;
            uint uObjectId;
        };

        layout(location = 0) in vec3 vPosition;
        layout(location = 1) in vec2 vTexCoords;
        layout(location = 2) in vec2 vTexCoords2;
        layout(location = 3) in vec3 vNormal;
        layout(location = 4) in vec4 vTangent;
        layout(location = 5) in vec4 vColor;

        void main() {
            gl_Position = uViewProjection * uWorld * vec4(vPosition, 1.0);
        }
        """;

    private const string FragSource = """
        #version 450

        layout(std140, set = 0, binding = 0) uniform PickingBuffer {
            mat4 uViewProjection;
            mat4 uWorld;
            uint uObjectId;
        };

        layout(location = 0) out vec4 fFragColor;

        void main() {
            fFragColor = vec4(
                float( uObjectId        & 0xFFu) / 255.0,
                float((uObjectId >> 8)  & 0xFFu) / 255.0,
                float((uObjectId >> 16) & 0xFFu) / 255.0,
                float((uObjectId >> 24) & 0xFFu) / 255.0
            );
        }
        """;

    private readonly GraphicsDevice _gd;
    private readonly Shader[] _shaders;
    private readonly ResourceLayout _layout;
    private readonly Pipeline _pipeline;
    private readonly DeviceBuffer _uniformBuffer;
    private readonly ResourceSet _resourceSet;
    private readonly CommandList _commandList;

    // Clicks near a silhouette/thin object can land on a background pixel by a pixel or two —
    // rather than trusting the exact pixel under the cursor, read back a small window around it
    // and pick the nearest non-background hit. Fixed size, independent of viewport resizing.
    private const int PickWindowSize = 4;

    private Texture _colorTexture = null!;
    private Texture _depthTexture = null!;
    private Framebuffer _framebuffer = null!;
    private readonly Texture _stagingTexture;
    private uint _width = 1, _height = 1;

    public PickingRenderer(GraphicsDevice gd)
    {
        _gd = gd;
        var factory = gd.ResourceFactory;

        _shaders = factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(VertSource), "main"),
            new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(FragSource), "main"));

        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("vPosition", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("vTexCoords", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("vTexCoords2", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("vNormal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("vTangent", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
            new VertexElementDescription("vColor", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4));

        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("PickingBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment)));

        // 2 * mat4 (64 bytes each) + 1 uint, rounded up to a 16-byte-aligned uniform buffer size.
        _uniformBuffer = factory.CreateBuffer(new BufferDescription(144, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(_layout, _uniformBuffer));

        _commandList = factory.CreateCommandList();

        _stagingTexture = factory.CreateTexture(TextureDescription.Texture2D(
            PickWindowSize, PickWindowSize, 1, 1, PixelFormat.R8G8B8A8UNorm, TextureUsage.Staging));
        _stagingTexture.Name = "Picking Staging Texture";

        CreateTargets(1, 1);

        // Scissor test enabled: Pick() constrains rasterization to a small window around the
        // cursor instead of the full viewport, since only a few pixels around (x, y) are ever
        // read back.
        var rasterizerState = new RasterizerStateDescription(
            FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise,
            depthClipEnabled: true, scissorTestEnabled: true);

        var pipelineDescription = new GraphicsPipelineDescription(
            BlendStateDescription.SINGLE_DISABLED,
            new DepthStencilStateDescription(true, true, ComparisonKind.LessEqual),
            rasterizerState,
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription([vertexLayout], _shaders),
            [_layout],
            _framebuffer.OutputDescription,
            ResourceBindingModel.Default);

        _pipeline = factory.CreateGraphicsPipeline(ref pipelineDescription);
    }

    private void CreateTargets(uint width, uint height)
    {
        _width = Math.Max(1u, width);
        _height = Math.Max(1u, height);

        var factory = _gd.ResourceFactory;

        _colorTexture = factory.CreateTexture(TextureDescription.Texture2D(
            _width, _height, 1, 1, PixelFormat.R8G8B8A8UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled));
        _colorTexture.Name = "Picking Color Texture";

        _depthTexture = factory.CreateTexture(TextureDescription.Texture2D(
            _width, _height, 1, 1, PixelFormat.D32FloatS8UInt, TextureUsage.DepthStencil));
        _depthTexture.Name = "Picking Depth Texture";

        _framebuffer = factory.CreateFramebuffer(new FramebufferDescription(_depthTexture, _colorTexture));
        _framebuffer.Name = "Picking Framebuffer";
    }

    private void Resize(uint width, uint height)
    {
        width = Math.Max(1u, width);
        height = Math.Max(1u, height);
        if (_width == width && _height == height) return;

        _framebuffer.Dispose();
        _colorTexture.Dispose();
        _depthTexture.Dispose();
        CreateTargets(width, height);
    }

    /// <summary>Sentinel returned by <see cref="Pick"/> when nothing was under the cursor. Entity IDs start at 0 and are used freely, so the background can't be encoded as 0 — it's encoded as all-ones instead.</summary>
    public const uint NoHit = uint.MaxValue;

    /// <summary>Renders every entry into the picking buffer and reads back the nearest non-background ID within <see cref="PickWindowSize"/> pixels of (x, y), or <see cref="NoHit"/>. Entries pass the SAME id for every mesh belonging to one selectable entity (e.g. all of a Moby's bangles/submeshes).</summary>
    public uint Pick(uint viewportWidth, uint viewportHeight, int x, int y, Matrix4x4 viewProjection, IEnumerable<(IMesh mesh, Matrix4x4 world, uint id)> entries)
    {
        Resize(viewportWidth, viewportHeight);

        x = Math.Clamp(x, 0, (int)_width - 1);
        y = Math.Clamp(y, 0, (int)_height - 1);

        // Rasterization is scissored to a small window around the cursor — everything outside it
        // is discarded before shading, so the readback below only ever sees this same window
        // (plus whatever the clear color left behind, i.e. NoHit).
        uint scissorX = (uint)Math.Clamp(x - PickWindowSize / 2, 0, (int)_width - 1);
        uint scissorY = (uint)Math.Clamp(y - PickWindowSize / 2, 0, (int)_height - 1);
        uint scissorRight = (uint)Math.Clamp(x - PickWindowSize / 2 + PickWindowSize, 1, (int)_width);
        uint scissorBottom = (uint)Math.Clamp(y - PickWindowSize / 2 + PickWindowSize, 1, (int)_height);
        uint copyWidth = scissorRight - scissorX;
        uint copyHeight = scissorBottom - scissorY;

        _commandList.Begin();
        _commandList.SetFramebuffer(_framebuffer);
        _commandList.ClearColorTarget(0, new RgbaFloat(1, 1, 1, 1)); // decodes to NoHit
        _commandList.ClearDepthStencil(1f);
        _commandList.SetPipeline(_pipeline);
        _commandList.SetScissorRect(0, scissorX, scissorY, scissorRight - scissorX, scissorBottom - scissorY);

        Span<byte> uniformData = stackalloc byte[144];
        foreach (var (mesh, world, id) in entries)
        {
            // Degenerate/empty submeshes have no IndexBuffer (Bliss skips allocating one for
            // zero-index meshes) — Veldrith's raw SetIndexBuffer doesn't null-check, so drawing
            // one crashes. The main forward renderer never hits this because it goes through
            // Bliss's higher-level draw path instead of calling SetIndexBuffer directly.
            if (mesh.IndexCount == 0) continue;

            MemoryMarshal.Write(uniformData, in viewProjection);
            MemoryMarshal.Write(uniformData[64..], in world);
            BitConverter.TryWriteBytes(uniformData[128..], id);

            _commandList.UpdateBuffer(_uniformBuffer, 0, uniformData.ToArray());
            _commandList.SetGraphicsResourceSet(0, _resourceSet);
            _commandList.SetVertexBuffer(0, mesh.VertexBuffer);
            _commandList.SetIndexBuffer(mesh.IndexBuffer, IndexFormat.UInt32);
            _commandList.DrawIndexed(mesh.IndexCount);
        }

        _commandList.CopyTexture(
            _colorTexture, scissorX, scissorY, 0, 0, 0,
            _stagingTexture, 0, 0, 0, 0, 0,
            copyWidth, copyHeight, 1, 1);

        _commandList.End();
        _gd.SubmitCommands(_commandList);
        _gd.WaitForIdle();

        // Nearest-hit scan: walk the copied window and keep the non-background pixel closest to
        // the actual cursor position, so a click that lands a pixel or two off a thin/silhouette
        // edge still resolves to the object instead of missing it. Indexed via the view's [x, y]
        // indexer (not row*width*4) since a 4-wide R8G8B8A8 staging texture is very likely
        // row-padded by the backend, not tightly packed.
        MappedResourceView<byte> mapped = _gd.Map<byte>(_stagingTexture, MapMode.Read);

        uint bestId = NoHit;
        int bestDistSq = int.MaxValue;
        for (uint wy = 0; wy < copyHeight; wy++)
        {
            for (uint wx = 0; wx < copyWidth; wx++)
            {
                byte r = mapped[wx * 4 + 0, wy];
                byte g = mapped[wx * 4 + 1, wy];
                byte b = mapped[wx * 4 + 2, wy];
                byte a = mapped[wx * 4 + 3, wy];
                uint id = (uint)(r | (g << 8) | (b << 16) | (a << 24));
                if (id == NoHit) continue;

                int dx = (int)(scissorX + wx) - x;
                int dy = (int)(scissorY + wy) - y;
                int distSq = dx * dx + dy * dy;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestId = id;
                }
            }
        }

        _gd.Unmap(_stagingTexture);

        return bestId;
    }

    public void Dispose()
    {
        _framebuffer.Dispose();
        _colorTexture.Dispose();
        _depthTexture.Dispose();
        _stagingTexture.Dispose();
        _pipeline.Dispose();
        _resourceSet.Dispose();
        _uniformBuffer.Dispose();
        _layout.Dispose();
        foreach (var shader in _shaders) shader.Dispose();
        _commandList.Dispose();
        GC.SuppressFinalize(this);
    }
}
