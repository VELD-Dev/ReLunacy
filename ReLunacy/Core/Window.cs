using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using ReLunacy.Core.Frames;
using ReLunacy.Core.Frames.DockedFrames;
using ReLunacy.Core.Frames.Modals;
using ReLunacy.Core.Selection;
using ReLunacy.Engine.Diagnostics;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Readers;
using ReLunacy.Engine.Rendering;
using ReLunacy.Engine.Scene;
using ReLunacy.MenuBar;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;
using NeoVeldrid;

namespace ReLunacy.Core;

public class LunaWindow : IDisposable
{
    [NotNull] public static LunaWindow? Instance { get; private set; }
    public EditorSettings EditorSettings => Program.Settings;
    public ResourcesManager Resources => Program.Resources;

    [NotNull] public EditorWindow? MainWindow { get; private set; }
    [NotNull] public GraphicsDevice? GraphicsDevice { get; private set; }
    [NotNull] public CommandList? CommandList { get; private set; }
    private double fixedFrameRate;
    private double fixedUpdateTimeStep;
    private double fixedUpdateTimer;
    public FullscreenBlit FullScreenRenderer { get; private set; } = null!;
    public MainRenderTarget FullScreenTexture { get; private set; } = null!;
    public ImGuiController imGuiController = null!;

    public List<Frame> openFrames = [];

    /// <summary>The real dockspace node's ID, cached from RenderDockSpace (the only place
    /// ImGui.GetID("dockspace") is valid to call - see its comment there). Callers that need to
    /// target the dockspace from elsewhere (e.g. ViewMenuDraw's Force Apply/Save/Load buttons)
    /// should read this instead of recomputing GetID("dockspace") themselves.</summary>
    public uint DockspaceId { get; private set; }

    /// <summary>Session memory of where each DockedFrame TYPE last sat (Frame.GetType().Name ->
    /// ImGuiWindowPtr.DockId), captured the moment a frame closes (see the RemoveAll(FrameMustClose)
    /// call in Update) and consulted by AddFrame the next time that type opens. This is what makes
    /// closing and reopening a panel mid-session put it back where it was instead of leaving it
    /// floating - independent of, and in addition to, the cross-session SavedLayout.FrameDockIds
    /// mechanism, which only kicks in via an explicit named-layout save/load.</summary>
    private readonly Dictionary<string, uint> _lastFrameDockIds = [];

    public FileManager? fileManager { get; private set; }
    public AssetManager? AssetManager { get; private set; }
    public LevelData? Level { get; private set; }

    /// <summary>The level and its background-decoded textures, handed from LoadLevelDataAsync's
    /// background task to <see cref="DoLoadEntitiesCheck"/> on the main thread - queued (same pattern
    /// as <see cref="pendingExportCompletions"/>) so both arrive as one atomic message.</summary>
    private readonly ConcurrentQueue<LevelReady> pendingLevelReady = new();

    private readonly record struct LevelReady(LevelData Level, Dictionary<ulong, ReLunacy.Engine.Rendering.Resources.TextureLevels?> PreparedTextures);

