using ReLunacy.Engine.Rendering;
using Vec2 = System.Numerics.Vector2;
using Vector2 = OpenTK.Mathematics.Vector2;
using Vec3 = System.Numerics.Vector3;
using Vector3 = OpenTK.Mathematics.Vector3;
using Vec4 = System.Numerics.Vector4;
using Vector4 = OpenTK.Mathematics.Vector4;
using Quaternion = OpenTK.Mathematics.Quaternion;
using Keys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;
using MouseButton = OpenTK.Windowing.GraphicsLibraryFramework.MouseButton;
using ReLunacy.Frames.ModalFrames;
using ReLunacy.Engine.EntityManagement;
using OpenTK.Windowing.Common.Input;

namespace ReLunacy;

public class Window : GameWindow
{
    public static Window? Singleton { get; private set; }
    public static string AppPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    public string oglVersionStr = "Unknown OpenGL version";
    public ImGuiController controller;
    public List<Frame> openFrames = [];
    public bool showOverlay = false;
    public bool showHoveredObject = false;
    public float framerate;
    public Vec4 screenSafeSpace;
    public Vector2 freecamLocal;
    public Renderer OGLRenderer { get; private set; }
    public ResourcesManager Resources { get; private set; }

    public AssetLoader AssetLoader { get; private set; }
    public FileManager FileManager { get; private set; }
    public Gameplay Gameplay { get; private set; }

    private bool doLoadEntities = false;

    public string[] args { get => Program.cmds; }

    public Window(GameWindowSettings gws, NativeWindowSettings nws) : base(gws, nws)
    {
        Singleton = this;
        VSync = Program.Settings.VSyncMode;
        Resources = ResourcesManager.LoadResourcesFromManifest();
    }

    protected override void OnLoad()
    {
        base.OnLoad();

        LunaLog.LogInfo("Checking for updates...");
        UpdateChecker.CheckUpdates();

        oglVersionStr = $"OpenGL {GL.GetString(StringName.Version)}";
        Title = $"{Program.AppDisplayName} {Program.Version} ({oglVersionStr})";
        var ico = Resources.GetWindowIcon("logo.ico");
        if (ico is not null)
        {
            Icon = new WindowIcon([ico]);
        }

        OGLRenderer = new Renderer();

        controller = new ImGuiController(ClientSize.X, ClientSize.Y);

        screenSafeSpace = new(0, 0, ImGui.GetMainViewport().Size.X, ImGui.GetMainViewport().Size.Y);

        LunaLog.LogInfo("Loading the OpenGL renderer.");

        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2.5f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 2.5f);
        ImGuiIOPtr io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

        AddFrame(new View3DFrame());
        AddFrame(new BasicEntityExplorer());
        AddFrame(new PropertyInspectorFrame());

