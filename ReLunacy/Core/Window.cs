using Bliss.CSharp;
using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Fonts;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
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
using LibLunacy;
using LibLunacy.Numerics;
using LibLunacy.Shaders;
using MiniAudioEx;
using ReLunacy.Core.EntityManagement;
using ReLunacy.Core.Frames;
using ReLunacy.Core.Frames.DockedFrames;
using ReLunacy.Core.Frames.Modals;
using ReLunacy.MenuBar;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using MiniAudioEx.Core.StandardAPI;
using ReLunacy.Core.Selection;
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
    private long frameCount;
    public FullScreenRenderer FullScreenRenderer { get; private set; }
    public RenderTexture2D FullScreenTexture { get; private set; }
    public Texture2D FinalFullScreenTexture { get; private set; }
    public ImGuiController imGuiController;
    private Texture2D logoTexture;

    public List<Frame> openFrames = [];

    public FileManager fileManager { get; private set; }
    public AssetManager AssetManager { get; private set; }
    public LunaLoader Loader { get; private set; }
    private bool doLoadEntities = false;

    public event Action<Frame> OnFrameAdded;
    public event Action<Frame> OnFrameRemoved;
    public event Action<AssetManager, LunaLoader> OnLoadingFinished;

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
        {
            LunaLog.LogInfo("Setting window icon.");
            MainWindow.SetIcon(wndIcon);
        }

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

            //Entity.EntitiesRenderedThisFrame = 0;
            Time.Update();

            MainWindow.PumpEvents();

            Input.Begin();

            AudioContext.Update();
            imGuiController.Update((float)Time.Delta);
            Update(Time.Delta);

            fixedUpdateTimer += Time.Delta;
            while(fixedUpdateTimer >= fixedUpdateTimeStep)
            {
                FixedUpdate();
                fixedUpdateTimer -= fixedUpdateTimeStep;
            }

            Draw(graphicsDevice, CommandList);
            AfterUpdate();
            Input.End();
        }

        LunaLog.LogInfo("Shutting down...");
        OnClose();
    }

    public async void PeriodicalSave()
    {
        while(MainWindow.Exists)
        {
            LM.SaveLanguages();
            await Task.Delay(30 * 1000);
        }
    }

    protected virtual void Init()
    {
        FullScreenRenderer = new FullScreenRenderer(GraphicsDevice);
        var (width, height) = (MainWindow.GetWidth(), MainWindow.GetHeight());
        FullScreenTexture = new RenderTexture2D(GraphicsDevice, (uint)width, (uint)height, false, (TextureSampleCount)EditorSettings.MSAA_Level);
        FinalFullScreenTexture = new Texture2D(GraphicsDevice, new Image(width, height), false);
        imGuiController = new ImGuiController(GraphicsDevice, FullScreenTexture.Framebuffer.OutputDescription, (int)FullScreenTexture.Width, (int)FullScreenTexture.Height);

        LM.Initialize();

        ShaderManager.LoadDefaultShaders(GraphicsDevice);

        // Update Checker
        UpdateChecker.CheckUpdates();

        AddFrame(new View3D(GraphicsDevice));
        AddFrame(new PropertyInspectorFrame());
        AddFrame(new BasicEntityExplorer());

        PeriodicalSave();
    }

    public async void LoadLevelDataAsync(string path, LoadingModal loadingFrame)
    {
        TryWipeLevel();
        //AddFrame(loadingFrame);
        LunaLog.LogInfo($"Loading level {path.Split(Path.DirectorySeparatorChar)[^1]}.");
        Program.ProvidedPath = path;

        fileManager = new();
        LunaLog.LogDebug("Starting FileManager threaded task.");
        fileManager.LoadFolder(path);

        LunaLog.LogDebug("Starting AssetLoader threaded task.");
        var alTask = Task.Run(() => Loader = new LunaLoader(loadingFrame, fileManager, LunaLoader.LoadingSettings.Default));
        LunaLog.LogDebug("Awaiting for AssetLoader to finish its work...");
        await alTask;
        loadingFrame.UpdateProgress(0, new(1, 1));
        doLoadEntities = true;
        LunaLog.LogDebug("Level loaded.");
        //Thread.Sleep(100);
        //loadingFrame.isOpen = false;
    }

    public void TryWipeLevel()
    {
        if (Program.ProvidedPath == string.Empty || Program.ProvidedPath == null)
            return;

        if (AssetManager is null || Loader is null || EntityManager.Singleton is null || fileManager is null)
            return;

        EntityManager.Singleton.Dispose();
        AssetManager.Dispose();
        Loader?.Dispose();
        Loader = null;
        fileManager = null;
        AssetManager = null;
        Program.ProvidedPath = string.Empty;
        /*
        if (IsAnyFrameOpened<BasicEntityExplorer>())
            GetFirstFrame<BasicEntityExplorer>().Wipe();
        */
    }

    private void DoLoadEntitiesCheck()
    {
        if (!doLoadEntities) return;
        doLoadEntities = false;

        AssetManager = new AssetManager(Loader, GraphicsDevice);

        EntityManager.Singleton.LoadRegions(Loader, AssetManager, GraphicsDevice);
        if (IsAnyFrameOpened<BasicEntityExplorer>())
            GetFirstFrame<BasicEntityExplorer>();//.SetEntities(EntityManager.Singleton.GetAllEntities());
        var loadModal = GetFirstFrame<LoadingModal>();
        if (IsAnyFrameOpened<TexturesExplorer>())
            Task.Run(() => GetFirstFrame<TexturesExplorer>().TransmitTextures(AssetManager, Loader));

        if (IsAnyFrameOpened<AssetViewer>())
            Task.Run(() => GetFirstFrame<AssetViewer>().TransmitAssets(AssetManager, Loader));

        SelectionManager.Singleton.Select(EntityManager.Singleton.Regions[0].MobyInstances.Entities[0]);

        if (loadModal is null)
            return;
        loadModal.loadingFinished = true;
        loadModal.LoadEnd = DateTime.Now;
    }

    public static void SetDefaultStyleVar()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2.5f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 2.5f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 2.5f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vec2(5, 5));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vec2(5, 5));
    }

    private void RenderUI(double deltaTime)
    {
        RenderMenuBar();

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vec2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vec2.Zero);

        RenderDockSpace();

        ImGui.PopStyleVar();
        SetDefaultStyleVar();
        foreach (Frame frame in openFrames.ToList())
        {
            frame.RenderAsWindow(deltaTime);
        }
        ImGui.PopStyleVar(11);

        // Dockspace end
        ImGui.End();
    }

    private bool RenderDockSpace()
    {
        ImGuiDockNodeFlags dockspaceFlags = ImGuiDockNodeFlags.PassthruCentralNode;
        ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoDocking
            | ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoBringToFrontOnFocus
            | ImGuiWindowFlags.NoNavFocus
            | ImGuiWindowFlags.NoBackground;
        ImGui.SetNextWindowViewport(ImGui.GetWindowViewport().ID);
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().WorkPos);
        ImGui.SetNextWindowSize(ImGui.GetMainViewport().WorkSize);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
        var dockspaceOpen = ImGui.Begin("dockspace", windowFlags);
        ImGui.PopStyleVar(2);

        uint dockspaceId = ImGui.GetID("dockspace");
        ImGui.DockSpace(dockspaceId, new Vec2(0, 0), dockspaceFlags);

        var frameNames = openFrames.Select(f => f.FrameName).ToList();
        DockspaceLayoutManager.TryApplyLayout(dockspaceId, DockspacePreset.Default, frameNames);

        return dockspaceOpen;
    }


    private void RenderMenuBar()
    {
        if (!ImGui.BeginMainMenuBar())
            return;

        if (ImGui.BeginMenu(LM.Get("GUI_Menu_File")))
        {
            FileMenuDraw.OpenLevelMenuItem();
            FileMenuDraw.CloseLevelMenuItem();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu(LM.Get("GUI_Menu_Edit")))
        {
            EditMenuDraw.EditorSettingsMenuItem();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu(LM.Get("GUI_Menu_Tools")))
        {
            ToolsMenuDraw.TranslationTool();
            ToolsMenuDraw.RotationTool();
            ToolsMenuDraw.ScaleTool();
            ImGui.Separator();
            ToolsMenuDraw.DeselectObject();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu(LM.Get("GUI_Menu_View")))
        {
            ViewMenuDraw.ShowOverlay();
            ImGui.Separator();
            ViewMenuDraw.ShowView3D();
            ViewMenuDraw.ShowAssetViewer();
            ViewMenuDraw.ShowTextureExplorer();
            ViewMenuDraw.ShowEntityExplorer();
            ViewMenuDraw.ShowInstanceInspector();
            ViewMenuDraw.ShowConsoleFrame();
            ImGui.Separator();
            ViewMenuDraw.ShowPSArcExplorer();
            ImGui.Separator();
            ViewMenuDraw.LayoutPresets();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu(LM.Get("GUI_Menu_Render")))
        {
            RenderMenuDraw.ShowMobys();
            RenderMenuDraw.ShowTies();
            RenderMenuDraw.ShowUFrags();
            RenderMenuDraw.ShowVolumes();
            RenderMenuDraw.ShowBoundingSpheres();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu(LM.Get("GUI_Menu_About")))
        {
            AboutMenuDraw.GithubLink();
            AboutMenuDraw.CheckForUpdate();
            ImGui.EndMenu();
        }

        DebugMenuDraw.Menu();

        ImGui.EndMainMenuBar();
    }

    public void AddFrame(Frame frame)
    {
        openFrames.Add(frame);
        OnFrameAdded?.Invoke(frame);
    }

    public bool IsAnyFrameOpened<T>() where T : Frame
    {
        return openFrames.Any(f => f.GetType() == typeof(T));
    }

    public void TryCloseFirstFrame<T>() where T : Frame
    {
        if (IsAnyFrameOpened<T>())
        {
            var frameToClose = GetFirstFrame<T>();
            frameToClose.isOpen = false;
            OnFrameRemoved?.Invoke(frameToClose);
        }
    }

    public T? GetFirstFrame<T>() where T : Frame
    {
        if (!IsAnyFrameOpened<T>()) return null;
        return openFrames.First(f => f.GetType() == typeof(T)) as T;
    }

    static bool FrameMustClose(Frame frame) => !frame.isOpen;

    protected virtual void Update(double deltaTime)
    {
        Entity.EntitiesRenderedThisFrame = 0;

        openFrames.RemoveAll(FrameMustClose);

        if(Overlay.showOverlay)
        {
            Overlay.DrawOverlay(Overlay.showOverlay);
        }

        RenderUI(deltaTime);
    }

    protected virtual void AfterUpdate()
    {
        DoLoadEntitiesCheck();
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

        imGuiController.Render(graphicsDevice, commandList);

        commandList.End();
        graphicsDevice.SubmitCommands(commandList);

        // Draw ScreenPass
        commandList.Begin();
        
        if (FullScreenTexture.SampleCount != TextureSampleCount.Count1)
        {
            commandList.ResolveTexture(FullScreenTexture.ColorTexture, FinalFullScreenTexture.DeviceTexture);
        }
        else
        {
            commandList.CopyTexture(FullScreenTexture.ColorTexture, FinalFullScreenTexture.DeviceTexture);
        }

        commandList.SetFramebuffer(graphicsDevice.SwapchainFramebuffer);
        commandList.ClearColorTarget(0, new RgbaFloat(0.1f, 0.1f, 0.1f, 1.0f));
        
        FullScreenRenderer.Draw(commandList, FinalFullScreenTexture, graphicsDevice.SwapchainFramebuffer.OutputDescription);

        commandList.End();
        graphicsDevice.WaitForIdle();
        graphicsDevice.SubmitCommands(commandList);
        graphicsDevice.SwapBuffers();
    }

    protected virtual void OnClose()
    {

    }

    void OnResize(Rectangle newSize)
    {
        imGuiController.Resize(newSize.Width, newSize.Height);
        GraphicsDevice.MainSwapchain.Resize((uint)newSize.Width, (uint)newSize.Height);
        FullScreenTexture.Resize((uint)newSize.Width, (uint)newSize.Height);
        FinalFullScreenTexture.Dispose();
        FinalFullScreenTexture = new Texture2D(GraphicsDevice, new Image(newSize.Width, newSize.Height), false);
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
