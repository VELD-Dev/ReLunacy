using NeoVeldrid;

namespace ReLunacy.Utility;

/// <summary>The offscreen surface the whole UI is drawn into, plus the single-sampled copy of it
/// that gets presented. Two textures because a multisampled image can't be sampled by a shader;
/// each frame resolves into <see cref="ResolveTexture"/>, which the blit reads.</summary>
public sealed class MainRenderTarget : IDisposable
{
    private const PixelFormat ColorFormat = PixelFormat.R8_G8_B8_A8_UNorm;
    private const PixelFormat DepthFormat = PixelFormat.D32_Float_S8_UInt;
    private const TextureUsage ColorUsage = TextureUsage.RenderTarget | TextureUsage.Sampled;

    public uint Width { get; }
    public uint Height { get; }
    public TextureSampleCount SampleCount { get; }

    public Texture ColorTexture { get; private set; } = null!;
    public Texture DepthTexture { get; private set; } = null!;
    public Framebuffer Framebuffer { get; private set; } = null!;

    public Texture ResolveTexture { get; private set; } = null!;
    public TextureView ResolveTextureView { get; private set; } = null!;

    public MainRenderTarget(
        GraphicsDevice graphicsDevice, uint width, uint height, TextureSampleCount sampleCount,
        PixelFormat colorFormat = ColorFormat, PixelFormat depthFormat = DepthFormat)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        SampleCount = sampleCount;
        var factory = graphicsDevice.ResourceFactory;

        try
        {
            ColorTexture = factory.CreateTexture(new TextureDescription(
                Width, Height, 1, 1, 1, colorFormat, ColorUsage, TextureType.Texture2D, SampleCount));

            DepthTexture = factory.CreateTexture(new TextureDescription(
                Width, Height, 1, 1, 1, depthFormat, TextureUsage.DepthStencil, TextureType.Texture2D, SampleCount));

            Framebuffer = factory.CreateFramebuffer(new FramebufferDescription(DepthTexture, ColorTexture));

            ResolveTexture = factory.CreateTexture(TextureDescription.Texture2D(
                Width, Height, 1, 1, colorFormat, TextureUsage.Sampled));
            ResolveTextureView = factory.CreateTextureView(ResolveTexture);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal static TextureSampleCount[] GetSupportedSampleCounts(GraphicsDevice graphicsDevice)
    {
        if (!graphicsDevice.GetPixelFormatSupport(ColorFormat, TextureType.Texture2D, ColorUsage, out var color) ||
            !graphicsDevice.GetPixelFormatSupport(DepthFormat, TextureType.Texture2D, TextureUsage.DepthStencil, out var depth))
            return [TextureSampleCount.Count1];

        return Enum.GetValues<TextureSampleCount>()
            .Where(count => color.IsSampleCountSupported(count) && depth.IsSampleCountSupported(count))
            .ToArray();
    }

    /// <summary>Collapses this frame's render into <see cref="ResolveTexture"/>, ready to be blitted.</summary>
    public void Resolve(CommandList commandList)
    {
        if (SampleCount != TextureSampleCount.Count1)
            commandList.ResolveTexture(ColorTexture, ResolveTexture);
        else
            commandList.CopyTexture(ColorTexture, ResolveTexture);
    }

    public void Dispose()
    {
        ResolveTextureView?.Dispose();
        ResolveTexture?.Dispose();
        Framebuffer?.Dispose();
        DepthTexture?.Dispose();
        ColorTexture?.Dispose();
    }
}
