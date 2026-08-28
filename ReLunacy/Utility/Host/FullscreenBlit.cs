using System.Text;
using Veldrith;
using Veldrith.SPIRV;

namespace ReLunacy.Utility;

/// <summary>Copies one texture over the whole of the current framebuffer.
///
/// The last step of every frame: the UI is composited offscreen (see <see cref="MainRenderTarget"/>)
/// and this puts the result on the swapchain.</summary>
public sealed class FullscreenBlit : IDisposable
{
    // A single oversized triangle rather than two triangles for a quad: it covers the framebuffer with
    // no seam down the diagonal, and needs no vertex buffer at all because the three corners are
    // derived from the vertex index.
    private const string VertexShader = """
        #version 450

        layout(location = 0) out vec2 fsUV;

        void main()
        {
            vec2 corner = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
            gl_Position = vec4(corner * 2.0 - 1.0, 0.0, 1.0);
            // Clip y = +1 is the top row of the framebuffer here (the device is configured for the
            // standard clip-space Y direction), while texture v = 0 is the top row of the source, so
            // the vertical axis has to be flipped or the whole screen presents upside down.
            fsUV = vec2(corner.x, 1.0 - corner.y);
        }
        """;

    private const string FragmentShader = """
        #version 450

        layout(location = 0) in vec2 fsUV;
        layout(location = 0) out vec4 outColor;

        layout(set = 0, binding = 0) uniform texture2D SourceTexture;
        layout(set = 0, binding = 1) uniform sampler SourceSampler;

        void main()
        {
            outColor = texture(sampler2D(SourceTexture, SourceSampler), fsUV);
        }
        """;

    private readonly GraphicsDevice _graphicsDevice;
    private readonly Shader[] _shaders;
    private readonly ResourceLayout _layout;
    private readonly Dictionary<TextureView, ResourceSet> _resourceSets = [];
    private readonly Dictionary<OutputDescription, Pipeline> _pipelines = [];

    public FullscreenBlit(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;
        var factory = graphicsDevice.ResourceFactory;

        _shaders = factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(VertexShader), "main"),
            new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(FragmentShader), "main"),
            new CrossCompileOptions());

        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("SourceTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SourceSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
    }

    /// <summary>Cached per output format. The swapchain's format is fixed in practice, so this holds one
    /// pipeline, but the key keeps a second target (a different sample count or colour format) from
    /// silently reusing an incompatible one.</summary>
    private Pipeline GetPipeline(OutputDescription output)
    {
        if (_pipelines.TryGetValue(output, out var cached)) return cached;

        var description = new GraphicsPipelineDescription(
            BlendStateDescription.SINGLE_OVERRIDE_BLEND,
            DepthStencilStateDescription.DISABLED,
            RasterizerStateDescription.CULL_NONE,
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription([], _shaders),
            [_layout],
            output);

        var pipeline = _graphicsDevice.ResourceFactory.CreateGraphicsPipeline(ref description);
        _pipelines[output] = pipeline;
        return pipeline;
    }

    private ResourceSet GetResourceSet(TextureView source)
    {
        if (_resourceSets.TryGetValue(source, out var cached)) return cached;

        var set = _graphicsDevice.ResourceFactory.CreateResourceSet(
            new ResourceSetDescription(_layout, source, _graphicsDevice.PointSampler));
        _resourceSets[source] = set;
        return set;
    }

    /// <summary>Draws <paramref name="source"/> across the framebuffer already set on
    /// <paramref name="commandList"/>.</summary>
    public void Draw(CommandList commandList, TextureView source, OutputDescription output)
    {
        commandList.SetPipeline(GetPipeline(output));
        commandList.SetGraphicsResourceSet(0, GetResourceSet(source));
        commandList.Draw(3);
    }

    /// <summary>Drops the cached resource set for a texture view about to be destroyed. A resize
    /// recreates the render target's views, and a set still pointing at a freed one is a use-after-free
    /// the next time that frame draws.</summary>
    public void Invalidate(TextureView source)
    {
        if (!_resourceSets.Remove(source, out var set)) return;
        set.Dispose();
    }

    public void Dispose()
    {
        foreach (var set in _resourceSets.Values) set.Dispose();
        _resourceSets.Clear();
        foreach (var pipeline in _pipelines.Values) pipeline.Dispose();
        _pipelines.Clear();
        _layout.Dispose();
        foreach (var shader in _shaders) shader.Dispose();
    }
}
