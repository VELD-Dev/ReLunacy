using NeoVeldrid;

namespace ReLunacy.Utility;

/// <summary>The offscreen surface the whole UI is drawn into, plus the single-sampled copy of it that
/// gets presented.
///
/// Two textures because of MSAA: a multisampled image cannot be sampled by a shader, so each frame
/// resolves into <see cref="ResolveTexture"/> and the blit reads that. With MSAA off the resolve
/// becomes a straight copy, and the pair stays because the alternative is two code paths for the sake
/// of one texture.</summary>
public sealed class MainRenderTarget : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly PixelFormat _colorFormat;
    private readonly PixelFormat _depthFormat;

    public uint Width { get; private set; }
    public uint Height { get; private set; }
    public TextureSampleCount SampleCount { get; }

    public Texture ColorTexture { get; private set; } = null!;
    public Texture DepthTexture { get; private set; } = null!;
    public Framebuffer Framebuffer { get; private set; } = null!;

    public Texture ResolveTexture { get; private set; } = null!;
    public TextureView ResolveTextureView { get; private set; } = null!;

    public MainRenderTarget(
        GraphicsDevice graphicsDevice, uint width, uint height, TextureSampleCount sampleCount,
        PixelFormat colorFormat = PixelFormat.R8_G8_B8_A8_UNorm,
        PixelFormat depthFormat = PixelFormat.D32_Float_S8_UInt)
    {
        _graphicsDevice = graphicsDevice;
        _colorFormat = colorFormat;
        _depthFormat = depthFormat;
        SampleCount = sampleCount;
        Create(width, height);
    }

    private void Create(uint width, uint height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        var factory = _graphicsDevice.ResourceFactory;

        ColorTexture = factory.CreateTexture(new TextureDescription(
            Width, Height, 1, 1, 1, _colorFormat,
            TextureUsage.RenderTarget | TextureUsage.Sampled, TextureType.Texture2D, SampleCount));

        DepthTexture = factory.CreateTexture(new TextureDescription(
            Width, Height, 1, 1, 1, _depthFormat,
            TextureUsage.DepthStencil, TextureType.Texture2D, SampleCount));

        Framebuffer = factory.CreateFramebuffer(new FramebufferDescription(DepthTexture, ColorTexture));

        // Never multisampled, whatever the target is: this is the resolve destination and the only one
        // of the two a shader can read.
        ResolveTexture = factory.CreateTexture(TextureDescription.Texture2D(
            Width, Height, 1, 1, _colorFormat, TextureUsage.Sampled));
        ResolveTextureView = factory.CreateTextureView(ResolveTexture);
    }

    public void Resize(uint width, uint height)
    {
        if (width == Width && height == Height) return;
        DestroyResources();
        Create(width, height);
    }

    /// <summary>Collapses this frame's render into <see cref="ResolveTexture"/>, ready to be blitted.</summary>
    public void Resolve(CommandList commandList)
    {
        if (SampleCount != TextureSampleCount.Count1)
            commandList.ResolveTexture(ColorTexture, ResolveTexture);
        else
            commandList.CopyTexture(ColorTexture, ResolveTexture);
    }

    private void DestroyResources()
    {
        ResolveTextureView?.Dispose();
        ResolveTexture?.Dispose();
        Framebuffer?.Dispose();
        DepthTexture?.Dispose();
        ColorTexture?.Dispose();
    }

    public void Dispose() => DestroyResources();
}
