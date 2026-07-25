using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Bliss.CSharp;
using Bliss.CSharp.Fonts;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Images;
using Bliss.CSharp.Interact;
using Bliss.CSharp.Interact.Contexts;
using Bliss.CSharp.Textures;
using Bliss.CSharp.Windowing;
using ReLunacy.Core.Frames;
using ReLunacy.Core.Frames.DockedFrames;
using ReLunacy.Core.Frames.Modals;
using ReLunacy.Core.Selection;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Readers;
using ReLunacy.Engine.Rendering;
using ReLunacy.Engine.Scene;
using ReLunacy.MenuBar;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;
using Veldrith;

namespace ReLunacy.Core;

public class LunaWindow : Disposable
{
    [NotNull] public static LunaWindow? Instance { get; private set; }
    public EditorSettings EditorSettings => Program.Settings;
    public ResourcesManager Resources => Program.Resources;

    [NotNull] public IWindow? MainWindow { get; private set; }
    [NotNull] public GraphicsDevice? GraphicsDevice { get; private set; }
    [NotNull] public CommandList? CommandList { get; private set; }
    private double fixedFrameRate;
    private double fixedUpdateTimeStep;
    private double fixedUpdateTimer;
    public FullScreenRenderer FullScreenRenderer { get; private set; } = null!;
    public RenderTexture2D FullScreenTexture { get; private set; } = null!;
    public Texture2D FinalFullScreenTexture { get; private set; } = null!;
    public ImGuiController imGuiController = null!;

    public List<Frame> openFrames = [];

    public FileManager? fileManager { get; private set; }
    public AssetManager? AssetManager { get; private set; }
    public LevelData? Level { get; private set; }
    private bool doLoadEntities;

    /// <summary>Background export tasks (see AssetViewer) can only touch <see cref="openFrames"/>
    /// from the main thread, same rule as the rest of this class — so completions are queued here
    /// (ConcurrentQueue needs no external locking) and drained on the main thread each frame by
    /// <see cref="DoExportCompletionsCheck"/>.</summary>
    private readonly ConcurrentQueue<ExportCompletion> pendingExportCompletions = new();

    public readonly record struct ExportCompletion(LoadingModal ProgressModal, bool Success, string Message, string Directory);

    public void QueueExportCompletion(ExportCompletion completion) => pendingExportCompletions.Enqueue(completion);

    private void DoExportCompletionsCheck()
    {
        while (pendingExportCompletions.TryDequeue(out var completion))
        {
            completion.ProgressModal.loadingFinished = true;
            completion.ProgressModal.LoadEnd = DateTime.Now;
            completion.ProgressModal.isOpen = false;

            AddFrame(new ExportResultModal(completion.Success, completion.Message, completion.Directory));
        }
    }

