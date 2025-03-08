using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Engine.Rendering;

public class FramebufferRenderer : IDisposable
{
    public static int MSAA_LEVEL = 2;
    private int internalAllocatedMsaaLevle;

    private bool disposed = false;

    private int targetTexture;
    private int typeTexture;
    public int RenderTexture { get; private set; }
    public int RenderTypeTexture { get; private set; }
    private int framebufferId;
    private int renderbufferId;
    private int renderFramebufferId;

    public Vector2i RenderSize;

    private void AllocateAllResources()
    {
        internalAllocatedMsaaLevle = MSAA_LEVEL;

        targetTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2DMultisample, targetTexture);
        GL.TexImage2DMultisample(TextureTargetMultisample.Texture2DMultisample, MSAA_LEVEL, PixelInternalFormat.Rgb, RenderSize.X, RenderSize.Y, true);

        renderbufferId = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, renderbufferId);
        GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, MSAA_LEVEL, RenderbufferStorage.DepthComponent, RenderSize.X, RenderSize.Y);

        typeTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2DMultisample, typeTexture);
        GL.TexImage2DMultisample(TextureTargetMultisample.Texture2DMultisample, MSAA_LEVEL, PixelInternalFormat.R32i, RenderSize.X, RenderSize.Y, true);

        framebufferId = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebufferId);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2DMultisample, targetTexture, 0);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1, TextureTarget.Texture2DMultisample, typeTexture, 0);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, renderbufferId);

        RenderTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, RenderTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb8, RenderSize.X, RenderSize.Y, 0, PixelFormat.Rgb, PixelType.UnsignedByte, nint.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);

        RenderTypeTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, RenderTypeTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R32i, RenderSize.X, RenderSize.Y, 0, PixelFormat.RedInteger, PixelType.Int, (IntPtr)0);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);

        renderFramebufferId = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, renderFramebufferId);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, RenderTexture, 0);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1, TextureTarget.Texture2D, RenderTypeTexture, 0);
    }

    private void DeleteAllResources()
    {
        GL.DeleteFramebuffer(framebufferId);
        GL.DeleteFramebuffer(renderFramebufferId);
        GL.DeleteRenderbuffer(renderbufferId);
        GL.DeleteTexture(targetTexture);
        GL.DeleteTexture(typeTexture);
        GL.DeleteTexture(RenderTexture);
        GL.DeleteTexture(RenderTypeTexture);
    }

    public FramebufferRenderer(int width, int height)
    {
        RenderSize = new(width, height);

        AllocateAllResources();
    }

    public void RenderToTexture(Action renderFunction)
    {
        if(internalAllocatedMsaaLevle != MSAA_LEVEL)
        {
            DeleteAllResources();
            AllocateAllResources();
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebufferId);
        GL.Viewport(0, 0, RenderSize.X, RenderSize.Y);

        DrawBuffersEnum[] buffers = [DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1];
        GL.DrawBuffers(2, buffers);

        GL.Enable(EnableCap.DepthTest);
        GL.DepthFunc(DepthFunction.Less);

        GL.GenVertexArrays(1, out int vao);
        GL.BindVertexArray(vao);

        renderFunction();

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebufferId);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, renderFramebufferId);
        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
        GL.BlitFramebuffer(0, 0, RenderSize.X, RenderSize.Y, 0, 0, RenderSize.X, RenderSize.Y, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Linear);
        GL.ReadBuffer(ReadBufferMode.ColorAttachment1);
        GL.DrawBuffer(DrawBufferMode.ColorAttachment1);
        GL.BlitFramebuffer(0, 0, RenderSize.X, RenderSize.Y, 0, 0, RenderSize.X, RenderSize.Y, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        GL.DeleteVertexArray(vao);
    }

    public void ExposeFramebuffer(Action func)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, renderFramebufferId);

        func();

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }



    protected virtual void Dispose(bool disposing)
    {
        if (disposed)
        {
            return;
        }

        if (disposing)
        {
            DeleteAllResources();
        }

        disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
    }

    ~FramebufferRenderer()
    {
        Dispose(false);
    }
}