        if(Program.ProvidedPath != string.Empty)
        {
            LoadLevelDataAsync(Program.ProvidedPath, new("Loading level...", new(0, 1)));
        }
    }

    protected override void OnRenderFrame(FrameEventArgs eventArgs)
    {
        base.OnRenderFrame(eventArgs);

        openFrames.RemoveAll(FrameMustClose);

        controller?.Update(this, (float)eventArgs.Time);

        if (Overlay.showOverlay)
        {
            Overlay.DrawOverlay(Overlay.showOverlay);
        }

        RenderUI((float)eventArgs.Time);

        controller?.Render();

        SwapBuffers();
    }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        if(Overlay.showOverlay)
        {
            framerate = (float)Math.Round(1 / (float)args.Time, 1);
        }

        DoLoadEntitiesCheck();

        base.OnUpdateFrame(args);
    }
    public async void LoadLevelDataAsync(string path, LoadingModal loadingFrame)
    {
        TryWipeLevel();
        AddFrame(loadingFrame);
        LunaLog.LogInfo($"Loading level {path.Split(Path.DirectorySeparatorChar)[^1]}.");
        Program.ProvidedPath = path;

        FileManager = new();
        LunaLog.LogDebug("Starting FileManager threaded task.");
        FileManager.LoadFolder(path);

        LunaLog.LogDebug("Starting AssetLoader threaded task.");
        AssetLoader = new(FileManager);
        var alTask = Task.Run(() => AssetLoader.LoadAssets());
        LunaLog.LogDebug("Awaiting for AssetLoader to finish its work...");
        await alTask;
        loadingFrame.UpdateProgress(0, new(1, 1));
        doLoadEntities = true;
        LunaLog.LogDebug("Level loaded.");
        Thread.Sleep(100);
        loadingFrame.isOpen = false;
    }

    public void TryWipeLevel()
    {
        if (Program.ProvidedPath == string.Empty || Program.ProvidedPath == null)
            return;

        EntityManager.Singleton.Wipe();
        EntityCluster.Wipe();
        AssetManager.Singleton.Wipe();
        AssetLoader = null;
        FileManager = null;
        Gameplay = null;
        Program.ProvidedPath = string.Empty;
        if(IsAnyFrameOpened<BasicEntityExplorer>())
            GetFirstFrame<BasicEntityExplorer>().Wipe();
    }

    private void DoLoadEntitiesCheck()
    {
        if (!doLoadEntities) return;
        doLoadEntities = false;

        AssetManager.Singleton.Initialize(AssetLoader);
        Gameplay = new(AssetLoader);
        LunaLog.LogDebug("Loading Gameplay into EntityManager.");
        EntityManager.Singleton.LoadGameplay(Gameplay);
        if(IsAnyFrameOpened<BasicEntityExplorer>())
            GetFirstFrame<BasicEntityExplorer>().SetEntities(EntityManager.Singleton.GetAllEntities());
    }

    private void RenderUI(float deltaTime)
    {
        RenderMenuBar();
        RenderDockSpace();

        foreach (Frame frame in openFrames.ToList())
        {
            frame.RenderAsWindow(deltaTime);
        }
    }

    private void RenderDockSpace()
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

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, 0);
        ImGui.Begin("dockspace", windowFlags);
        ImGui.PopStyleVar(4);

        uint dockspaceId = ImGui.GetID("dockspace");
        ImGui.DockSpace(dockspaceId, new Vec2(0, 0), dockspaceFlags);
    }

    private void RenderMenuBar()
    {
        if (!ImGui.BeginMainMenuBar())
            return;

        if (ImGui.BeginMenu("File"))
        {
            FileMenuDraw.OpenLevelMenuItem();
            FileMenuDraw.CloseLevelMenuItem();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Edit"))
        {
            EditMenuDraw.EditorSettingsMenuItem();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Tools"))
        {
            ToolsMenuDraw.TranslationTool();
            ToolsMenuDraw.RotationTool();
            ToolsMenuDraw.ScaleTool();
            ImGui.Separator();
            ToolsMenuDraw.DeselectObject();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("View"))
        {
            ViewMenuDraw.ShowOverlay();
            ImGui.Separator();
            ViewMenuDraw.ShowView3D();
            ViewMenuDraw.ShowEntityExplorer();
            ViewMenuDraw.ShowInstanceInspector();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Render"))
        {
            RenderMenuDraw.ShowMobys();
            RenderMenuDraw.ShowTies();
            RenderMenuDraw.ShowUFrags();
            RenderMenuDraw.ShowVolumes();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("About"))
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
        }
    }

    public T GetFirstFrame<T>() where T : Frame
    {
        if (!IsAnyFrameOpened<T>()) return null;
        return openFrames.First(f => f.GetType() == typeof(T)) as T;
    }

    static bool FrameMustClose(Frame frame) => !frame.isOpen;

    protected override void OnClosing(CancelEventArgs e)
    {
        Environment.Exit(0);
        base.OnClosing(e);
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);

        controller?.WindowResized(ClientSize.X, ClientSize.Y);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);

        controller?.PressChar((char)e.Unicode);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        controller?.MouseScroll(e.Offset);
    }
}