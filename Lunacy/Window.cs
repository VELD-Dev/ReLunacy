using System.ComponentModel;
using System.Numerics;
using Vector2 = OpenTK.Mathematics.Vector2;
using Vector3 = OpenTK.Mathematics.Vector3;
using Quaternion = OpenTK.Mathematics.Quaternion;

namespace Lunacy
{
	public class Window : GameWindow
	{
		FileManager fm;
		internal static AssetLoader? al;		// handles loading assets from files
		Gameplay gp;

		GUI gui;

		public Vector2 freecamLocal;

		Drawable quad;
		Material composite;
		Material screen;

		internal static float framerate;
		int opaqueFbo;
		int transFbo;
		int opaqueTex;
		int depthTex;		
		int accumTex;
		int revealTex;
		float[] cClearBuf = new float[4]{0, 0, 0, 1};
		float[] dClearBuf = new float[4]{1, 1, 1, 1};

		public Window(GameWindowSettings gws, NativeWindowSettings nws, string[] args) : base(gws, nws)
		{
			LoadFolder(args[0]);
			EntityManager.Singleton.loadUfrags = args.Any(x => x == "--load-ufrags");
		}

		public void LoadFolder(string folderPath)
		{
			fm = new FileManager();
			fm.LoadFolder(folderPath);
			al = new AssetLoader(fm);
			var prog = new System.Numerics.Vector2();
			var tot = 0f;
			var stat = "";
			al.LoadAssets(ref prog, ref tot, ref stat);
		}

		protected override void OnLoad()
		{
			base.OnLoad();
			
			GL.Enable(EnableCap.DepthTest);
			GL.Enable(EnableCap.Texture2D);
			//GL.Enable(EnableCap.StencilTest);
			//GL.Enable(EnableCap.CullFace);
			//GL.Enable(EnableCap.Blend);
			//GL.BlendFuncSeparate(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha, BlendingFactorSrc.One, BlendingFactorDest.Zero);

			MaterialManager.LoadMaterial("stdv;transparentf", "shaders/stdv.glsl", "shaders/transparentf.glsl");
			MaterialManager.LoadMaterial("stdv;solidf", "shaders/stdv.glsl", "shaders/solidf.glsl");
			MaterialManager.LoadMaterial("stdv;whitef", "shaders/stdvsingle.glsl", "shaders/whitef.glsl");
			MaterialManager.LoadMaterial("stdv;volumef", "shaders/stdv.glsl", "shaders/volumef.glsl");
			MaterialManager.LoadMaterial("stdv;pickingf", "shaders/stdv.glsl", "shaders/pickingf.glsl");
			MaterialManager.LoadMaterial("screenv;compositef", "shaders/screenv.glsl", "shaders/compositef.glsl");
			MaterialManager.LoadMaterial("screenv;screenf", "shaders/screenv.glsl", "shaders/screenf.glsl");

			opaqueFbo = GL.GenFramebuffer();
			transFbo = GL.GenFramebuffer();

			opaqueTex = GL.GenTexture();
			GL.BindTexture(TextureTarget.Texture2D, opaqueTex);
			GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, ClientSize.X, ClientSize.Y, 0, PixelFormat.Rgba, PixelType.HalfFloat, IntPtr.Zero);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
			GL.BindTexture(TextureTarget.Texture2D, 0);

			depthTex = GL.GenTexture();
			GL.BindTexture(TextureTarget.Texture2D, depthTex);
			GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent, ClientSize.X, ClientSize.Y, 0, PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
			GL.BindTexture(TextureTarget.Texture2D, 0);

