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

    /// <summary>The open level's reader, kept so another of its regions can be read over the same files
    /// (see <see cref="SwitchRegion"/>).</summary>
    private LevelReader? _levelReader;

    /// <summary>True while a level or region is being read, built or uploaded: no region switch then.</summary>
    public bool IsLoadingLevel => _loadBar != null || _sceneBuild != null || _buildingAssetManager != null || _finalizingLevelLoad;

    /// <summary>The level and its background-decoded textures, handed from LoadLevelDataAsync's
    /// background task to <see cref="DoLoadEntitiesCheck"/> on the main thread - queued (same pattern
    /// as <see cref="pendingExportCompletions"/>) so both arrive as one atomic message.</summary>
    private readonly ConcurrentQueue<LevelReady> pendingLevelReady = new();

    private readonly record struct LevelReady(
        int Generation,
        LevelData Level,
        Dictionary<ulong, ReLunacy.Engine.Rendering.Resources.TextureLevels?> PreparedTextures,
        Engine.Loading.PreparedGeometry PreparedGeometry,
        Engine.Rendering.Vulkan.TextureBudgetReport? TextureBudget);

    /// <summary>Which load is current. Bumped by every load and every wipe, so a loading task that
    /// finishes after its level was closed - or after another level was asked for - can be told
    /// apart from the one being waited on, and its result dropped instead of built.</summary>
    private int _loadGeneration;

    /// <summary>Background export tasks (see AssetViewer) can only touch <see cref="openFrames"/>
    /// from the main thread, same rule as the rest of this class - so completions are queued here
    /// (ConcurrentQueue needs no external locking) and drained on the main thread each frame by
    /// <see cref="DoExportCompletionsCheck"/>.</summary>
    private readonly ConcurrentQueue<ExportCompletion> pendingExportCompletions = new();

    /// <summary>The loading frame belonging to the level load currently running, if any. See
    /// LoadLevelDataAsync for why this is a field rather than an openFrames lookup.</summary>
    private LoadingFrame? _activeLoadingFrame;

    /// <summary>Work a background job wants done on the main thread - the completion of an archive
    /// save going back into the panel that started it, say. Same rule and same pattern as
    /// <see cref="pendingExportCompletions"/>: nothing off the main thread may touch
    /// <see cref="openFrames"/> or anything else ImGui is reading.</summary>
    private readonly ConcurrentQueue<Action> pendingMainThreadWork = new();

    public readonly record struct ExportCompletion(LoadingFrame ProgressModal, bool Success, string Message, string Directory);

    public void QueueExportCompletion(ExportCompletion completion) => pendingExportCompletions.Enqueue(completion);

    /// <summary>Queues work to run on the main thread at the end of the next frame. Safe from any
    /// thread; see <see cref="Utility.BackgroundJob"/>, which is what hands completions back.</summary>
    public void QueueOnMainThread(Action work) => pendingMainThreadWork.Enqueue(work);

    private void DoMainThreadWorkCheck()
    {
        while (pendingMainThreadWork.TryDequeue(out var work))
        {
            // One job's completion throwing must not take the drain - or the frame - down with it.
            try { work(); }
            catch (Exception e) { LunaLog.LogError($"Queued main-thread work failed: {e}"); }
        }
    }

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
        // The editor's camera model, drawn at every camera controller (VulkanRenderer.CameraMarkers).
        try
        {
            Engine.Rendering.Vulkan.VulkanRenderer.CameraModel =
                Engine.Rendering.MarkerModel.LoadObj(Path.Combine(Program.EditorPath, "Assets", "ReLunacyCamera.obj"));
        }
        catch (Exception ex)
        {
            LunaLog.LogWarn($"The camera model could not be loaded, so cameras are not drawn: {ex.Message}");
        }
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
        em.renderCameras = !EditorSettings.HideCameras;
        em.renderPaths = !EditorSettings.HidePaths;
        em.renderNavmesh = EditorSettings.RenderNavmesh;
        // The RSX shaders only apply while lit (see View3D, which keeps this in step afterwards); set
        // before the first scene is built so it is not built once with the wrong set and rebuilt.
        Engine.Rendering.Vulkan.VulkanRenderer.UseRetailEmbeddedShaders = EditorSettings.EnableLighting && EditorSettings.GameShaders;
        em.renderCollision = EditorSettings.RenderCollision;
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

    /// <summary>Steps on the level load's bar. A thousand rather than a hundred so a long phase's
    /// item counts still move it visibly - the window is wide enough to show them.</summary>
    private const uint LoadBarSteps = 1000;

    public async void LoadLevelDataAsync(string path, LoadingFrame? loadingFrame = null, string? debugDatPath = null)
    {
        // Held for the rest of the load rather than looked up in openFrames when needed: the frame
        // can be hidden or closed while the work runs, and a lookup would then come back empty and
        // strand the load - no texture-upload bar, and loadingFinished never set, which would leave
        // the task sitting in the status bar for ever.
        _activeLoadingFrame = loadingFrame;
        TryWipeLevel();
        LunaLog.LogInfo($"Loading level {path.Split(Path.DirectorySeparatorChar)[^1]}.");
        Program.ProvidedPath = path;

        // The game's own fragment shaders live in its EBOOT: a decrypted one (*.elf) in this level's
        // install is read when the scene is built (NewEngineStandardShaders, TodTieShaders).
        Engine.Loading.Shaders.NewEngineStandardShaders.SearchRoots = [(path, null)];

        // ONE progress for the whole load, mutated in place by whichever thread is reporting - the
        // phases hand it a fraction of the whole job rather than each owning a bar of its own, so
        // what the user watches is a single bar that crosses the load from the file open to the last
        // texture upload. The labels are resolved here, on the main thread, for the reason
        // PhaseLabels documents.
        var labels = PhaseLabels<Engine.Loading.LoadPhase>.Snapshot("LoadPhase");
        var bar = new LoadingProgress(labels.Label(Engine.Loading.LoadPhase.OpeningFiles), LoadBarSteps, true);
        _loadBar = bar;
        // Registered either way, so a load started without a window still shows in the status bar.
        if (loadingFrame is not null) loadingFrame.UpdateProgress(0, bar);
        else BackgroundTasks.Begin(bar);

        int generation = ++_loadGeneration;

        var progress = new Engine.Loading.LevelLoadProgress(
            report => labels.Apply(bar, report.Phase, report.Overall, report.Done, report.Total));
        _loadProgress = progress;

        try
        {
            await Task.Run(() =>
            {
                // Opening the archive is a phase like any other: on a packed level it is the better
                // part of a second, and the bar used to sit at zero through all of it.
                progress.Begin(Engine.Loading.LoadPhase.OpeningFiles);
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
                progress.CompletePhase();

                var levelReader = new LevelReader(fileManager);
                Level = levelReader.LoadLevel(progress);
                _levelReader = levelReader;
                PrepareLevel(Level, progress, generation);
            });
        }
        catch (Exception ex)
        {
            // A load that threw used to leave its bar in the status bar for ever and the window
            // sitting at whatever percentage it had reached. Nothing here touches openFrames: this
            // continuation runs on a pool thread (there is no synchronization context), and only the
            // task registry and the frame's own finished flag are safe from one.
            LunaLog.LogError($"Level load failed: {ex}");
            BackgroundTasks.End(bar);
            if (loadingFrame is not null)
            {
                loadingFrame.LoadEnd = DateTime.Now;
                loadingFrame.loadingFinished = true;
            }
            return;
        }

        LunaLog.LogDebug("Level loaded.");
    }

    /// <summary>
    /// Shows another region of the open level. The current one's scene, GPU models and textures are
    /// freed and the new region is read from the level's files, which stay open: only its zones,
    /// instances, environment, collision and the classes it places are read, and only their textures
    /// are decoded and uploaded. A level is shown one region at a time, so a GPU only has to hold one.
    /// </summary>
    public async void SwitchRegion(string regionName)
    {
        if (_levelReader is not { } reader || Level is null || IsLoadingLevel) return;
        if (Level.Region?.Name == regionName) return;

        TryWipeLevel(keepFiles: true);
        LunaLog.LogInfo($"Loading region {regionName}.");

        var labels = PhaseLabels<Engine.Loading.LoadPhase>.Snapshot("LoadPhase");
        var bar = new LoadingProgress(labels.Label(Engine.Loading.LoadPhase.Zones), LoadBarSteps, true);
        _loadBar = bar;
        BackgroundTasks.Begin(bar);
        int generation = ++_loadGeneration;
        var progress = new Engine.Loading.LevelLoadProgress(
            report => labels.Apply(bar, report.Phase, report.Overall, report.Done, report.Total));
        _loadProgress = progress;

        try
        {
            await Task.Run(() =>
            {
                Level = reader.LoadRegion(regionName, progress);
                PrepareLevel(Level, progress, generation);
            });
        }
        catch (Exception ex)
        {
            LunaLog.LogError($"Region load failed: {ex}");
            BackgroundTasks.End(bar);
        }
    }

    /// <summary>The loading task's tail, shared by a level load and a region switch: everything the main
    /// thread would otherwise do that does not need the graphics device, then the hand-off to it.</summary>
    private void PrepareLevel(LevelData level, Engine.Loading.LevelLoadProgress progress, int generation)
    {
        // Everything the asset build and the scene entities would otherwise do on the main
        // thread that does NOT need the graphics device, done here instead: packing every
        // mesh and UFrag into vertex/index/interleaved arrays, then decoding every texture
        // and building its mip chain. Both are internally parallel across cores; they run
        // one after the other rather than at the same time because each already saturates
        // them, and sequencing keeps the bar's phases - and the peak memory - honest.
        progress.Begin(Engine.Loading.LoadPhase.PreparingGeometry);
        var geometry = Engine.Loading.PreparedGeometry.Build(level, progress);

        progress.Begin(Engine.Loading.LoadPhase.DecodingTextures);
        var swPrep = System.Diagnostics.Stopwatch.StartNew();
        // Fitted to what the GPU has: a level whose textures would overflow device memory gets them
        // capped here rather than failing inside the driver halfway through the upload.
        var budget = Engine.Rendering.Vulkan.GpuMemoryBudget.Query(GraphicsDevice);
        Engine.Rendering.Vulkan.TextureBudgetReport? report = null;
        Dictionary<ulong, ReLunacy.Engine.Rendering.Resources.TextureLevels?> prepared;
        if (budget is not { } known)
            prepared = AssetManager.PrepareTextures(level, progress);
        else
        {
            prepared = AssetManager.PrepareTextures(level, progress, known.TextureAllowance, out var fitted);
            report = fitted with { Budget = known };
            LunaLog.LogInfo($"Textures: {fitted.FullBytes >> 20} MiB at full resolution, {known.TextureAllowance >> 20} MiB allowed " +
                $"(GPU {known.DeviceName}: heap {known.HeapBytes >> 20} MiB, budget {known.BudgetBytes >> 20} MiB{(known.FromBudgetExtension ? " from VK_EXT_memory_budget" : "")}).");
        }
        LunaLog.LogDebug($"Decoded {prepared.Count} textures in {swPrep.ElapsedMilliseconds}ms (loading task, parallel).");

        pendingLevelReady.Enqueue(new LevelReady(generation, level, prepared, geometry, report));
    }

    /// <summary>Applies a user-picked debug.dat to the currently loaded level by reloading it -
    /// the reload runs every name through the exact same DebugReader path a normal load does,
    /// rather than trying to retroactively patch names onto already-built entities.</summary>
    public void LoadExternalDebugDatAndReload(string debugDatPath, LoadingFrame? loadingFrame = null)
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
    /// <param name="keepFiles">For a region switch: the level's files and reader stay open for the next
    /// region to be read from.</param>
    public void TryWipeLevel(bool keepFiles = false)
    {
        // A level whose scene is still being built has no AssetManager published yet, so the guard
        // below would walk straight past it and leave its half-built entities, its GPU textures and
        // a job still stepping over a level that is being thrown away. Dropped first, before
        // anything else, and whatever the rest of this method decides to do.
        AbandonLevelFinalization();
        // Anything the loading task finished but that was never picked up belongs to the level being
        // wiped, not to the one about to load - and a task still running is now a stale generation,
        // so whatever it enqueues later is dropped rather than built over the next level.
        pendingLevelReady.Clear();
        _loadGeneration++;
        // Likewise the previous load's bar: this runs before the guards below, so a load wiped while
        // it was still reading files stops being listed as running either way.
        if (_loadBar != null) BackgroundTasks.End(_loadBar);
        _loadBar = null;
        _loadProgress = null;

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
        Level = null;
        AssetManager = null;
        if (!keepFiles)
        {
            fileManager.Dispose();
            fileManager = null;
            _levelReader = null;
            Program.ProvidedPath = string.Empty;
        }
        // In case this level was wiped mid-load, while DoLoadEntitiesCheck was still draining its
        // queued texture uploads.
        _finalizingLevelLoad = false;
        if (_uploadProgress != null) BackgroundTasks.End(_uploadProgress);
        _uploadProgress = null;
    }

    /// <summary>Drops a scene build in flight and frees what it had already allocated. The GPU-side
    /// disposal goes through the same deferred path a normal wipe uses where that slot is free;
    /// where it is not (a second load in the same frame) the build's resources are freed directly,
    /// which is safe precisely because they were never published - nothing has drawn with them.</summary>
    private void AbandonLevelFinalization()
    {
        if (_sceneBuild is null && _buildingAssetManager is null) return;

        _sceneBuild = null;
        var abandoned = _buildingAssetManager;
        _buildingAssetManager = null;
        if (abandoned is null) return;

        if (_pendingWipeAssetManager is null)
            _pendingWipeAssetManager = abandoned;
        else
        {
            abandoned.DisposeSceneRenderer();
            abandoned.Dispose();
        }
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

    /// <summary>The scene build in flight, if any: the entities of one level, built a few steps per
    /// frame (see <see cref="Engine.Scene.SceneBuildJob"/>).</summary>
    private Engine.Scene.SceneBuildJob? _sceneBuild;

    /// <summary>The level's AssetManager while its scene is still being built. Deliberately NOT yet
    /// published to <see cref="AssetManager"/>: View3D captures the whole scene the first frame it
    /// sees an asset manager with nothing left to upload, and capturing a half-built level would
    /// retain exactly that - a level missing most of its zones, with nothing to make it rebuild.</summary>
    private AssetManager? _buildingAssetManager;

    /// <summary>The level load's single bar, and the phase-weighted reporter writing to it. Held for
    /// the whole load because the last phases - the asset build, the entity build and the uploads -
    /// happen here, on the main thread, frames after the loading task has finished.</summary>
    private LoadingProgress? _loadBar;
    private Engine.Loading.LevelLoadProgress? _loadProgress;

    /// <summary>Per-frame time budget for building scene entities, the same idea as
    /// <see cref="UploadBudgetMs"/>: it runs in AfterUpdate, after this frame's Draw, so overrunning
    /// slightly delays the next frame rather than corrupting this one. A zone is the unit of work and
    /// cannot be split, so a single very large zone can still overshoot this.</summary>
    private const double BuildBudgetMs = 12.0;
    // The upload phase's own progress bar slot on the loading modal - kept as a direct reference so
    // each frame can just mutate .current instead of reconstructing/relocking through UpdateProgress.
    private LoadingProgress? _uploadProgress;

    // Per-frame time budget for draining queued texture uploads (see AssetManager.UploadPendingTextures).
    // A time budget rather than a count, since texture sizes vary hugely (a 4K atlas vs a 32x32 icon).
    // Runs inside AfterUpdate, after this frame's Draw, so overrunning slightly delays next frame's
    // Present rather than corrupting this one.
    private const double UploadBudgetMs = 20.0;

    /// <summary>Keeps the uploaded environment on whichever region is being rendered. On NewEngine+ the
    /// cubemap, lighting, fog and sky belong to the region rather than the level (Luminopolis has four
    /// distinct cubemap reference sets across its eight regions), so showing one region's geometry
    /// under another's environment is simply wrong - and it was, for every region but the first.
    ///
    /// The first ENABLED region wins. Exactly one is visible by default, which makes that
    /// unambiguous; with several ticked the earliest in load order supplies the environment.</summary>
    private void FollowActiveRegionEnvironment()
    {
        if (AssetManager is null || Level is null) return;
        var region = EntityManager.Singleton.Regions.FirstOrDefault(r => r.allowRender);
        if (region is null || region.RegionName == AssetManager.ActiveEnvironmentRegion) return;

        // The renderer takes the lighting environment at construction, so the scene has to be
        // rebuilt for a change to reach it. Only the lighting switches - the cube array is the union
        // of every region's resources and stays bound.
        if (AssetManager.SetActiveEnvironmentRegion(Level, region.RegionName))
            AssetManager.InvalidateSceneRenderer();
    }

    /// <summary>Warns when a new-engine level's own install holds no decrypted EBOOT: without one the
    /// retail shader table is read from whichever sibling game has one, which depends on what else is
    /// installed beside the level and on the path it was browsed through (a symlinked install has
    /// different neighbours than its target). The substituted table draws, but with the wrong lighting,
    /// reflections and transparency.</summary>
    // The AssetManager whose GPU memory failure was already shown: one dialog per level, not per texture.
    private AssetManager? _gpuMemoryFailureShownFor;

    /// <summary>Tells the user, once per level, that a texture could not get GPU memory. It is drawn with
    /// the default texture instead; the dialog says how much was already allocated against the budget.</summary>
    private void ReportGpuMemoryFailure()
    {
        var assets = _buildingAssetManager ?? AssetManager;
        if (assets?.GpuMemoryFailure is not { } failure || ReferenceEquals(_gpuMemoryFailureShownFor, assets)) return;
        _gpuMemoryFailureShownFor = assets;
        LunaLog.LogError($"Out of GPU memory: {failure}");
        AddFrame(new GpuMemoryModal(failure, Engine.Rendering.Vulkan.GpuMemoryBudget.Query(GraphicsDevice)));
    }

    private void WarnIfEbootMissing(LevelData level)
    {
        if (level.IsOldEngine) return;
        if (Engine.Loading.Shaders.NewEngineStandardShaders.LastResolution is not { FromOwnInstall: false } resolution) return;

        LunaLog.LogWarn(resolution.Path == null
            ? "No decrypted EBOOT found for this level: the renderer keeps its generic shading."
            : $"This level's install has no decrypted EBOOT; the retail shader table came from '{resolution.Path}'.");
        AddFrame(new MissingEbootModal(resolution.Path));
    }

    /// <summary>Builds the level's renderer-side assets and starts its scene build. The asset build
    /// is one indivisible step - it is a constructor, and it allocates the GPU textures every
    /// material needs - so this is the one part of the load that can still be felt as a hitch; the
    /// vertex packing it used to do on top of that is already done by here.</summary>
    private void BeginLevelFinalization(LevelReady ready)
    {
        var lsw = System.Diagnostics.Stopwatch.StartNew();
        _buildingAssetManager = new AssetManager(
            ready.Level, GraphicsDevice, ready.PreparedTextures, ready.PreparedGeometry, _loadProgress,
            ready.TextureBudget?.MinDimension ?? 0);
        if (ready.TextureBudget is { } budget) _buildingAssetManager.TextureAllowance = budget.Budget.TextureAllowance;
        _buildingAssetManager.TextureRetired += imGuiController.ReleaseImGuiBinding;
        if (ready.TextureBudget is { Reduced: true } reduced)
        {
            LunaLog.LogWarn($"This level's textures need {reduced.FullBytes >> 20} MiB at full resolution but the GPU allows about " +
                $"{reduced.Budget.TextureAllowance >> 20} MiB: {reduced.ReducedCount} of {reduced.TextureCount} textures were reduced to " +
                $"{reduced.MinDimension}-{reduced.MaxDimension} px ({reduced.FittedBytes >> 20} MiB).");
            AddFrame(new GpuMemoryModal(reduced));
        }
        ReportGpuMemoryFailure();
        // AssetManager's constructor is what resolves the retail shader table, so its provenance is
        // known by here. A borrowed or absent EBOOT still renders - wrongly - so say so plainly
        // rather than leaving it to look like a renderer bug.
        WarnIfEbootMissing(ready.Level);
        LunaLog.LogDebug($"Assets built in {lsw.ElapsedMilliseconds}ms; building scene entities.");

        _sceneBuild = new Engine.Scene.SceneBuildJob(ready.Level, _buildingAssetManager, GraphicsDevice, _loadProgress);
    }

    /// <summary>Spends this frame's budget on the scene build. Returns true once the scene is
    /// complete and the level has been published, which is when the caller may carry on into the
    /// upload drain and the finalization tail.</summary>
    private bool StepLevelFinalization()
    {
        if (_sceneBuild is null || _buildingAssetManager is null) return false;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed.TotalMilliseconds < BuildBudgetMs)
            if (!_sceneBuild.Step()) break;

        if (!_sceneBuild.IsFinished) return false; // resume next frame

        // One region is loaded at a time (LevelReader.LoadRegion picks the level's default one and
        // Render > Region switches), so whatever was built is shown.
        foreach (var region in EntityManager.Singleton.Regions)
            region.allowRender = true;

        // Published only now that every region and zone exists - see _buildingAssetManager.
        AssetManager = _buildingAssetManager;
        _buildingAssetManager = null;
        _sceneBuild = null;

        LunaLog.LogDebug($"Scene entities built ({EntityManager.Singleton.ZonesCount} zone(s), " +
            $"{EntityManager.Singleton.UFragsCount} UFrag(s), {EntityManager.Singleton.MobysCount} Moby instance(s)). " +
            $"{AssetManager.TotalQueuedUploads} textures queued for GPU upload.");
        // Anything left means the pre-pass packed geometry the build never asked for, i.e. the two
        // walks have drifted apart - worth saying, since the leftovers are pure wasted work.
        if (AssetManager.PreparedGeometry is { Count: > 0 } leftover)
            LunaLog.LogDebug($"{leftover.Count} prepared geometr(ies) went unused by the build.");

        _loadProgress?.Begin(Engine.Loading.LoadPhase.UploadingTextures, AssetManager.TotalQueuedUploads);
        _uploadProgress = new LoadingProgress(LM.Get("GUI_LoadLevelModal_UploadingTextures"), (uint)Math.Max(1, AssetManager.TotalQueuedUploads), true);
        // Registered whether or not a loading frame is up, so uploads show in the status bar
        // even when the level was loaded without one or its window has been dismissed.
        BackgroundTasks.Begin(_uploadProgress);
        _activeLoadingFrame?.AddProgress(_uploadProgress);
        _finalizingLevelLoad = true;
        return true;
    }

    /// <summary>
    /// The main thread's share of a level load, driven once per frame from AfterUpdate: build the
    /// asset manager, then the scene entities a few steps at a time, then drain the texture uploads a
    /// few milliseconds at a time, then finalize.
    ///
    /// All of it is here rather than threaded because all of it talks to the graphics device or to
    /// the renderer's static capture registry (see <see cref="Engine.Scene.SceneBuildJob"/>). What
    /// CAN be threaded was already done on the loading task before this is ever reached - the
    /// vertex packing and the texture decode, which is where the seconds were.
    /// </summary>
    private void DoLoadEntitiesCheck()
    {
        // One level at a time: a second load's data is not touched until the first has published.
        while (_sceneBuild is null && _buildingAssetManager is null && pendingLevelReady.TryDequeue(out var ready))
        {
            if (ready.Generation != _loadGeneration)
            {
                LunaLog.LogDebug("Dropped a finished level load: its level was closed or replaced while it ran.");
                continue;
            }
            BeginLevelFinalization(ready);
        }

        if (_sceneBuild is not null && !StepLevelFinalization()) return;

        if (AssetManager is null) return;

        // Draining is NOT tied to _finalizingLevelLoad: textures are also requested long after a level
        // has finished loading. The Sky builds its own the first time View3D calls PrepareScene, which
        // happens after finalization has already cleared the flag - so while this was gated on it, those
        // uploads sat in the queue forever, PrepareScene saw HasPendingUploads on every frame, and the
        // sky packet was never built ("Sky: not drawn - waiting on texture uploads", then silence).
        // Anything queued later (asset previews, lazily built materials) was stuck the same way.
        if (AssetManager.HasPendingUploads)
        {
            // One batch of uploads per frame, spread across as many AfterUpdate calls as it takes,
            // bounded per call so the window keeps pumping events instead of appearing to hang.
            AssetManager.UploadPendingTextures(UploadBudgetMs);
            ReportGpuMemoryFailure();

            // Only meaningful while the loading modal is up; null once the level has finalized.
            int uploaded = AssetManager.TotalQueuedUploads - AssetManager.PendingUploadCount;
            if (_uploadProgress != null)
                _uploadProgress.current = (uint)uploaded;
            // The overall bar's last phase. Set rather than advanced, since the queue is also fed
            // after a load (lazily built materials, the sky, asset previews).
            if (_finalizingLevelLoad) _loadProgress?.SetDone(uploaded);

            if (AssetManager.HasPendingUploads) return; // more queued - resume next frame
        }

        // Everything below is the one-shot level-finalization tail.
        if (!_finalizingLevelLoad) return;

        _finalizingLevelLoad = false;
        if (_uploadProgress != null) BackgroundTasks.End(_uploadProgress);
        _uploadProgress = null;

        // The bar reaches 100% here and nowhere earlier: the uploads were its last phase.
        _loadProgress?.Finish();
        if (_loadProgress is not null)
            LunaLog.LogDebug($"Level load phases: {_loadProgress.Summary()}.");
        if (_loadBar != null) BackgroundTasks.End(_loadBar);
        _loadBar = null;
        _loadProgress = null;

        foreach (var listener in openFrames.OfType<ILevelListener>())
            listener.OnLevelLoaded();

        var loadFrame = _activeLoadingFrame;
        _activeLoadingFrame = null;

        var region = EntityManager.Singleton.Regions.FirstOrDefault();
        var firstMoby = region?.MobyInstances.Entities.FirstOrDefault();
        if (firstMoby != null)
            SelectionManager.Singleton.Select(firstMoby);

        if (loadFrame is null) return;
        loadFrame.LoadEnd = DateTime.Now;
        // Setting this is what takes the load out of the status bar, so it happens even if the
        // frame's window was hidden or closed long ago.
        loadFrame.loadingFinished = true;
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

        // Before the dockspace: a viewport side bar takes its height out of the work area, and the
        // dockspace sizes itself from that work area a few lines below. Drawn after, it would
        // overlap the docked frames instead of making room.
        Frames.StatusBar.Render();

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
        FileMenuDraw.SaveLevelShortcut();
        if (!ImGui.BeginMainMenuBar()) return;

        if (ImGui.BeginMenu(LM.Get("GUI_Menu_File")))
        {
            FileMenuDraw.OpenLevelMenuItem();
            FileMenuDraw.OpenGameBrowserMenuItem();
            FileMenuDraw.SaveLevelMenuItem();
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
            ViewMenuDraw.ShowCubemapViewer();
            ViewMenuDraw.ShowShaderBrowser();
            ViewMenuDraw.ShowEntityExplorer();
            ViewMenuDraw.ShowInstanceInspector();
            ViewMenuDraw.ShowProfiler();
            ViewMenuDraw.ShowConsoleFrame();
            ImGui.Separator();
            ViewMenuDraw.ShowPSArcExplorer();
            ViewMenuDraw.ShowLuaEnvironment();
            ImGui.Separator();
            ViewMenuDraw.LayoutPresets();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu(LM.Get("GUI_Menu_Render")))
        {
            // Mobys, Ties, UFrags, volumes and bounding spheres are toggled from the 3D viewport's overlay.
            RenderMenuDraw.ShowFoliage();
            ImGui.Separator();
            RenderMenuDraw.RegionSelection();
            RenderMenuDraw.ZoneVisibility();
            RenderMenuDraw.SkyVisibility();
            ImGui.Separator();
            // Old engine only, and it draws its own separator after it.
            RenderMenuDraw.GameFidelity();
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
        // Only DockedFrame subclasses - the floating frames (LoadingFrame, ExportResultModal, the
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
        // Lighting and backface culling are pushed straight to the renderer every frame (View3D
        // passes EditorSettings.EnableLighting to VulkanRenderer.Frame and assigns
        // RetailBackfaceCulling), so neither goes through the asset manager.
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
        FollowActiveRegionEnvironment();
        // The post-Draw safe point FlushSceneRendererInvalidation documents: this frame's draw is
        // submitted, so tearing the captured scene down here lets the next Update rebuild it from
        // the current entity state. Nothing called it before, so every InvalidateSceneRenderer -
        // the per-zone and per-region render toggles among them - only ever set a flag that was
        // read back by nothing, and the scene kept drawing whatever it was first built with.
        AssetManager?.FlushSceneRendererInvalidation();
        FlushPendingLevelWipe();
        DoLoadEntitiesCheck();
        AssetManager?.UpdateTextureStreaming();
        DoExportCompletionsCheck();
        DoMainThreadWorkCheck();
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
