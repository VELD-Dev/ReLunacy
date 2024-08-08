namespace ReLunacy.Engine.Rendering;

public class Renderer
{
    int opaqueFbo;
    int transFbo;
    int opaqueTex;
    int depthTex;
    int accumTex;
    int revealTex;
    float[] cClearBuf = [0, 0, 0, 1];
    float[] dClearBuf = [1, 1, 1, 1];

    Material composite;
    Material screen;

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

        opaqueFbo = GL.GenFramebuffer();
        transFbo = GL.GenFramebuffer();

        opaqueTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, opaqueTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, Window.Singleton.ClientSize.X, Window.Singleton.ClientSize.Y, 0, PixelFormat.Rgba, PixelType.HalfFloat, nint.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.BindTexture(TextureTarget.Texture2D, 0);

        depthTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, depthTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent, Window.Singleton.ClientSize.X, Window.Singleton.ClientSize.Y, 0, PixelFormat.DepthComponent, PixelType.Float, nint.Zero);
        GL.BindTexture(TextureTarget.Texture2D, 0);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, opaqueFbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, opaqueTex, 0);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, depthTex, 0);

        FramebufferErrorCode fbec = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (fbec != FramebufferErrorCode.FramebufferComplete)
        {
            throw new Exception($"opaqueFbo incomplete, error {fbec.ToString()}");
        }

        accumTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, accumTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, Window.Singleton.ClientSize.X, Window.Singleton.ClientSize.Y, 0, PixelFormat.Rgba, PixelType.HalfFloat, nint.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.BindTexture(TextureTarget.Texture2D, 0);

        revealTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, revealTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8, Window.Singleton.ClientSize.X, Window.Singleton.ClientSize.Y, 0, PixelFormat.Red, PixelType.Float, nint.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.BindTexture(TextureTarget.Texture2D, 0);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, transFbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, accumTex, 0);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1, TextureTarget.Texture2D, revealTex, 0);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, depthTex, 0);

        DrawBuffersEnum[] transDrawBuffers = new DrawBuffersEnum[] { DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1 };
        GL.DrawBuffers(2, transDrawBuffers);

        fbec = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (fbec != FramebufferErrorCode.FramebufferComplete)
        {
            throw new Exception($"transFbo incomplete, error {fbec.ToString()}");
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        composite = new Material(MaterialManager.Materials["screenv;compositef"]);
        screen = new Material(MaterialManager.Materials["screenv;screenf"]);

        // Create camera perspective
        Camera.Main = new();
        Camera.Main.SetPerspective(MathHelper.PiOver2, Window.Singleton.ClientSize.X / (float)Window.Singleton.ClientSize.Y, 0.1f, Window.Singleton.Settings.RenderDistance);
    }

    public void UpdatePerspective(float aspect)
    {
        Camera.Main.SetPerspective(MathHelper.PiOver2, aspect, 0.1f, Window.Singleton.Settings.RenderDistance);
    }
}