    public event Action<Frame>? OnFrameAdded;
    public event Action<Frame>? OnFrameRemoved;

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
            WindowType.Sdl3, 1280, 720, ProgramInfo.DisplayName, WindowState.Resizable, options,
            EditorSettings.GraphicsBackend, out GraphicsDevice graphicsDevice);
        MainWindow.Resized += () => OnResize(MainWindow.GetWidth(), MainWindow.GetHeight());
        GraphicsDevice = graphicsDevice;

        var wndIcon = Resources.GetWindowIcon();
        if (wndIcon != null) MainWindow.SetIcon(wndIcon);

        Time.Init();
        SetTargetFPS(EditorSettings.TargetFPS);

        CommandList = graphicsDevice.ResourceFactory.CreateCommandList();

        GlobalResource.Init(graphicsDevice);

        if (MainWindow is not Sdl3Window)
            throw new NotSupportedException("Unsupported window type for input context.");
        Input.Init(new Sdl3InputContext(MainWindow));

        Init();

        while (MainWindow.Exists)
        {
            if (GetTargetFPS() != 0 && Time.Timer.Elapsed.TotalSeconds < fixedFrameRate)
                continue;

            Time.Update();
            MainWindow.PumpEvents();
            Input.Begin();

            imGuiController.Update((float)Time.Delta);
            Update(Time.Delta);

            fixedUpdateTimer += Time.Delta;
            while (fixedUpdateTimer >= fixedUpdateTimeStep)
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
        while (MainWindow.Exists)
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

        AddFrame(new View3D(GraphicsDevice));
        AddFrame(new PropertyInspectorFrame());
        AddFrame(new BasicEntityExplorer());

        PeriodicalSave();
    }

    /// <summary>
    /// User-picked debug.dat, set via the "Load a debug.dat" tab — takes priority over whatever
    /// auto-detection would otherwise find, and survives across a reload of the same level so the
    /// tab can be used after the fact to fix a level that loaded without one.
    /// </summary>
    public string? PendingExternalDebugDatPath { get; set; }

    public async void LoadLevelDataAsync(string path, LoadingModal? loadingFrame = null, string? debugDatPath = null)
    {
        TryWipeLevel();
        LunaLog.LogInfo($"Loading level {path.Split(Path.DirectorySeparatorChar)[^1]}.");
        Program.ProvidedPath = path;

        await Task.Run(() =>
        {
            fileManager = new FileManager();
            if (path.EndsWith(".psarc", StringComparison.OrdinalIgnoreCase))
                fileManager.LoadFromPsarcFile(path);
            else
                fileManager.LoadFolder(path);

            // Old engine only: debug.dat almost never ships alongside main.dat/the level's own
            // .psarc — try, in priority order, whatever the user explicitly picked, then whatever
            // the caller already resolved (GameBrowserFrame via GameLibraryScanner), then fall
            // back to deriving it from the path directly (for callers, like the manual "Open
            // level" dialog, that never went through the scanner at all).
            if (fileManager.isOld && fileManager.igfiles.GetValueOrDefault("debug.dat") is null)
            {
                string? resolvedDebugDat = PendingExternalDebugDatPath ?? debugDatPath ?? Engine.Games.GameLibraryScanner.TryResolveDebugDatPath(path);
                if (resolvedDebugDat != null)
                    fileManager.LoadExternalDebugDat(resolvedDebugDat);
            }

            var levelReader = new LevelReader(fileManager);
            Level = levelReader.LoadLevel((status, progress) =>
                loadingFrame?.UpdateProgress(0, new LoadingProgress(status, 100, true) { current = (uint)(progress * 100) }));
        });

        doLoadEntities = true;
        LunaLog.LogDebug("Level loaded.");
    }

    /// <summary>Applies a user-picked debug.dat to the currently loaded level by reloading it —
    /// the reload runs every name through the exact same DebugReader path a normal load does,
    /// rather than trying to retroactively patch names onto already-built entities.</summary>
    public void LoadExternalDebugDatAndReload(string debugDatPath, LoadingModal? loadingFrame = null)
    {
        PendingExternalDebugDatPath = debugDatPath;
        if (!string.IsNullOrEmpty(Program.ProvidedPath))
            LoadLevelDataAsync(Program.ProvidedPath, loadingFrame);
    }

    /// <summary>
    /// Disposes the currently loaded level (EntityManager's GPU meshes, AssetManager's built
    /// models/textures, FileManager's open file handles) and notifies every open frame that
    /// implements <see cref="ILevelListener"/> beforehand, so nothing is left holding a reference
    /// to an object that's about to be destroyed — most importantly the current selection, which
    /// otherwise leaves View3D pointing a disposed mesh at the GPU the very next frame.
    /// </summary>
    public void TryWipeLevel()
    {
        if (string.IsNullOrEmpty(Program.ProvidedPath)) return;
        if (AssetManager is null || Level is null || fileManager is null) return;

        foreach (var listener in openFrames.OfType<ILevelListener>())
            listener.OnLevelUnloading();

        SelectionManager.Singleton.Deselect();

        EntityManager.Singleton.Dispose();
        AssetManager.Dispose();
        fileManager.Dispose();

        Level = null;
        fileManager = null;
        AssetManager = null;
        Program.ProvidedPath = string.Empty;
    }

    private void DoLoadEntitiesCheck()
    {
        if (!doLoadEntities) return;
        doLoadEntities = false;

        if (Level is null) return;

        AssetManager = new AssetManager(Level, GraphicsDevice);
        EntityManager.Singleton.LoadRegion(Level.Region, AssetManager, GraphicsDevice);

        foreach (var listener in openFrames.OfType<ILevelListener>())
            listener.OnLevelLoaded();

        var loadModal = GetFirstFrame<LoadingModal>();

        var region = EntityManager.Singleton.Regions.FirstOrDefault();
        var firstMoby = region?.MobyInstances.Entities.FirstOrDefault();
        if (firstMoby != null)
            SelectionManager.Singleton.Select(firstMoby);

        if (loadModal is null) return;
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
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(5, 5));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(5, 5));
    }

    private void RenderUI(double deltaTime)
    {
        RenderMenuBar();

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);

        RenderDockSpace();

        ImGui.PopStyleVar();
        SetDefaultStyleVar();
        foreach (Frame frame in openFrames.ToList())
        {
            frame.RenderAsWindow(deltaTime);
        }
        ImGui.PopStyleVar(11);

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
        ImGui.DockSpace(dockspaceId, Vector2.Zero, dockspaceFlags);

        var frameNames = openFrames.Select(f => f.FrameName).ToList();
        DockspaceLayoutManager.TryApplyLayout(dockspaceId, DockspacePreset.Default, frameNames);

        return dockspaceOpen;
    }

    private void RenderMenuBar()
    {
        if (!ImGui.BeginMainMenuBar()) return;

        if (ImGui.BeginMenu(LM.Get("GUI_Menu_File")))
        {
            FileMenuDraw.OpenLevelMenuItem();
            FileMenuDraw.OpenGameBrowserMenuItem();
            FileMenuDraw.ExportLevelMenuItem();
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
            ViewMenuDraw.ShowShaderBrowser();
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
            ImGui.Separator();
            RenderMenuDraw.ShowMobyDistanceCulling();
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

    public bool IsAnyFrameOpened<T>() where T : Frame => openFrames.Any(f => f.GetType() == typeof(T));

    public void TryCloseFirstFrame<T>() where T : Frame
    {
        if (!IsAnyFrameOpened<T>()) return;
        var frameToClose = GetFirstFrame<T>()!;
        frameToClose.isOpen = false;
        OnFrameRemoved?.Invoke(frameToClose);
    }

    public T? GetFirstFrame<T>() where T : Frame => IsAnyFrameOpened<T>() ? openFrames.First(f => f.GetType() == typeof(T)) as T : null;

    private static bool FrameMustClose(Frame frame) => !frame.isOpen;

    protected virtual void Update(double deltaTime)
    {
        Entity.EntitiesRenderedThisFrame = 0;

        // EntityManager is engine-layer and deliberately doesn't read Program.Settings (see
        // AssetManager's decalOffset for the same convention) — so the persisted setting is
        // pushed in here every frame instead of being read where it's consumed. Cheap enough
        // (one bool) to just always do, rather than only on Settings-frame Apply, so a value
        // loaded from disk at startup takes effect immediately without the user having to open
        // the Settings frame and toggle the checkbox once first.
        EntityManager.Singleton.FrustumCullingEnabled = EditorSettings.FrustrumCulling;
        AssetManager?.SetBackfaceCulling(EditorSettings.BackfaceCulling);

        openFrames.RemoveAll(FrameMustClose);

        if (Overlay.showOverlay)
            Overlay.DrawOverlay(Overlay.showOverlay);

        RenderUI(deltaTime);
    }

    protected virtual void AfterUpdate()
    {
        DoLoadEntitiesCheck();
        DoExportCompletionsCheck();
    }

    protected virtual void FixedUpdate() { }

    protected virtual void Draw(GraphicsDevice graphicsDevice, CommandList commandList)
    {
        commandList.Begin();
        commandList.SetFramebuffer(FullScreenTexture.Framebuffer);
        commandList.ClearColorTarget(0, new RgbaFloat(0.1f, 0.1f, 0.1f, 1.0f));
        commandList.ClearDepthStencil(1.0f);

        imGuiController.Render(graphicsDevice, commandList);

        commandList.End();
        graphicsDevice.SubmitCommands(commandList);

        commandList.Begin();

        if (FullScreenTexture.SampleCount != TextureSampleCount.Count1)
            commandList.ResolveTexture(FullScreenTexture.ColorTexture, FinalFullScreenTexture.DeviceTexture);
        else
            commandList.CopyTexture(FullScreenTexture.ColorTexture, FinalFullScreenTexture.DeviceTexture);

        commandList.SetFramebuffer(graphicsDevice.SwapchainFramebuffer);
        commandList.ClearColorTarget(0, new RgbaFloat(0.1f, 0.1f, 0.1f, 1.0f));

        FullScreenRenderer.Draw(commandList, FinalFullScreenTexture, graphicsDevice.SwapchainFramebuffer.OutputDescription);

        commandList.End();
        graphicsDevice.SubmitCommands(commandList);
        // Veldrith's Vulkan backend only signals a render-finished semaphore before presenting
        // when the present queue differs from the graphics queue — on a shared queue (the common
        // case on desktop GPUs), SwapBuffers's vkQueuePresentKHR call waits on nothing at all, so
        // without this the presentation engine can read the swapchain image before the GPU has
        // finished writing it, showing stale/previous-frame content (flicker, visible in both the
        // 3D viewport and the GUI since both are already composited into this image by here).
        // WaitForIdle was previously called before this Submit instead of after, which only waited
        // on the *prior* frame's work and left this exact gap uncovered.
        graphicsDevice.WaitForIdle();
        graphicsDevice.SwapBuffers();
    }

    protected virtual void OnClose() { }

    private void OnResize(int width, int height)
    {
        imGuiController.Resize(width, height);
        GraphicsDevice.MainSwapchain.Resize((uint)width, (uint)height);
        FullScreenTexture.Resize((uint)width, (uint)height);
        FinalFullScreenTexture.Dispose();
        FinalFullScreenTexture = new Texture2D(GraphicsDevice, new Image(width, height), false);
    }

    public int GetTargetFPS() => (int)(1.0 / fixedUpdateTimeStep);

    public void SetTargetFPS(int fps)
    {
        fixedFrameRate = fps == 0 ? double.MaxValue : 1.0 / fps;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            GlobalResource.Destroy();
            Input.Destroy();
            MainWindow.Dispose();
            GraphicsDevice.Dispose();
        }
    }
}
