using ReLunacy.Engine.Rendering;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ReLunacy;

public class Window : GameWindow
{
    public static Window? Singleton { get; private set; }
    public static string AppPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    public string oglVersionStr = "Unknown OpenGL version";
    public EditorSettings Settings { get; private set; } = EditorSettings.LoadOrCreate(Path.Combine(AppPath, "EditorSettings.json"));
    public ImGuiController controller;
    public List<Frame> openFrames = [];
    public bool showOverlay = false;
    public bool showHoveredObject = false;
    public float framerate;
    public Vec4 screenSafeSpace;
    public Renderer OGLRenderer { get; private set; }

    public AssetLoader AssetLoader { get; private set; }
    public FileManager FileManager { get; private set; }
    public Gameplay Gameplay { get; private set; }

    public string[] args { get => Program.cmds; }

    public Window(GameWindowSettings gws, NativeWindowSettings nws) : base(gws, nws)
    {
        Singleton = this;
        VSync = Settings.VSyncMode;
    }

    public async void LoadLevel(string path)
    {
        FileManager = new();
        var lfTask = Task.Run(() => FileManager.LoadFolder(path));
        AssetLoader = new(FileManager);
        var laTask = Task.Run(() => AssetLoader.LoadAssets());

        await laTask;
        AssetManager.Singleton.Initialize(AssetLoader);

        var gpTask = Task.Run(() => Gameplay = new(AssetLoader));

        await gpTask;
        EntityManager.Singleton.LoadGameplay(Gameplay);
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
        if(IsAnyFrameOpened<T>())
        {
            var frameToClose = openFrames.First(f => f.GetType() == typeof(T));
            frameToClose.isOpen = false;
        }
    }

    static bool FrameMustClose(Frame frame) => !frame.isOpen;

    protected override void OnClosing(CancelEventArgs e)
    {
        Environment.Exit(0);
        base.OnClosing(e);
    }

    protected override void OnLoad()
    {
        base.OnLoad();

        oglVersionStr = $"OpenGL {GL.GetString(StringName.Version)}";
        Title = $"{Program.AppDisplayName} {Program.Version} ({oglVersionStr})";

        controller = new ImGuiController(ClientSize.X, ClientSize.Y);

        screenSafeSpace = new(0, 0, ImGui.GetMainViewport().Size.X, ImGui.GetMainViewport().Size.Y);

        OGLRenderer = new Renderer();

        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2.5f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 2.5f);
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);

        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);

        controller?.WindowResized(ClientSize.X, ClientSize.Y);
    }

    protected override void OnRenderFrame(FrameEventArgs eventArgs)
    {
        base.OnRenderFrame(eventArgs);

        openFrames.RemoveAll(FrameMustClose);

        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);

        controller?.Update(this, (float)eventArgs.Time);

        RenderUI((float)eventArgs.Time);

        if(Overlay.showOverlay)
        {
            Overlay.DrawOverlay();
        }

        GL.ClearColor(new Color4(0, 0, 0, 255));
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);

        controller?.Render();

        SwapBuffers();
    }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        if(Overlay.showOverlay)
        {
            framerate = (float)Math.Round(1 / (float)args.Time, 1);
        }

        base.OnUpdateFrame(args);
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

    private void RenderUI(float deltaTime)
    {
        RenderMenuBar();
        RenderDockSpace();

        foreach(Frame frame in openFrames)
        {
            frame.RenderAsWindow(deltaTime);
        }
    }

    private void RenderDockSpace()
    {
        ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoDocking
            | ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoBringToFrontOnFocus
            | ImGuiWindowFlags.NoNavFocus;
        ImGui.SetNextWindowViewport(ImGui.GetWindowViewport().ID);
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().WorkPos);
        ImGui.SetNextWindowSize(ImGui.GetMainViewport().WorkSize);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
        ImGui.Begin("dockspace", windowFlags);
        ImGui.PopStyleVar(2);

        uint dockspaceId = ImGui.GetID("dockspace");
        ImGui.DockSpace(dockspaceId, new Vec2(0, 0), ImGuiDockNodeFlags.None);
    }

    private void RenderMenuBar()
    {
        if (!ImGui.BeginMainMenuBar())
            return;

        if(ImGui.BeginMenu("File"))
        {
            FileMenuDraw.OpenLevelMenuItem();
            ImGui.EndMenu();
        }

        if(ImGui.BeginMenu("Edit"))
        {
            EditMenuDraw.EditorSettingsMenuItem();
            ImGui.EndMenu();
        }

        if(ImGui.BeginMenu("View"))
        {
            ImGui.SeparatorText("Overlay");
            ViewMenuDraw.ShowOverlay();
            ImGui.SeparatorText("Windows");
            ImGui.EndMenu();
        }
    }
}