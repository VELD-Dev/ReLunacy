using Vector2 = System.Numerics.Vector2;

namespace ReLunacy.Engine.Rendering;

public class Renderer
{
    public nint ColourFramebuffer { get; private set; }
    int opaqueTex;
    int colourTex;
    int depthTex;
    int accumTex;
    int revealTex;
    float[] cClearBuf = [0, 0, 0, 1];
    float[] dClearBuf = [1, 1, 1, 1];

    public static readonly Color4 ClearColour = new(48, 48, 96, 255);

    public Vector2i oldSize;
    private readonly bool initialized = false;

    Material composite;
    Material screen;
    Drawable quad;

    public Renderer()
    {
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.Texture2D);

        MaterialManager.LoadMaterial("stdv;transparentf", "Shaders/stdv.glsl", "Shaders/transparentf.glsl");
        MaterialManager.LoadMaterial("stdv;solidf", "Shaders/stdv.glsl", "Shaders/solidf.glsl");
        MaterialManager.LoadMaterial("stdv;whitef", "Shaders/stdvsingle.glsl", "Shaders/whitef.glsl");
        MaterialManager.LoadMaterial("stdv;volumef", "Shaders/stdv.glsl", "Shaders/volumef.glsl");
        MaterialManager.LoadMaterial("stdv;pickingf", "Shaders/stdv.glsl", "Shaders/pickingf.glsl");
        MaterialManager.LoadMaterial("screenv;compositef", "Shaders/screenv.glsl", "Shaders/compositef.glsl");
        MaterialManager.LoadMaterial("screenv;screenf", "Shaders/screenv.glsl", "Shaders/screenf.glsl");

        initialized = true;

        quad = new();
        quad.SetVertexPositions([
            -1, -1,  0,
            -1,  1,  0,
             1,  1,  0,
             1, -1,  0
        ]);
        quad.SetVertexTexCoords([
            0, 0,
            0, 1,
            1, 1,
            1, 0
        ]);
        quad.SetIndices([
            0, 1, 2,
            2, 3, 0
        ]);

        composite = new Material(MaterialManager.Materials["screenv;compositef"]);
        screen = new Material(MaterialManager.Materials["screenv;screenf"]);

        // Create camera
        Camera.Main = new();

    }

    internal void Resize3DView(Vector2i newSize)
    {
        if (oldSize == newSize) return;

        oldSize = newSize;

        if(initialized)
        {
            GL.DeleteFramebuffer((int)ColourFramebuffer);
            GL.DeleteTexture(colourTex);
            GL.DeleteTexture(depthTex);
        }

        ColourFramebuffer = GL.GenFramebuffer();

        colourTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, colourTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, newSize.X, newSize.Y, 0, PixelFormat.Rgba, PixelType.UnsignedInt8888, nint.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.BindTexture(TextureTarget.Texture2D, 0);

        depthTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, depthTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent, newSize.X, newSize.Y, 0, PixelFormat.DepthComponent, PixelType.Float, nint.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.BindTexture(TextureTarget.Texture2D, 0);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, (int)ColourFramebuffer);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, colourTex, 0);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, depthTex, 0);

        var fboStatus = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (fboStatus != FramebufferErrorCode.FramebufferComplete)
        {
            throw new Exception($"Framebuffer failed to (re)initialize with error {fboStatus}.");
        }

        UpdatePerspective();
    }

    internal void RenderFrame()
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, (int)ColourFramebuffer);
        GL.ClearColor(ClearColour);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GL.DepthMask(true);
        GL.Viewport(0, 0, oldSize.X, oldSize.Y);
        EntityManager.Singleton.Render();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, Window.Singleton.ClientSize.X, Window.Singleton.ClientSize.Y);
        GL.ClearColor(0, 0, 0, 0);
    }

    /*
    internal void RenderFrame()
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, opaqueFbo);
        GL.ClearColor(ClearColour);
        GL.Enable(EnableCap.DepthTest);
        GL.DepthFunc(DepthFunction.Less);
        GL.DepthMask(true);
        GL.Disable(EnableCap.DepthTest);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        EntityManager.Singleton.RenderOpaque();

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, transFbo);
        GL.DepthMask(false);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(0, BlendingFactorSrc.One, BlendingFactorDest.One);
        GL.BlendFunc(1, BlendingFactorSrc.Zero, BlendingFactorDest.OneMinusSrcColor);
        GL.BlendEquation(BlendEquationMode.FuncAdd);
        GL.ClearBuffer(ClearBuffer.Color, 0, cClearBuf);
        GL.ClearBuffer(ClearBuffer.Color, 1, dClearBuf);

        EntityManager.Singleton.RenderTransparent();

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, opaqueFbo);
        GL.DepthFunc(DepthFunction.Always);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            throw new Exception($"Framebuffer incomplete, error {status}");
        }

        quad.SetMaterial(composite);
        quad.material.SimpleUse();
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, accumTex);
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, revealTex);
        quad.material.SetInt("accum", 0);
        quad.material.SetInt("reveal", 1);
        quad.SimpleDraw();

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Disable(EnableCap.DepthTest);
        GL.DepthMask(true);
        GL.Disable(EnableCap.Blend);
        GL.ClearColor(0, 0, 0, 0);
        status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            throw new Exception($"Framebuffer incomplete, error {status}");
        }

        quad.SetMaterial(screen);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, opaqueTex);
        quad.material.SetInt("screen", 0);
        quad.SimpleDraw();
    }

    public void OnResize(Vector2 frameSize)
    {
        GL.Viewport(0, 0, (int)frameSize.X, (int)frameSize.Y);
        UpdatePerspective(frameSize.X / frameSize.Y);

        GL.BindTexture(TextureTarget.Texture2D, opaqueTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, (int)frameSize.X, (int)frameSize.Y, 0, PixelFormat.Rgba, PixelType.HalfFloat, IntPtr.Zero);

        GL.BindTexture(TextureTarget.Texture2D, depthTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent, (int)frameSize.X, (int)frameSize.Y, 0, PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);

        GL.BindTexture(TextureTarget.Texture2D, accumTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, (int)frameSize.X, (int)frameSize.Y, 0, PixelFormat.Rgba, PixelType.HalfFloat, IntPtr.Zero);

        GL.BindTexture(TextureTarget.Texture2D, revealTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8, (int)frameSize.X, (int)frameSize.Y, 0, PixelFormat.Red, PixelType.Float, IntPtr.Zero);

        GL.BindTexture(TextureTarget.Texture2D, 0);
    }
    */

    public void UpdatePerspective()
    {
        Camera.Main.SetPerspective(Program.Settings.CamFOVRad, oldSize.X / (float)oldSize.Y, 0.1f, Program.Settings.RenderDistance);
    }
}
