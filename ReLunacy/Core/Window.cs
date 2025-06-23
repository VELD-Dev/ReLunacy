using Bliss.CSharp;
using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Fonts;
using Bliss.CSharp.Graphics.Rendering.Batches.Primitives;
using Bliss.CSharp.Graphics.Rendering.Batches.Sprites;
using Bliss.CSharp.Graphics.Rendering.Passes;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Images;
using Bliss.CSharp.Interact;
using Bliss.CSharp.Interact.Contexts;
using Bliss.CSharp.Interact.Gamepads;
using Bliss.CSharp.Interact.Keyboards;
using Bliss.CSharp.Logging;
using Bliss.CSharp.Textures;
using Bliss.CSharp.Textures.Cubemaps;
using Bliss.CSharp.Transformations;
using Bliss.CSharp.Windowing;
using Bliss.CSharp.Windowing.Events;
using MiniAudioEx;
using ReLunacy.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Veldrid;
using Veldrid.OpenGL;

namespace ReLunacy.Core;

public class LunaWindow : Disposable
{
    [NotNull] public static LunaWindow Instance { get; private set; }
    public EditorSettings EditorSettings => Program.Settings;
    public ResourcesManager Resources => Program.Resources;

    [NotNull] public IWindow MainWindow { get; private set; }
    [NotNull] public GraphicsDevice GraphicsDevice { get; private set; }
    [NotNull] public CommandList CommandList { get; private set; }

    private double fixedFrameRate;

    private readonly double fixedUpdateTimeStep;
    private double fixedUpdateTimer;

    public FullScreenRenderPass FullScreenRenderPass { get; private set; }
    public RenderTexture2D FullScreenTexture { get; private set; }

    private ImGuiController imGuiController;
    private ImmediateRenderer immediateRenderer;
    private Font font;
    private Texture2D logoTexture;
    private Cubemap skyboxCubemap;
    private Texture2D skyboxTexture;

    private Cam3D camera;
    // Assetmanager

    private long frameCount;

    private string textInput;

    public LunaWindow()
    {
        Instance = this;
        fixedUpdateTimeStep = EditorSettings.FrametimeCap;
    }

    public void Run()
    {
        LunaLog.LogInfo($"Starting {ProgramInfo.DisplayName} v{ProgramInfo.Version}");

        GraphicsDeviceOptions options = new()
        {
            Debug = false,
            HasMainSwapchain = true,
            SwapchainDepthFormat = PixelFormat.D32FloatS8UInt,
            SyncToVerticalBlank = EditorSettings.VSync,
            ResourceBindingModel = ResourceBindingModel.Improved,
            PreferDepthRangeZeroToOne = true,
            PreferStandardClipSpaceYDirection = true,
            SwapchainSrgbFormat = false
        };

        MainWindow = Window.CreateWindow(
            WindowType.Sdl3,
            1280,
            720,
            ProgramInfo.DisplayName,
            WindowState.Resizable,
            options,
            Window.GetPlatformDefaultBackend(),
            out GraphicsDevice graphicsDevice
        );
        MainWindow.Resized += () => OnResize(new(MainWindow.GetX(), MainWindow.GetY(), MainWindow.GetWidth(), MainWindow.GetHeight()));
        GraphicsDevice = graphicsDevice;

        var wndIcon = Resources.GetWindowIcon();
        if(wndIcon != null)
            MainWindow.SetIcon(wndIcon);

        Time.Init();

        SetTargetFPS(EditorSettings.TargetFPS);

        CommandList = graphicsDevice.ResourceFactory.CreateCommandList();

        GlobalResource.Init(graphicsDevice);

        if(MainWindow is Sdl3Window)
        {
            Input.Init(new Sdl3InputContext(MainWindow));
        }
        else
        {
            throw new NotSupportedException("Unsupported window type for input context.");
        }

        AudioContext.Initialize(44100, 2);

        Init();

        while (MainWindow.Exists)
        {
            if (GetTargetFPS() != 0 && Time.Timer.Elapsed.TotalSeconds < fixedFrameRate)
                continue;

            Time.Update();

            MainWindow.PumpEvents();

            Input.Begin();

            AudioContext.Update();
            imGuiController.Update((float)Time.Delta);
            Update(Time.Delta);
            AfterUpdate();

            fixedUpdateTimer += Time.Delta;
            while(fixedUpdateTimer >= fixedUpdateTimeStep)
            {
                FixedUpdate();
                fixedUpdateTimer -= fixedUpdateTimeStep;
            }

            Draw(graphicsDevice, CommandList);
            Input.End();
        }

        LunaLog.LogInfo("Shutting down...");
        OnClose();
    }