    /// <summary>Background export tasks (see AssetViewer) can only touch <see cref="openFrames"/>
    /// from the main thread, same rule as the rest of this class - so completions are queued here
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
            SwapchainDepthFormat = PixelFormat.D32_Float_S8_UInt,
            SyncToVerticalBlank = EditorSettings.VSync,
            ResourceBindingModel = ResourceBindingModel.Improved,
            PreferDepthRangeZeroToOne = true,
            PreferStandardClipSpaceYDirection = true,
            SwapchainSrgbFormat = false
        };

        MainWindow = EditorWindow.Create(
            EditorSettings.WindowWidth, EditorSettings.WindowHeight, EditorSettings.WindowMaximized,
            ProgramInfo.DisplayName, options,
            EditorSettings.GraphicsBackend, out GraphicsDevice graphicsDevice);
        MainWindow.Resized += () => OnResize(MainWindow.GetWidth(), MainWindow.GetHeight());
        GraphicsDevice = graphicsDevice;

        var wndIcon = Resources.GetWindowIcon();
        if (wndIcon != null) MainWindow.SetIcon(wndIcon);

        // The renderer's shaders are compile-time constants, so their SPIR-V can be built before any
        // level exists. Warmed up here on a worker so it's off the critical path of the first level
        // load. Fire-and-forget: a level load that beats it simply compiles what it needs.
        Task.Run(() =>
        {
            try { Engine.Rendering.Vulkan.VulkanRenderer.WarmUpShaderCache(); }
            catch (Exception e) { LunaLog.LogError($"[VkRenderer] shader warm-up failed: {e.Message}"); }
        });

        Time.Init();
        SetTargetFPS(EditorSettings.TargetFPS);

        CommandList = graphicsDevice.ResourceFactory.CreateCommandList();

        Input.Init(MainWindow);

        Init();

        while (MainWindow.Exists)
        {
            if (GetTargetFPS() != 0 && Time.Timer.Elapsed.TotalSeconds < fixedFrameRate)
                continue;

            Time.Update();

            // Only instrument when something is actually displaying the breakdown.
            FrameProfiler.Enabled =
                IsAnyFrameOpened<ProfilerFrame>() || (Overlay.showOverlay && Overlay.ShowProfiler);
            FrameProfiler.BeginFrame();

            using (FrameProfiler.Sample("Events"))
            {
                // Must snapshot the previous frame's state before this frame's SDL events land: Begin()
                // copies _mouseDown/_keysDown into the "last" arrays that edge-triggered queries
                // (IsMouseButtonPressed, IsKeyPressed) compare against.
                Input.Begin();
                MainWindow.PumpEvents();
            }

            using (FrameProfiler.Sample("ImGui NewFrame"))
                imGuiController.Update((float)Time.Delta);

            using (FrameProfiler.Sample("Update"))
                Update(Time.Delta);

            fixedUpdateTimer += Time.Delta;
            while (fixedUpdateTimer >= fixedUpdateTimeStep)
            {
                FixedUpdate();
                fixedUpdateTimer -= fixedUpdateTimeStep;
            }

            using (FrameProfiler.Sample("Draw"))
                Draw(graphicsDevice, CommandList);

            AfterUpdate();
            Input.End();
            FrameProfiler.EndFrame();
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
        FullScreenRenderer = new FullscreenBlit(GraphicsDevice);
        var (width, height) = MainWindow.GetSizeInPixels();
        FullScreenTexture = new MainRenderTarget(GraphicsDevice, (uint)width, (uint)height, (TextureSampleCount)EditorSettings.MSAA_Level);
        imGuiController = new ImGuiController(GraphicsDevice, FullScreenTexture.Framebuffer.OutputDescription, (int)FullScreenTexture.Width, (int)FullScreenTexture.Height);

        LM.Initialize();

        // Restore last session's Render menu choices (see EditorSettings.RenderMobys and friends'
        // own doc comment) before anything ever renders, so the very first frame already reflects
        // them instead of a brief flash of the engine's own hardcoded defaults.
        var em = EntityManager.Singleton;
        em.renderMobys = EditorSettings.RenderMobys;
        em.renderTies = EditorSettings.RenderTies;
        em.renderUFrags = EditorSettings.RenderUFrags;
        em.renderFoliage = EditorSettings.RenderFoliage;
        em.renderVolumes = EditorSettings.RenderVolumes;
        em.renderBoundingSpheres = EditorSettings.RenderBoundingSpheres;
        em.MobyDistanceCullingEnabled = EditorSettings.MobyDistanceCullingEnabled;

        // Restore whichever layout the user last had active (see EditorSettings.ActiveLayoutName).
        // Loading it here, before any frame exists, marks DockspaceLayoutManager's default-preset
        // guard as already satisfied, so RenderDockSpace's TryApplyLayout call won't overwrite it
        // with the hardcoded DockspacePreset.Default later. openFrames is empty at this point (no
        // frame has been added yet), so every frame type the saved layout had docked gets reopened
        // here via ViewMenuDraw.EnsureFrameTypeOpen - this is what actually reconstructs "the editor
        // as last left it" instead of only repositioning whichever 3 frames Init() happened to add
        // by default. Only fall back to that hardcoded default set if there was nothing to restore
        // (fresh install, or the active layout had none of these frames docked at all).
        bool restoredLayout = DockspaceLayoutManager.TryLoadLayout(EditorSettings, EditorSettings.ActiveLayoutName, 0, openFrames, ViewMenuDraw.EnsureFrameTypeOpen);

        // ActiveLayoutName can be unset (fresh install, or a session that never explicitly saved a
        // named layout) even though the previous session's exact panel arrangement was still
        // captured into the LatestLayoutName auto-save slot on exit (see SaveActiveLayout) - fall
        // back to that before giving up and building the hardcoded 3-frame default.
        if (!restoredLayout)
            restoredLayout = DockspaceLayoutManager.TryLoadLayout(EditorSettings, DockspaceLayoutManager.LatestLayoutName, 0, openFrames, ViewMenuDraw.EnsureFrameTypeOpen);

        if (!restoredLayout)
        {
            AddFrame(new View3D(GraphicsDevice));
            AddFrame(new PropertyInspectorFrame());
            AddFrame(new BasicEntityExplorer());
        }

        PeriodicalSave();
    }

    /// <summary>
    /// User-picked debug.dat, set via the "Load a debug.dat" tab - takes priority over whatever
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

            // Old engine only: debug.dat almost never ships alongside main.dat/the level's own .psarc -
            // try, in priority order, whatever the user explicitly picked, then whatever the caller
            // already resolved, then fall back to deriving it from the path directly.
            if (fileManager.isOld && fileManager.igfiles.GetValueOrDefault("debug.dat") is null)
            {
                string? resolvedDebugDat = PendingExternalDebugDatPath ?? debugDatPath ?? Engine.Games.GameLibraryScanner.TryResolveDebugDatPath(path);
                if (resolvedDebugDat != null)
                    fileManager.LoadExternalDebugDat(resolvedDebugDat);
            }

            var levelReader = new LevelReader(fileManager);
            Level = levelReader.LoadLevel((status, progress) =>
                loadingFrame?.UpdateProgress(0, new LoadingProgress(status, 100, true) { current = (uint)(progress * 100) }));

            // Decode every texture and build its mip chain HERE, still on the loading task and in
            // parallel across cores - none of it needs the graphics device.
            var swPrep = System.Diagnostics.Stopwatch.StartNew();
            var prepared = AssetManager.PrepareTextures(Level);
            LunaLog.LogDebug($"Decoded {prepared.Count} textures in {swPrep.ElapsedMilliseconds}ms (loading task, parallel).");

            pendingLevelReady.Enqueue(new LevelReady(Level, prepared));
        });

        LunaLog.LogDebug("Level loaded.");
    }

    /// <summary>Applies a user-picked debug.dat to the currently loaded level by reloading it -
    /// the reload runs every name through the exact same DebugReader path a normal load does,
    /// rather than trying to retroactively patch names onto already-built entities.</summary>
    public void LoadExternalDebugDatAndReload(string debugDatPath, LoadingModal? loadingFrame = null)
    {
        PendingExternalDebugDatPath = debugDatPath;
        if (!string.IsNullOrEmpty(Program.ProvidedPath))
            LoadLevelDataAsync(Program.ProvidedPath, loadingFrame);
    }

    /// <summary>
    /// Clears the currently loaded level (EntityManager's GPU meshes, AssetManager's built
    /// models/textures, FileManager's open file handles) and notifies every open frame that
    /// implements <see cref="ILevelListener"/> beforehand, so nothing is left holding a reference
    /// to an object that's about to be destroyed. The GPU-side disposal itself happens slightly
    /// later (see FlushPendingLevelWipe), but every field this class exposes already reads as
    /// cleared once this returns.
    /// </summary>
    public void TryWipeLevel()
    {
        if (string.IsNullOrEmpty(Program.ProvidedPath)) return;
        if (AssetManager is null || Level is null || fileManager is null) return;

        foreach (var listener in openFrames.OfType<ILevelListener>())
            listener.OnLevelUnloading();

        SelectionManager.Singleton.Deselect();

        // The actual GPU-resource disposal (scene renderer, entity meshes, asset textures) is deferred
        // to FlushPendingLevelWipe, run from AfterUpdate after this frame's Draw has submitted,
        // presented and WaitForIdle'd - disposing them here synchronously would free resources ImGui
        // may already have queued a draw command against earlier this same frame. fileManager.Dispose()
        // just closes file handles, not GPU state, so it stays synchronous.
        _pendingWipeAssetManager = AssetManager;
        fileManager.Dispose();

        Level = null;
        fileManager = null;
        AssetManager = null;
        Program.ProvidedPath = string.Empty;
        // In case this level was wiped mid-load, while DoLoadEntitiesCheck was still draining its
        // queued texture uploads.
        _finalizingLevelLoad = false;
        _uploadProgress = null;
    }

    private AssetManager? _pendingWipeAssetManager;

    /// <summary>Actually frees the GPU resources a wipe queued up in TryWipeLevel (see its comment
    /// for why this can't happen synchronously there). DisposeSceneRenderer must run before
    /// EntityManager.Dispose(): the captured scene references live entity meshes/geometry and the
    /// VulkanSceneCapture registry that EntityManager's own disposal invalidates.</summary>
    private void FlushPendingLevelWipe()
    {
        if (_pendingWipeAssetManager is null) return;
        _pendingWipeAssetManager.DisposeSceneRenderer();
        EntityManager.Singleton.Dispose();
        _pendingWipeAssetManager.Dispose();
        _pendingWipeAssetManager = null;
    }

    // True from the moment a level's AssetManager/entities are built until its queued texture uploads
    // have fully drained - see DoLoadEntitiesCheck. Spans many frames for one level.
    private bool _finalizingLevelLoad;
    // The upload phase's own progress bar slot on the loading modal - kept as a direct reference so
    // each frame can just mutate .current instead of reconstructing/relocking through UpdateProgress.
    private LoadingProgress? _uploadProgress;

    // Per-frame time budget for draining queued texture uploads (see AssetManager.UploadOnePendingTexture).
    // A time budget rather than a count, since texture sizes vary hugely (a 4K atlas vs a 32x32 icon).
    // Runs inside AfterUpdate, after this frame's Draw, so overrunning slightly delays next frame's
    // Present rather than corrupting this one.
    private const double UploadBudgetMs = 20.0;

    private void DoLoadEntitiesCheck()
    {
        if (pendingLevelReady.TryDequeue(out var ready))
        {
            var lsw = System.Diagnostics.Stopwatch.StartNew();
            AssetManager = new AssetManager(ready.Level, GraphicsDevice, ready.PreparedTextures);
            long a0 = lsw.ElapsedMilliseconds;
            EntityManager.Singleton.LoadRegion(ready.Level.Region, AssetManager, GraphicsDevice);
            long tRegion = lsw.ElapsedMilliseconds - a0; a0 = lsw.ElapsedMilliseconds;
            EntityManager.Singleton.LoadFoliage(ready.Level.Foliages, AssetManager, GraphicsDevice);
            LunaLog.LogDebug($"Entities built in {lsw.ElapsedMilliseconds}ms (region {tRegion}, foliage {lsw.ElapsedMilliseconds - a0}). {AssetManager.TotalQueuedUploads} textures queued for GPU upload.");

            _uploadProgress = new LoadingProgress(LM.Get("GUI_LoadLevelModal_UploadingTextures"), (uint)Math.Max(1, AssetManager.TotalQueuedUploads), true);
            GetFirstFrame<LoadingModal>()?.AddProgress(_uploadProgress);
            _finalizingLevelLoad = true;
        }

        if (!_finalizingLevelLoad || AssetManager is null) return;

        if (AssetManager.HasPendingUploads)
        {
            // GraphicsDevice.UpdateTexture work, spread across as many AfterUpdate calls as it takes,
            // bounded per call so the window keeps pumping events instead of appearing to hang.
            var uploadSw = System.Diagnostics.Stopwatch.StartNew();
            while (AssetManager.HasPendingUploads && uploadSw.Elapsed.TotalMilliseconds < UploadBudgetMs)
                AssetManager.UploadOnePendingTexture();

            if (_uploadProgress != null)
                _uploadProgress.current = (uint)(AssetManager.TotalQueuedUploads - AssetManager.PendingUploadCount);

            if (AssetManager.HasPendingUploads) return; // more queued - resume next frame
        }

        _finalizingLevelLoad = false;
        _uploadProgress = null;

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
        // Unconditional, every frame - NOT called from inside RenderMenuBar's "View" BeginMenu block
        // (where the popup used to be drawn from). See its own doc comment for why: a modal popup
        // has to be drawn regardless of whether the menu that triggered it is still open, or it can
        // never actually appear once that menu closes (which happens the instant its MenuItem is
        // clicked).
        ViewMenuDraw.RenderSaveLayoutPopup();

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

        // Cached rather than recomputed elsewhere: ImGui.GetID(str) hashes against the CURRENT
        // window's ID stack, so calling ImGui.GetID("dockspace") from anywhere other than right here
        // (inside this "dockspace" window's own Begin/End scope) produces a completely different
        // number - which is exactly what ViewMenuDraw.LayoutPresets used to do from inside the menu
        // bar's own ID scope, silently operating DockBuilder on an unrelated, nonexistent node every
        // time a preset button was clicked. See DockspaceId below.
        uint dockspaceId = ImGui.GetID("dockspace");
        DockspaceId = dockspaceId;
        ImGui.DockSpace(dockspaceId, Vector2.Zero, dockspaceFlags);

        var frameNames = openFrames.Select(f => f.WindowId).ToList();
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
            ViewMenuDraw.ShowLevelData();
            ViewMenuDraw.ShowView3D();
            ViewMenuDraw.ShowAssetViewer();
            ViewMenuDraw.ShowTextureExplorer();
            ViewMenuDraw.ShowShaderBrowser();
            ViewMenuDraw.ShowEntityExplorer();
            ViewMenuDraw.ShowInstanceInspector();
            ViewMenuDraw.ShowProfiler();
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
            RenderMenuDraw.ShowFoliage();
            RenderMenuDraw.ShowVolumes();
            RenderMenuDraw.ShowBoundingSpheres();
            ImGui.Separator();
            RenderMenuDraw.ZoneVisibility();
            ImGui.Separator();
            RenderMenuDraw.ShowMobyDistanceCulling();
            ImGui.Separator();
            RenderMenuDraw.ReflectionControls();
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
        // Only DockedFrame subclasses - Modal-derived frames (LoadingModal, ExportResultModal, the
        // file/level-export dialogs...) are deliberately floating popups, not workspace panels, and
        // forcing them into the dockspace would just be wrong.
        if (frame is DockedFrame)
            DockNewlyOpenedFrame(frame);
        OnFrameAdded?.Invoke(frame);
    }

    /// <summary>Places a freshly-opened panel somewhere sane instead of leaving it floating at
    /// whatever position ImGui defaults an undocked window to - either back where this frame TYPE
    /// was the last time one of it was open this session (_lastFrameDockIds), or failing that, into
    /// the dockspace's central node (the same "main content" area View3D/AssetViewer/etc. already
    /// tab into by default - see DockspaceLayoutManager.ApplyDefaultLayout). The central node is
    /// looked up live via DockBuilderGetNode rather than cached, since PassthruCentralNode keeps
    /// exactly one node flagged central even as the user resizes/splits things further, so this
    /// stays correct without this class needing to track split ratios itself.
    ///
    /// A no-op before any dockspace exists yet (DockspaceId == 0, e.g. the AddFrame calls Init()
    /// makes before RenderDockSpace has ever run) - those frames are handled by
    /// DockspaceLayoutManager's own startup path (TryApplyLayout/TryLoadLayout) instead.</summary>
    private unsafe void DockNewlyOpenedFrame(Frame frame)
    {
        uint targetId = 0;
        if (_lastFrameDockIds.TryGetValue(frame.GetType().Name, out uint lastId) && ImGuiP.DockBuilderGetNode(lastId).Handle != null)
        {
            targetId = lastId;
        }
        else if (DockspaceId != 0)
        {
            var root = ImGuiP.DockBuilderGetNode(DockspaceId);
            if (root.Handle != null && root.CentralNode.Handle != null)
                targetId = root.CentralNode.ID;
        }

        if (targetId != 0)
            ImGuiP.DockBuilderDockWindow(frame.WindowId, targetId);
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

    /// <summary>Populates _lastFrameDockIds for every frame about to be removed by RemoveAll
    /// (FrameMustClose) right after this, so DockNewlyOpenedFrame can put the next one of that type
    /// back where this one was. Only records an actually-docked DockId (0 means floating - nothing
    /// worth remembering there).</summary>
    private unsafe void RememberDockIdsBeforeClosing()
    {
        foreach (var frame in openFrames)
        {
            if (frame.isOpen || frame is not DockedFrame) continue;
            var win = ImGuiP.FindWindowByName(frame.WindowId);
            if (win.Handle == null) continue;
            uint dockId = win.DockId;
            if (dockId == 0) continue;
            _lastFrameDockIds[frame.GetType().Name] = dockId;
        }
    }

    protected virtual void Update(double deltaTime)
    {
        Entity.EntitiesRenderedThisFrame = 0;

        // EntityManager is engine-layer and deliberately doesn't read Program.Settings, so the
        // persisted setting is pushed in here every frame instead of being read where it's consumed.
        EntityManager.Singleton.FrustumCullingEnabled = EditorSettings.FrustrumCulling;
        // Lighting is pushed straight to the renderer every frame (View3D passes
        // EditorSettings.EnableLighting to VulkanRenderer.Frame), and backface culling is baked into
        // the renderer's pipelines, so neither goes through the asset manager.
        AssetManager?.SetTextureFiltering(EditorSettings.TextureFiltering);

        // Captured here rather than at the point isOpen flips false (TryCloseFirstFrame, or the
        // window's own tab close button mutating it directly via Begin's ref isOpen) so this covers
        // BOTH close paths uniformly - they both just set the flag and let removal happen here.
        // One frame later than the close itself, but the window's settings entry (and DockId) is
        // still live at that point; ImGui doesn't tear it down just because Begin() stopped being
        // called for it.
        RememberDockIdsBeforeClosing();
        openFrames.RemoveAll(FrameMustClose);

        if (Overlay.showOverlay)
            Overlay.DrawOverlay(Overlay.showOverlay);

        RenderUI(deltaTime);
    }

    protected virtual void AfterUpdate()
    {
        FlushPendingLevelWipe();
        DoLoadEntitiesCheck();
        DoExportCompletionsCheck();
    }

    protected virtual void FixedUpdate() { }

    protected virtual void Draw(GraphicsDevice graphicsDevice, CommandList commandList)
    {
        using (FrameProfiler.Sample("ImGui Render"))
        {
            commandList.Begin();
            commandList.SetFramebuffer(FullScreenTexture.Framebuffer);
            commandList.ClearColorTarget(0, new RgbaFloat(0.1f, 0.1f, 0.1f, 1.0f));
            commandList.ClearDepthStencil(1.0f);

            imGuiController.Render(graphicsDevice, commandList);

            commandList.End();
            graphicsDevice.SubmitCommands(commandList);
        }

        using (FrameProfiler.Sample("Composite"))
        {
            commandList.Begin();

            FullScreenTexture.Resolve(commandList);

            commandList.SetFramebuffer(graphicsDevice.SwapchainFramebuffer);
            commandList.ClearColorTarget(0, new RgbaFloat(0.1f, 0.1f, 0.1f, 1.0f));

            FullScreenRenderer.Draw(commandList, FullScreenTexture.ResolveTextureView, graphicsDevice.SwapchainFramebuffer.OutputDescription);

            commandList.End();
            graphicsDevice.SubmitCommands(commandList);
        }
        // NeoVeldrid's Vulkan backend only signals a render-finished semaphore before presenting
        // when the present queue differs from the graphics queue - on a shared queue (the common
        // case on desktop GPUs), SwapBuffers's vkQueuePresentKHR call waits on nothing at all, so
        // this WaitForIdle is required to keep the presentation engine from reading the swapchain
        // image before the GPU has finished writing it.
        //
        // This is also the frame's most diagnostic number: its duration is the GPU tail (see
        // FrameProfiler's class summary). If this phase dominates, the bottleneck is the GPU or
        // this forced full sync, not CPU submission.
        using (FrameProfiler.Sample(FrameProfiler.GpuWaitPhase))
            graphicsDevice.WaitForIdle();
        using (FrameProfiler.Sample(FrameProfiler.PresentPhase))
            graphicsDevice.SwapBuffers();

        // The 3D scene goes to the GPU here, AFTER the present and the device wait above, so it runs
        // while the CPU pumps events and builds the next frame. Recorded during Update; see
        // View3D.SubmitScene and VulkanRenderer.SubmitFrame.
        foreach (var view in openFrames.OfType<View3D>())
            view.SubmitScene();
    }

    protected virtual void OnClose()
    {
        // Captured before the layout save below so both land in the same file write. Only
        // overwrites the stored windowed size while NOT maximized - SDL reports the maximized
        // (screen-filling) size while maximized, not a size worth restoring to, so saving that
        // would make "un-maximize" always land at the screen size instead of whatever windowed
        // size the user actually had before maximizing (or the default, if they never un-maximized
        // this session at all).
        EditorSettings.WindowMaximized = MainWindow.IsMaximized;
        if (!EditorSettings.WindowMaximized)
        {
            var (w, h) = MainWindow.GetWindowSize();
            EditorSettings.WindowWidth = w;
            EditorSettings.WindowHeight = h;
        }

        // Persist whatever the user ended the session with, so tweaks made without an explicit
        // "Save Layout As..." (dragging/resizing a panel) aren't lost on the next launch.
        DockspaceLayoutManager.SaveActiveLayout(EditorSettings, openFrames);
    }

    private void OnResize(int width, int height)
    {
        imGuiController.Resize(width, height);
        GraphicsDevice.MainSwapchain.Resize((uint)width, (uint)height);
        // The blit caches a resource set per texture view, and Resize replaces the view it was built
        // from, so the old one has to be dropped before it is freed underneath the cache.
        FullScreenRenderer.Invalidate(FullScreenTexture.ResolveTextureView);
        FullScreenTexture.Resize((uint)width, (uint)height);
    }

    public int GetTargetFPS() => (int)(1.0 / fixedUpdateTimeStep);

    public void SetTargetFPS(int fps)
    {
        fixedFrameRate = fps == 0 ? double.MaxValue : 1.0 / fps;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        FullScreenRenderer?.Dispose();
        FullScreenTexture?.Dispose();
        Input.Destroy();
        MainWindow.Dispose();
        GraphicsDevice.Dispose();
    }
}
