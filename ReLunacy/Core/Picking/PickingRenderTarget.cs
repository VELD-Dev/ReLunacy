using Veldrid;

namespace ReLunacy.Core.Picking;

public class PickingRenderTarget : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;

    private Texture _colorTexture = null!;
    private Texture _depthTexture = null!;
    private Framebuffer _framebuffer = null!;
    private Texture _stagingTexture = null!;

    public uint Width { get; private set; }
    public uint Height { get; private set; }
    public Framebuffer Framebuffer => _framebuffer;
    public OutputDescription OutputDescription => _framebuffer.OutputDescription;

    public PickingRenderTarget(GraphicsDevice gd, uint width, uint height)
    {
        _graphicsDevice = gd;
        CreateResources(width, height);
    }

    private void CreateResources(uint width, uint height)
    {
        Width = Math.Max(1u, width);
        Height = Math.Max(1u, height);

        _colorTexture = _graphicsDevice.ResourceFactory.CreateTexture(
            TextureDescription.Texture2D(
                Width, Height, 1, 1,
                PixelFormat.R8G8B8A8UNorm,
                TextureUsage.RenderTarget | TextureUsage.Sampled));
        _colorTexture.Name = "Picking Color Texture";

        _depthTexture = _graphicsDevice.ResourceFactory.CreateTexture(
            TextureDescription.Texture2D(
                Width, Height, 1, 1,
                PixelFormat.D32FloatS8UInt,
                TextureUsage.DepthStencil));
        _depthTexture.Name = "Picking Depth Texture";

        _framebuffer = _graphicsDevice.ResourceFactory.CreateFramebuffer(
            new FramebufferDescription(_depthTexture, _colorTexture));
        _framebuffer.Name = "Picking Framebuffer";

        _stagingTexture = _graphicsDevice.ResourceFactory.CreateTexture(
            TextureDescription.Texture2D(
                1, 1, 1, 1,
                PixelFormat.R8G8B8A8UNorm,
                TextureUsage.Staging));
        _stagingTexture.Name = "Picking Staging Texture";
    }

    public void Resize(uint width, uint height)
    {
        width = Math.Max(1u, width);
        height = Math.Max(1u, height);

        if (Width == width && Height == height)
            return;

        DisposeResources();
        CreateResources(width, height);
    }

    public uint ReadPixel(CommandList cl, int x, int y)
    {
        x = Math.Clamp(x, 0, (int)Width - 1);
        y = Math.Clamp(y, 0, (int)Height - 1);

        ReLunacy.Utility.LunaLog.LogDebug($"ReadPixel: copying from ({x}, {y}), texture size: {Width}x{Height}");

        cl.CopyTexture(
            _colorTexture,
            (uint)x, (uint)y, 0, 0, 0,
            _stagingTexture,
            0, 0, 0, 0, 0,
            1, 1, 1, 1);

        ReLunacy.Utility.LunaLog.LogDebug("ReadPixel: ending command list");
        cl.End();

        ReLunacy.Utility.LunaLog.LogDebug("ReadPixel: submitting commands");
        _graphicsDevice.SubmitCommands(cl);

        ReLunacy.Utility.LunaLog.LogDebug("ReadPixel: waiting for idle");
        _graphicsDevice.WaitForIdle();

        ReLunacy.Utility.LunaLog.LogDebug("ReadPixel: mapping staging texture");
        MappedResourceView<byte> mapped = _graphicsDevice.Map<byte>(_stagingTexture, MapMode.Read);
        byte r = mapped[0];
        byte g = mapped[1];
        byte b = mapped[2];
        byte a = mapped[3];
        _graphicsDevice.Unmap(_stagingTexture);

        ReLunacy.Utility.LunaLog.LogDebug($"ReadPixel: got RGBA = ({r}, {g}, {b}, {a})");
        return (uint)(r | (g << 8) | (b << 16) | (a << 24));
    }

    private void DisposeResources()
    {
        _framebuffer?.Dispose();
        _colorTexture?.Dispose();
        _depthTexture?.Dispose();
        _stagingTexture?.Dispose();
    }

    public void Dispose()
    {
        DisposeResources();
        GC.SuppressFinalize(this);
    }
}