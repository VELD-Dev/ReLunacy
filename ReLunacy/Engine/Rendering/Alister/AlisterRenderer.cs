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
    private bool initialized = false;
    private Material composite;
    private Material screen;
    public Frustrum Frustrum { get; private set; }
    public int RenderTexture { get; private set; }
    public Vector2i RenderSize { get; private set; }


    public static readonly Color4 ClearColour = new(0x20, 0x20, 0x30, 0xFF);
    public static readonly Color4 BgClearColour = new(0x1B, 0x1B, 0x1B, 0xFF);

    public AlisterRenderer(Camera camera, Toolbox toolbox)
    {
        this.camera = camera;
        this.toolbox = toolbox;

        composite = new Material(MaterialManager.ShaderHandles["screenv;compositef"]);
        screen = new Material(MaterialManager.ShaderHandles["screenv;screenf"]);
    }

    public void SetSkybox(Entity skybox)
    {
        this.skybox = skybox;
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
        GL.ClearColor(ClearColour);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GL.DepthMask(true);
        GL.Viewport(0, 0, RenderSize.X, RenderSize.Y);

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
            //GL.DeleteTexture(depthTex);
        }

        framebuffer = GL.GenFramebuffer();

        RenderTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, RenderTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, newSize.X, newSize.Y, 0, PixelFormat.Rgba, PixelType.UnsignedInt8888, nint.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.BindTexture(TextureTarget.Texture2D, 0);

        /*
        depthTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, (int)depthTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent, newSize.X, newSize.Y, 0, PixelFormat.DepthComponent, PixelType.Float, nint.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        */

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, (int)RenderTexture, 0);
        //GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, (int)depthTex, 0);

        var fboStatus = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (fboStatus != FramebufferErrorCode.FramebufferComplete)
        {
            throw new Exception($"Framebuffer failed to (re)initialize with error {fboStatus}.");
        }

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
