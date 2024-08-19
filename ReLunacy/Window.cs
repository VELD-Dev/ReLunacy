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

    public AssetLoader AssetLoader { get; private set; }
    public FileManager FileManager { get; private set; }
    public Gameplay Gameplay { get; private set; }

    public string[] args { get => Program.cmds; }

    public Window(GameWindowSettings gws, NativeWindowSettings nws) : base(gws, nws)
    {
        Singleton = this;
        VSync = Program.Settings.VSyncMode;
    }

    public async void LoadLevel(string path)
    {
        var loadingFrame = new LoadingModal([
            new("Loading level", new(0, 5)),
            new("Loading files...", new(0, 1))
        ]);
        AddFrame(loadingFrame);
        LunaLog.LogInfo($"Loading level {path.Split(Path.DirectorySeparatorChar)[^1]}.");

        FileManager = new();
        LunaLog.LogDebug("Starting FileManager threaded task.");
        var fmTask = Task.Run(() => FileManager.LoadFolder(path));
        LunaLog.LogDebug("Awaiting for FileManager to finish its work");
        await fmTask;
        loadingFrame.UpdateProgress(0, new(1, 5));
        loadingFrame.UpdateProgress(1, new(1, 1));

        Thread.Sleep(100);

        loadingFrame.UpdateProgress(1, new(0, 1), "Loading assets...");
        AssetLoader = new(FileManager);
        LunaLog.LogDebug("Starting AssetLoader threaded task.");
        var alTask = Task.Run(() => AssetLoader.LoadAssets());
        LunaLog.LogDebug("Awaiting for AssetLoader to finish its work...");
        await alTask;
        loadingFrame.UpdateProgress(0, new(2, 5));
        loadingFrame.UpdateProgress(1, new(1, 1));

        Thread.Sleep(100);

        LunaLog.LogDebug("AssetLoader done. Initializing AssetManager.");
        loadingFrame.UpdateProgress(1, new(0, 1), "Loading asset manager...");
        AssetManager.Singleton.Initialize(AssetLoader);
        loadingFrame.UpdateProgress(0, new(3, 5));
        loadingFrame.UpdateProgress(1, new(1, 1));

        Thread.Sleep(100);

        LunaLog.LogDebug("Starting Gameplay threaded task.");

        loadingFrame.UpdateProgress(1, new(0, 1), "Loading gameplay data...");
        var gpTask = Task.Run(() => Gameplay = new(AssetLoader));

        LunaLog.LogDebug("Awaiting for Gameplay to finish its work...");
        await gpTask;
        loadingFrame.UpdateProgress(0, new(4, 5));
        loadingFrame.UpdateProgress(1, new(1, 1));

        Thread.Sleep(100);

        LunaLog.LogDebug("Loading Gameplay into EntityManager.");
        loadingFrame.UpdateProgress(1, new(0, 1), "Initializing entity manager...");
        EntityManager.Singleton.LoadGameplay(Gameplay);
        loadingFrame.UpdateProgress(0, new(5, 5));
        loadingFrame.UpdateProgress(1, new(1, 1));
        LunaLog.LogDebug("Level loaded.");
        Thread.Sleep(500);
        loadingFrame.CloseModal();
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
        LunaLog.LogInfo("Loading window.");
        base.OnLoad();

        oglVersionStr = $"OpenGL {GL.GetString(StringName.Version)}";
        Title = $"{Program.AppDisplayName} {Program.Version} ({oglVersionStr})";

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
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);

        controller?.WindowResized(ClientSize.X, ClientSize.Y);
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

        float moveSpeed = Program.Settings.CamMoveSpeed;
        float sensivity = Program.Settings.CamSensivity; // Change to settings later

        if (KeyboardState.IsKeyDown(Keys.LeftShift)) moveSpeed = Program.Settings.CamMaxSpeed;

        var camTransform = Camera.Main.transform;

        if (KeyboardState.IsKeyDown(Keys.W)) camTransform.position += camTransform.Forward * (float)args.Time * moveSpeed;
        if (KeyboardState.IsKeyDown(Keys.A)) camTransform.position += camTransform.Right * (float)args.Time * moveSpeed;
        if (KeyboardState.IsKeyDown(Keys.S)) camTransform.position -= camTransform.Forward * (float)args.Time * moveSpeed;
        if (KeyboardState.IsKeyDown(Keys.D)) camTransform.position -= camTransform.Right * (float)args.Time * moveSpeed;
        if (KeyboardState.IsKeyDown(Keys.E)) camTransform.position += (0, 0, (float)args.Time * moveSpeed);
        if (KeyboardState.IsKeyDown(Keys.Q)) camTransform.position -= (0, 0, (float)args.Time * moveSpeed);

        if (MouseState.IsButtonDown(MouseButton.Right))
        {
            CursorState = CursorState.Grabbed;

            freecamLocal += MouseState.Delta * sensivity;

            freecamLocal.X = MathHelper.Clamp(freecamLocal.X, -MathHelper.PiOver2 + 0.0001f, MathHelper.PiOver2 - 0.0001f);

            camTransform.SetRotation(Quaternion.FromAxisAngle(Vector3.UnitX, freecamLocal.X) * Quaternion.FromAxisAngle(Vector3.UnitY, freecamLocal.Y));
        }
        else if (MouseState.IsButtonReleased(MouseButton.Right))
        {
            CursorState = CursorState.Normal;
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
            //ImGui.SetNextWindowDockID(ImGui.GetID("dockspace"), ImGuiCond.Appearing);
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
        ImGui.Begin("dockspace", windowFlags);
        ImGui.PopStyleVar(3);

        uint dockspaceId = ImGui.GetID("dockspace");
        ImGui.DockSpace(dockspaceId, new Vec2(0, 0), dockspaceFlags);
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
            ViewMenuDraw.ShowView3D();
            ImGui.EndMenu();
        }

        DebugMenuDraw.Menu();
    }
}