using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace ReLunacy.Engine.Rendering.Alister;

public class AlisterRenderer : IDisposable
{
    private Entity skybox;
    private readonly List<Entity> opaqueEntities = [];
    private readonly List<Entity> transparentEntities = [];
    private readonly Camera camera;
    private readonly Toolbox toolbox;
    private int framebuffer;
    private int depthBuffer;
    private bool initialized = false;
    public Frustrum Frustrum { get; private set; }
    public int RenderTexture { get; private set; }
    public Vector2i RenderSize { get; private set; }
    private Drawable? testDrawable = null;
    private readonly Transform testDrawableTransform = new(new(1, 1, 1), Quat.Identity, new(1, 1, 1));


    public static readonly Color4 ClearColour = new(0x02, 0x02, 0x02, 0xFF);
    public static readonly Color4 BgClearColour = new(0x1B, 0x1B, 0x1B, 0xFF);

    public AlisterRenderer(Camera camera, Toolbox toolbox)
    {
        this.camera = camera;
        this.toolbox = toolbox;
    }

    public void SetSkybox(Entity skybox)
    {
        this.skybox = skybox;
    }

    public void SetTestDrawable(Drawable drawable)
    {
         testDrawable = drawable;
    }

    public void Include(Entity entity)
    {
        if(entity.Model.Material is not null)
        {
            if (entity.Model?.Material.HasTransparency ?? false)
            {
                transparentEntities.Add(entity);
                LunaLog.LogDebug($"Added entity {entity.name} to render pass.");
            }
            else
            {
                opaqueEntities.Add(entity);
                LunaLog.LogDebug($"Added entity {entity.name} to render pass.");
            }
        }
        else if(entity.Model.Children.Count > 0)
        {
            foreach (var child in entity.Model.Children)
            {
                if (child.Material is not null)
                {
                    if (child.Material?.HasTransparency ?? false)
                    {
                        transparentEntities.Add(entity);
                        LunaLog.LogDebug($"Added entity {entity.name} to render pass.");
                    }
                    else
                    {
                        opaqueEntities.Add(entity);
                        LunaLog.LogDebug($"Added entity {entity.name} to render pass.");
                    }
                }
                else
                {
                    foreach (var subChild in child.Children)
                    {
                        if (subChild.Material is null)
                            continue;  // No support for further shit nawh

                        if (subChild.Material.HasTransparency)
                        {
                            transparentEntities.Add(entity);
                            LunaLog.LogDebug($"Added entity {entity.name} to render pass.");
                        }
                        else
                        {
                            opaqueEntities.Add(entity);
                            LunaLog.LogDebug($"Added entity {entity.name} to render pass.");
                        }
                    }
                }
            }
        }
        else
        {
            LunaLog.LogDebug($"Entity {entity.name} skipped.");
            // Billboard entities.
            return;
        }
    }

    public void Render()
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        GLUtil.CheckGlError("BindFramebuffer");
        GL.ClearColor(ClearColour);
        GLUtil.CheckGlError("ClearColour");
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GLUtil.CheckGlError("Clear");
        GL.DepthMask(true);
        GLUtil.CheckGlError("DepthMask");
        GL.Viewport(0, 0, RenderSize.X, RenderSize.Y);
        GLUtil.CheckGlError("DepthMask");
        GL.Enable(EnableCap.DepthTest);
        GLUtil.CheckGlError("Enable");

        testDrawable?.Draw(testDrawableTransform);

        // Skybox render pass
        skybox?.Draw();

        // Opaque render pass
        foreach (var entity in opaqueEntities)
        {
            if(entity.boundingSphere.W > 0 && Program.Settings.FrustrumCulling)
            {
                if (Frustrum.IsInside(entity.boundingSphere.XYZ, entity.boundingSphere.W))
                {
                    entity.Draw();
                }
            }
            else
            {
                if(Frustrum.IsInside(entity.Transform.Position) && Program.Settings.FrustrumCulling)
                {
                    entity.Draw();
                }
                else
                {
                    entity.Draw();
                }
            }
        }

        /*
        transparentEntities.Sort((a, b) =>
            (camera.transform.Position - a.Transform.Position).LengthSquared.CompareTo(
            (camera.transform.Position - b.Transform.Position).LengthSquared)
        );
        */
        // Use atomic loop for transparency instead


        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        // Transparent render pass
        foreach (var entity in transparentEntities)
        {
            if (entity.boundingSphere.W > 0 && Program.Settings.FrustrumCulling)
            {
                if (Frustrum.IsInside(entity.boundingSphere.XYZ, entity.boundingSphere.W))
                {
                    entity.Draw();
                }
            }
            else
            {
                if (Frustrum.IsInside(entity.Transform.Position) && Program.Settings.FrustrumCulling)
                {
                    entity.Draw();
                }
                else
                {
                    entity.Draw();
                }
            }
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, Window.Singleton.ClientSize.X, Window.Singleton.ClientSize.Y);
        GL.ClearColor(BgClearColour);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }

    internal void Resize3DView(Vector2i newSize)
    {
        if (RenderSize == newSize) return;

        RenderSize = newSize;

        if (initialized)
        {
            GL.DeleteFramebuffer(framebuffer);
            GL.DeleteTexture(RenderTexture);
            GL.DeleteRenderbuffer(depthBuffer);
        }

        framebuffer = GL.GenFramebuffer();

        RenderTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, RenderTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, newSize.X, newSize.Y, 0, PixelFormat.Rgba, PixelType.UnsignedInt8888, nint.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.BindTexture(TextureTarget.Texture2D, 0);

        depthBuffer = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthBuffer);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent, newSize.X, newSize.Y);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, RenderTexture, 0);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depthBuffer);

        var fboStatus = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (fboStatus != FramebufferErrorCode.FramebufferComplete)
        {
            throw new Exception($"Framebuffer failed to (re)initialize with error {fboStatus}.");
        }

        initialized = true;
        UpdatePerspective();
    }

    public void UpdatePerspective()
    {
        camera.SetPerspective(Program.Settings.CamFOVRad, RenderSize.X / (float)RenderSize.Y, 0.01f, Program.Settings.RenderDistance);
        Frustrum = new Frustrum(camera.ViewToClip);
    }

    public void Dispose()
    {
        GL.DeleteFramebuffer(framebuffer);
        GL.DeleteTexture(RenderTexture);

        GC.SuppressFinalize(this);
    }
}
