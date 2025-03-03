using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace ReLunacy.Engine.Rendering.Alister;

public class AlisterRenderer : IDisposable
{
    private readonly List<Entity> opaqueEntities = [];
    private readonly List<Entity> transparentEntities = [];
    private readonly Camera camera;
    private readonly Toolbox toolbox;
    private int framebuffer;
    public int RenderTexture { get; private set; }
    public Vector2i RenderSize { get; private set; }

    public AlisterRenderer(Camera camera, Toolbox toolbox)
    {
        this.camera = camera;
        this.toolbox = toolbox;
    }

    public void Include(Entity entity)
    {
        if (entity.Model?.material.HasTransparency ?? false)
        {
            transparentEntities.Add(entity);
        }
        else
        {
            opaqueEntities.Add(entity);
        }
    }

    public void Render()
    {
        framebuffer = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);

        RenderTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, RenderTexture);

        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, RenderSize.X, RenderSize.Y, 0, PixelFormat.Rgba, PixelType.UnsignedByte, nint.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, RenderTexture, 0);

        GL.Viewport(0, 0, RenderSize.X, RenderSize.Y);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        var frustrum = new Frustrum(camera.ViewToClip);

        // Opaque render pass
        foreach(var entity in opaqueEntities)
        {
            if(entity.boundingSphere.W > 0)
            {
                if (frustrum.IsInside(entity.boundingSphere.XYZ, entity.boundingSphere.W))
                {
                    entity.Draw();
                }
            }
            else
            {
                if(frustrum.IsInside(entity.Transform.Position))
                {
                    entity.Draw();
                }
            }
        }

        transparentEntities.Sort((a, b) =>
            (camera.transform.Position - a.Transform.Position).LengthSquared.CompareTo(
            (camera.transform.Position - b.Transform.Position).LengthSquared)
        );

        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        // Transparent render pass
        foreach (var entity in transparentEntities)
        {
            if (entity.boundingSphere.W > 0)
            {
                if (frustrum.IsInside(entity.boundingSphere.XYZ, entity.boundingSphere.W))
                {
                    entity.Draw();
                }
            }
            else
            {
                if (frustrum.IsInside(entity.Transform.Position))
                {
                    entity.Draw();
                }
            }
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Dispose()
    {
        GL.DeleteFramebuffer(framebuffer);
        GL.DeleteTexture(RenderTexture);
    }
}