    protected virtual void Init()
    {
        FullScreenRenderPass = new FullScreenRenderPass(GraphicsDevice);
        FullScreenTexture = new RenderTexture2D(GraphicsDevice, (uint)MainWindow.GetWidth(), (uint)MainWindow.GetHeight(), (TextureSampleCount)EditorSettings.MSAA_Level);

        immediateRenderer = new ImmediateRenderer(GraphicsDevice);
        float aspectRation = (float)MainWindow.GetWidth() / MainWindow.GetHeight();
        camera = new Cam3D(
            new(0, 0, 0), // Start position
            Vector3.UnitZ,  // Looking at
            aspectRation,
            Vector3.UnitY, // Up vector
            ProjectionType.Perspective,
            CameraMode.Free,
            EditorSettings.CamFOV, // FOV (degrees)
            0.01f, // Near plane
            EditorSettings.RenderDistance // Far plane
        );
    }

    protected virtual void Update(double deltaTime)
    {
        camera.Update(deltaTime);
    }

    protected virtual void AfterUpdate()
    {
    }

    protected virtual void FixedUpdate()
    {
        // Handle quick actions !!   
    }

    protected virtual void Draw(GraphicsDevice graphicsDevice, CommandList commandList)
    {
        commandList.Begin();
        commandList.SetFramebuffer(FullScreenTexture.Framebuffer);
        commandList.ClearColorTarget(0, new RgbaFloat(0.1f, 0.1f, 0.1f, 1.0f));
        commandList.ClearDepthStencil(1.0f);

        Input.EnableRelativeMouseMode();

        camera.Begin();

        // Enumerate all objects like point lights and colliders, and draw them !
        // foreach(volume in Volumes) renderer.Draw(immediateRenderer, commandList);

        // Draw each mesh now !
        // foreach(entity in Entities)
        // {
        //      if(!camera.GetFrustrum().ContainsSphere(entity.BoundingSphere))
        //          continue;
        //      entity.Draw(commandList, FullScreenTexture.Framebuffer.OutputDescription);
        // }

        camera.End();

        if(Input.IsTextInputActive())
        {
            if(Input.GetTypedText(out string txt))
            {
                textInput += txt;
            }

            if(Input.IsKeyPressed(KeyboardKey.BackSpace, true))
            {
                if(textInput.Length > 0)
                {
                    textInput = textInput[..^1]; // Remove last character
                }
            }
        }

        commandList.End();
        graphicsDevice.SubmitCommands(commandList);

        // Draw ScreenPass
        commandList.Begin();

        if(FullScreenTexture.SampleCount != TextureSampleCount.Count1)
        {
            commandList.ResolveTexture(FullScreenTexture.ColorTexture, FullScreenTexture.DestinationTexture);
        }

        commandList.SetFramebuffer(graphicsDevice.SwapchainFramebuffer);
        commandList.ClearColorTarget(0, new RgbaFloat(0.1f, 0.1f, 0.1f, 1.0f));

        FullScreenRenderPass.Draw(commandList, FullScreenTexture, graphicsDevice.SwapchainFramebuffer.OutputDescription);

        commandList.End();

        graphicsDevice.SubmitCommands(commandList);
        graphicsDevice.SwapBuffers();
    }

    protected virtual void OnClose()
    {

    }

    public void OnResize(Rectangle newSize)
    {
        GraphicsDevice.MainSwapchain.Resize((uint)newSize.Width, (uint)newSize.Height);
        FullScreenTexture.Resize((uint)newSize.Width, (uint)newSize.Height);
        camera.Resize((uint)newSize.Width, (uint)newSize.Height);
    }

    public int GetTargetFPS() => (int)(1.0 / fixedUpdateTimeStep);

    public void SetTargetFPS(int fps) => fixedFrameRate = 1.0 / fps;

    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            AudioContext.Deinitialize();
            GlobalResource.Destroy();
            Input.Destroy();
            MainWindow.Dispose();
            GraphicsDevice.Dispose();
        }
    }
}