			GL.BindFramebuffer(FramebufferTarget.Framebuffer, opaqueFbo);
			GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, opaqueTex, 0);
			GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, depthTex, 0);

			FramebufferErrorCode fbec = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
			if(fbec != FramebufferErrorCode.FramebufferComplete)
			{
				throw new Exception($"opaqueFbo incomplete, error {fbec.ToString()}");
			}

			accumTex = GL.GenTexture();
			GL.BindTexture(TextureTarget.Texture2D, accumTex);
			GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, ClientSize.X, ClientSize.Y, 0, PixelFormat.Rgba, PixelType.HalfFloat, IntPtr.Zero);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
			GL.BindTexture(TextureTarget.Texture2D, 0);

			revealTex = GL.GenTexture();
			GL.BindTexture(TextureTarget.Texture2D, revealTex);
			GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8, ClientSize.X, ClientSize.Y, 0, PixelFormat.Red, PixelType.Float, IntPtr.Zero);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
			GL.BindTexture(TextureTarget.Texture2D, 0);

			GL.BindFramebuffer(FramebufferTarget.Framebuffer, transFbo);
			GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, accumTex, 0);
			GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1, TextureTarget.Texture2D, revealTex, 0);
			GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, depthTex, 0);

			DrawBuffersEnum[] transDrawBuffers = new DrawBuffersEnum[]{ DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1 };
			GL.DrawBuffers(2, transDrawBuffers);

			fbec = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
			if(fbec != FramebufferErrorCode.FramebufferComplete)
			{
				throw new Exception($"transFbo incomplete, error {fbec.ToString()}");
			}

			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

			quad = new Drawable();
			quad.SetVertexPositions(new float[]
			{
				-1, -1,  0,
				-1,  1,  0,
				 1,  1,  0,
				 1, -1,  0,
			});
			quad.SetVertexTexCoords(new float[]
			{
				0, 0,
				0, 1,
				1, 1,
				1, 0
			});
			quad.SetIndices(new uint[]
			{
				0, 1, 2,
				2, 3, 0
			});
			composite = new Material(MaterialManager.materials["screenv;compositef"]);
			screen = new Material(MaterialManager.materials["screenv;screenf"]);

			Camera.CreatePerspective(MathHelper.PiOver2, ClientSize.X / (float)ClientSize.Y);

			gui = new GUI(this);

			AssetManager.Singleton.Initialize(al);

			gp = new Gameplay(al);

			EntityManager.Singleton.LoadGameplay(gp);
		}

		protected override void OnRenderFrame(FrameEventArgs args)
		{
			base.OnRenderFrame(args);

			GL.BindFramebuffer(FramebufferTarget.Framebuffer, opaqueFbo);
			GL.ClearColor(0.1f, 0.1f, 0.1f, 0.0f);
			GL.Enable(EnableCap.DepthTest);
			GL.DepthFunc(DepthFunction.Less);
			GL.DepthMask(true);
			GL.Disable(EnableCap.Blend);
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

			quad.SetMaterial(screen);
			GL.ActiveTexture(TextureUnit.Texture0);
			GL.BindTexture(TextureTarget.Texture2D, opaqueTex);
			quad.material.SetInt("screen", 0);
			quad.SimpleDraw();

			gui.FrameBegin(args.Time);

			gui.ShowRegionsWindow();

			gui.Tick();

			gui.FrameEnd();

			SwapBuffers();
		}

		protected override void OnUpdateFrame(FrameEventArgs args)
        {
            framerate = (float)(1 / args.Time);

            if (KeyboardState.IsKeyDown(Keys.Escape)) Close();

			float moveSpeed = 5;
			float sensitivity = 0.01f;

			if(KeyboardState.IsKeyDown(Keys.LeftShift)) moveSpeed *= 10;

			if(KeyboardState.IsKeyDown(Keys.W)) Camera.transform.position += Camera.transform.Forward * (float)args.Time * moveSpeed;
			if(KeyboardState.IsKeyDown(Keys.A)) Camera.transform.position += Camera.transform.Right * (float)args.Time * moveSpeed;
			if(KeyboardState.IsKeyDown(Keys.S)) Camera.transform.position -= Camera.transform.Forward * (float)args.Time * moveSpeed;
			if(KeyboardState.IsKeyDown(Keys.D)) Camera.transform.position -= Camera.transform.Right * (float)args.Time * moveSpeed;

			CursorGrabbed = MouseState.IsButtonDown(MouseButton.Right);

			if(CursorGrabbed)
			{
				freecamLocal += MouseState.Delta.Yx * sensitivity;

				freecamLocal.X = MathHelper.Clamp(freecamLocal.X, -MathHelper.PiOver2 + 0.0001f, MathHelper.PiOver2 - 0.0001f);

				Camera.transform.SetRotation(Quaternion.FromAxisAngle(Vector3.UnitX, freecamLocal.X) * Quaternion.FromAxisAngle(Vector3.UnitY, freecamLocal.Y));
			}
			else
			{
				CursorGrabbed = false;
				CursorVisible = true;
			}

			Title = $"Lunacy Level Editor | {framerate}";

			base.OnUpdateFrame(args);
		}

		protected override void OnTextInput(TextInputEventArgs e)
		{
			base.OnTextInput(e);
			
			gui.KeyPress(e.Unicode);
		}

		protected override void OnResize(ResizeEventArgs e)
		{
			base.OnResize(e);

			GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
			Camera.CreatePerspective(MathHelper.PiOver2, ClientSize.X / (float)ClientSize.Y);
			gui.Resize();

			GL.BindTexture(TextureTarget.Texture2D, opaqueTex);
			GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, ClientSize.X, ClientSize.Y, 0, PixelFormat.Rgba, PixelType.HalfFloat, IntPtr.Zero);

			GL.BindTexture(TextureTarget.Texture2D, depthTex);
			GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent, ClientSize.X, ClientSize.Y, 0, PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);

			GL.BindTexture(TextureTarget.Texture2D, accumTex);
			GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, ClientSize.X, ClientSize.Y, 0, PixelFormat.Rgba, PixelType.HalfFloat, IntPtr.Zero);

			GL.BindTexture(TextureTarget.Texture2D, revealTex);
			GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8, ClientSize.X, ClientSize.Y, 0, PixelFormat.Red, PixelType.Float, IntPtr.Zero);

			GL.BindTexture(TextureTarget.Texture2D, 0);
		}

		protected override void OnClosing(CancelEventArgs e)
		{
			base.OnClosing(e);
		}
	}
}