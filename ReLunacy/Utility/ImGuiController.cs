using System.Numerics;
using System.Runtime.CompilerServices;
using Bliss.CSharp.Effects;
using Bliss.CSharp.Interact;
using Bliss.CSharp.Interact.Keyboards;
using Bliss.CSharp.Interact.Mice;
using Hexa.NET.ImGui;
using Veldrid;
using Veldrid.SPIRV;

namespace ReLunacy.Utility;

// Credits to Sparkle Engine https://github.com/MrScautHD/Sparkle/ (PR-34: https://github.com/MrScautHD/Sparkle/pull/34)

/// <summary>
/// A modified version of Veldrid.ImGui's ImGuiRenderer.
/// Manages input for ImGui and handles rendering ImGui's DrawLists with Veldrid.
/// </summary>
public class ImGuiController : IDisposable
{
    private struct ResourceSetInfo(nint imGuiBinding, ResourceSet resourceSet)
    {
        public readonly nint ImGuiBinding = imGuiBinding;
        public readonly ResourceSet ResourceSet = resourceSet;
    }

    private GraphicsDevice _graphicsDevice;
    private bool _frameBegun;

    // Veldrid objects
    private DeviceBuffer _vertexBuffer = null!;
    private DeviceBuffer _indexBuffer = null!;
    private DeviceBuffer _projMatrixBuffer = null!;
    private Texture? _fontTexture;
    private TextureView? _fontTextureView;
    private Effect _effect = null!;
    private ResourceLayout _layout = null!;
    private ResourceLayout _textureLayout = null!;
    private Pipeline _pipeline = null!;
    private ResourceSet _mainResourceSet = null!;
    private ResourceSet? _fontTextureResourceSet;

    private const nint FontAtlasId = 1;

    private int _windowWidth;
    private int _windowHeight;
    private readonly Vector2 _scaleFactor = Vector2.One;

    // Image trackers
    private readonly Dictionary<TextureView, ResourceSetInfo> _setsByView = new();
    private readonly Dictionary<Texture, TextureView?> _autoViewsByTexture = new();
    private readonly Dictionary<nint, ResourceSetInfo> _viewsById = new();
    private readonly List<IDisposable?> _ownedResources = [];
    private int _lastAssignedId = 100;

    // Input
    private static readonly Dictionary<KeyboardKey, ImGuiKey> KeyMap = new();
    private static bool _lastControlPressed;
    private static bool _lastShiftPressed;
    private static bool _lastAltPressed;
    private static bool _lastSuperPressed;

    /// <summary>
    /// Constructs a new ImGuiController.
    /// </summary>
    public ImGuiController(GraphicsDevice graphicsDevice, OutputDescription outputDescription, int width, int height)
    {
        _graphicsDevice = graphicsDevice;
        _windowWidth = width;
        _windowHeight = height;

        SetupKeymap();

        ImGui.CreateContext();
        var io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset | ImGuiBackendFlags.RendererHasTextures;
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard |
                          ImGuiConfigFlags.DockingEnable;

        // Request RGBA32 format for the font texture and add default font
        unsafe
        {
            io.Fonts.TexDesiredFormat = ImTextureFormat.Rgba32;
            io.Fonts.AddFontDefault();
        }

        // Create pipeline resources
        CreatePipelineResources(graphicsDevice, outputDescription);

        // Initialize per-frame data and start first frame
        SetPerFrameImGuiData(1f / 60f);
        ImGui.NewFrame();
        _frameBegun = true;
    }

    public void Resize(int width, int height)
    {
        _windowWidth = width;
        _windowHeight = height;
    }

    private void CreatePipelineResources(GraphicsDevice gd, OutputDescription outputDescription)
    {
        _graphicsDevice = gd;
        var factory = gd.ResourceFactory;

        _vertexBuffer = factory.CreateBuffer(new BufferDescription(10000,
            BufferUsage.VertexBuffer | BufferUsage.Dynamic)
        );
        _vertexBuffer.Name = "ImGui.NET Vertex Buffer";

        _indexBuffer = factory.CreateBuffer(new BufferDescription(2000,
            BufferUsage.IndexBuffer | BufferUsage.Dynamic)
        );
        _indexBuffer.Name = "ImGui.NET Index Buffer";

        _projMatrixBuffer = factory.CreateBuffer(new BufferDescription(64,
            BufferUsage.UniformBuffer | BufferUsage.Dynamic)
        );
        _projMatrixBuffer.Name = "ImGui.NET Projection Buffer";

        var vertexLayoutDescription = new VertexLayoutDescription(
            new VertexElementDescription("in_position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("in_texCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("in_color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Byte4Norm)
        );

        var shaderDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Shaders",
            "ImGui");
        byte[] imguiVertData = File.ReadAllBytes(Path.Combine(shaderDir, "default.vert"));
        byte[] imguiFragData = File.ReadAllBytes(Path.Combine(shaderDir, "default.frag"));

        _effect = new Effect(_graphicsDevice, vertexLayoutDescription, imguiVertData, imguiFragData, new CrossCompileOptions());

        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ProjectionMatrixBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
            new ResourceLayoutElementDescription("MainSampler", ResourceKind.Sampler, ShaderStages.Fragment)
        ));

        _textureLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription(
                "MainTexture",
                ResourceKind.TextureReadOnly, ShaderStages.Fragment)
            )
        );
        var pipelineDescription = new GraphicsPipelineDescription(
            BlendStateDescription.SINGLE_ALPHA_BLEND,
            new DepthStencilStateDescription(false, false, ComparisonKind.Always),
            new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, true),
            PrimitiveTopology.TriangleList,
            _effect.ShaderSet,
            [_layout, _textureLayout],
            outputDescription,
            ResourceBindingModel.Default
        );

        _pipeline = factory.CreateGraphicsPipeline(ref pipelineDescription);

        _mainResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
            _layout, _projMatrixBuffer, gd.PointSampler
        ));
    }

    /// <summary>
    /// Gets or creates a handle for a Texture to be drawn with ImGui.
    /// Pass the returned handle to Image() or ImageButton().
    /// </summary>
    public nint GetOrCreateImGuiBinding(ResourceFactory factory, TextureView textureView)
    {
        if (_setsByView.TryGetValue(textureView, out var rsi)) return rsi.ImGuiBinding;

        var resourceSet = factory.CreateResourceSet(new ResourceSetDescription(_textureLayout, textureView));
        rsi = new ResourceSetInfo(GetNextImGuiBindingId(), resourceSet);

        _setsByView.Add(textureView, rsi);
        _viewsById.Add(rsi.ImGuiBinding, rsi);
        _ownedResources.Add(resourceSet);

        return rsi.ImGuiBinding;
    }

    private nint GetNextImGuiBindingId() => _lastAssignedId++;

    /// <summary>
    /// Gets or creates a handle for a Texture to be drawn with ImGui.
    /// Pass the returned handle to Image() or ImageButton().
    /// </summary>
    public nint GetOrCreateImGuiBinding(ResourceFactory factory, Texture texture)
    {
        if (_autoViewsByTexture.TryGetValue(texture, out var textureView))
            return GetOrCreateImGuiBinding(factory, textureView!);

        textureView = factory.CreateTextureView(texture);
        _autoViewsByTexture.Add(texture, textureView);
        _ownedResources.Add(textureView);

        return GetOrCreateImGuiBinding(factory, textureView);
    }

    /// <summary>
    /// Retrieves the shader Texture binding for the given helper handle.
    /// </summary>
    public ResourceSet GetImageResourceSet(nint imGuiBinding)
    {
        if (!_viewsById.TryGetValue(imGuiBinding, out var tvi))
            throw new InvalidOperationException("No registered ImGui binding with id " + imGuiBinding);

        return tvi.ResourceSet;
    }

    public void ClearCachedImageResources()
    {
        foreach (var resource in _ownedResources)
            resource?.Dispose();

        _ownedResources.Clear();
        _setsByView.Clear();
        _viewsById.Clear();
        _autoViewsByTexture.Clear();
        _lastAssignedId = 100;
    }

    /// <summary>
    /// Renders the ImGui draw list data.
    /// This method requires a <see cref="GraphicsDevice"/> because it may create new DeviceBuffers if the size of vertex
    /// or index data has increased beyond the capacity of the existing buffers.
    /// A <see cref="CommandList"/> is needed to submit drawing and resource update commands.
    /// </summary>
    public void Render(GraphicsDevice gd, CommandList cl)
    {
        if (!_frameBegun) return;

        _frameBegun = false;
        ImGui.Render();
        var drawData = ImGui.GetDrawData();

        // Process any pending texture operations (ImGui 1.92+ dynamic texture management)
        UpdateTextures(gd, drawData);

        RenderImDrawData(drawData, gd, cl);
    }

    /// <summary>
    /// Process texture creation/update/destruction requests from ImGui.
    /// Called before rendering to ensure all textures are ready.
    /// </summary>
    private unsafe void UpdateTextures(GraphicsDevice gd, ImDrawDataPtr drawData)
    {
        // Process textures from draw data (ImGui 1.92+ pattern)
        var textures = drawData.Textures;
        int count = textures.Size;
        if (count == 0)
            return;

        for (int i = 0; i < count; i++)
        {
            var tex = textures[i];
            if (tex.Status != ImTextureStatus.Ok)
            {
                UpdateTexture(gd, tex);
            }
        }
    }

    /// <summary>
    /// Handle individual texture creation, update, or destruction.
    /// </summary>
    private unsafe void UpdateTexture(GraphicsDevice gd, ImTextureDataPtr tex)
    {
        if (tex.Status == ImTextureStatus.WantCreate)
        {
            // Create new texture
            int width = tex.Width;
            int height = tex.Height;
            int bpp = tex.BytesPerPixel;
            byte* pixels = tex.Pixels;
            var format = tex.Format;

            // Debug: count non-transparent pixels
            int nonTransparent = 0;
            if (format == ImTextureFormat.Alpha8)
            {
                for (int i = 0; i < width * height; i++)
                    if (pixels[i] > 0) nonTransparent++;
            }
            else
            {
                for (int i = 0; i < width * height; i++)
                    if (pixels[i * bpp + 3] > 0) nonTransparent++;
            }
            File.AppendAllText("imgui_debug.txt", $"\nTexture WantCreate: {width}x{height}, format={format}, bpp={bpp}, nonTransparent={nonTransparent}/{width*height}");

            // Create the GPU texture
            var texture = gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
                (uint)width,
                (uint)height,
                1,
                1,
                PixelFormat.R8G8B8A8UNorm,
                TextureUsage.Sampled)
            );
            texture.Name = "ImGui Dynamic Texture";

            if (format == ImTextureFormat.Alpha8)
            {
                // Convert Alpha8 to RGBA32
                byte[] rgbaPixels = new byte[width * height * 4];
                for (int j = 0; j < width * height; j++)
                {
                    rgbaPixels[j * 4 + 0] = 255; // R
                    rgbaPixels[j * 4 + 1] = 255; // G
                    rgbaPixels[j * 4 + 2] = 255; // B
                    rgbaPixels[j * 4 + 3] = pixels[j]; // A
                }
                fixed (byte* rgbaPtr = rgbaPixels)
                {
                    gd.UpdateTexture(
                        texture,
                        (nint)rgbaPtr,
                        (uint)(4 * width * height),
                        0, 0, 0,
                        (uint)width, (uint)height, 1,
                        0, 0
                    );
                }
            }
            else
            {
                // Already RGBA32, use directly
                gd.UpdateTexture(
                    texture,
                    (nint)pixels,
                    (uint)(bpp * width * height),
                    0, 0, 0,
                    (uint)width, (uint)height, 1,
                    0, 0
                );
            }

            // Create texture view and resource set
            var textureView = gd.ResourceFactory.CreateTextureView(texture);
            var resourceSet = gd.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                _textureLayout, textureView
            ));

            // Store references for later use and disposal
            nint bindingId = GetNextImGuiBindingId();
            var rsi = new ResourceSetInfo(bindingId, resourceSet);
            _viewsById[bindingId] = rsi;
            _ownedResources.Add(texture);
            _ownedResources.Add(textureView);
            _ownedResources.Add(resourceSet);

            // Check if this is the font atlas texture
            if (_fontTexture == null)
            {
                // First texture created is the font atlas
                _fontTexture = texture;
                _fontTextureView = textureView;
                _fontTextureResourceSet = resourceSet;
                tex.SetTexID(new ImTextureID(FontAtlasId));

                File.AppendAllText("imgui_debug.txt", $"\nFont texture created successfully");
            }
            else
            {
                tex.SetTexID(new ImTextureID(bindingId));
            }

            tex.Status = ImTextureStatus.Ok;

            File.AppendAllText("imgui_debug.txt", $"\nFont resource set created successfully");
        }
        else if (tex.Status == ImTextureStatus.WantUpdates)
        {
            // Handle texture updates (for dynamic glyph loading)
            // Get the texture associated with this ImTextureID
            nint texId = (nint)tex.TexID.Handle;

            // For now, recreate the entire texture on update
            // A more efficient implementation would use UpdateTexture with sub-regions
            int width = tex.Width;
            int height = tex.Height;
            int bpp = tex.BytesPerPixel;
            byte* pixels = tex.Pixels;
            var format = tex.Format;

            Texture? targetTexture = null;
            if (texId == FontAtlasId)
            {
                targetTexture = _fontTexture;
            }

            if (targetTexture != null)
            {
                if (format == ImTextureFormat.Alpha8)
                {
                    byte[] rgbaPixels = new byte[width * height * 4];
                    for (int j = 0; j < width * height; j++)
                    {
                        rgbaPixels[j * 4 + 0] = 255;
                        rgbaPixels[j * 4 + 1] = 255;
                        rgbaPixels[j * 4 + 2] = 255;
                        rgbaPixels[j * 4 + 3] = pixels[j];
                    }
                    fixed (byte* rgbaPtr = rgbaPixels)
                    {
                        gd.UpdateTexture(
                            targetTexture,
                            (nint)rgbaPtr,
                            (uint)(4 * width * height),
                            0, 0, 0,
                            (uint)width, (uint)height, 1,
                            0, 0
                        );
                    }
                }
                else
                {
                    gd.UpdateTexture(
                        targetTexture,
                        (nint)pixels,
                        (uint)(bpp * width * height),
                        0, 0, 0,
                        (uint)width, (uint)height, 1,
                        0, 0
                    );
                }
            }

            tex.Status = ImTextureStatus.Ok;
        }
        else if (tex.Status == ImTextureStatus.WantDestroy)
        {
            // Mark texture as destroyed
            // Actual cleanup will happen during disposal
            tex.SetTexID(new ImTextureID(0));
            tex.Status = ImTextureStatus.Destroyed;
        }
    }

    /// <summary>
    /// Updates ImGui input and IO configuration state.
    /// </summary>
    public void Update(float deltaSeconds)
    {
        if (_frameBegun)
            ImGui.Render();

        SetPerFrameImGuiData(deltaSeconds);
        UpdateImGuiInput();

        _frameBegun = true;
        ImGui.NewFrame();
    }

    /// <summary>
    /// Sets per-frame data based on the associated window.
    /// This is called by Update(float).
    /// </summary>
    private void SetPerFrameImGuiData(float deltaSeconds)
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(
            _windowWidth / _scaleFactor.X,
            _windowHeight / _scaleFactor.Y
        );
        io.DisplayFramebufferScale = _scaleFactor;
        io.DeltaTime = deltaSeconds; // DeltaTime is in seconds.
    }

    private static void SetupKeymap()
    {
        if (KeyMap.Count > 0)
            return;

        KeyMap[KeyboardKey.Comma] = ImGuiKey.Comma;
        KeyMap[KeyboardKey.Minus] = ImGuiKey.Minus;
        KeyMap[KeyboardKey.Period] = ImGuiKey.Period;
        KeyMap[KeyboardKey.Slash] = ImGuiKey.Slash;
        KeyMap[KeyboardKey.Number0] = ImGuiKey.Key0;
        KeyMap[KeyboardKey.Number1] = ImGuiKey.Key1;
        KeyMap[KeyboardKey.Number2] = ImGuiKey.Key2;
        KeyMap[KeyboardKey.Number3] = ImGuiKey.Key3;
        KeyMap[KeyboardKey.Number4] = ImGuiKey.Key4;
        KeyMap[KeyboardKey.Number5] = ImGuiKey.Key5;
        KeyMap[KeyboardKey.Number6] = ImGuiKey.Key6;
        KeyMap[KeyboardKey.Number7] = ImGuiKey.Key7;
        KeyMap[KeyboardKey.Number8] = ImGuiKey.Key8;
        KeyMap[KeyboardKey.Number9] = ImGuiKey.Key9;
        KeyMap[KeyboardKey.Semicolon] = ImGuiKey.Semicolon;
        KeyMap[KeyboardKey.A] = ImGuiKey.A;
        KeyMap[KeyboardKey.B] = ImGuiKey.B;
        KeyMap[KeyboardKey.C] = ImGuiKey.C;
        KeyMap[KeyboardKey.D] = ImGuiKey.D;
        KeyMap[KeyboardKey.E] = ImGuiKey.E;
        KeyMap[KeyboardKey.F] = ImGuiKey.F;
        KeyMap[KeyboardKey.G] = ImGuiKey.G;
        KeyMap[KeyboardKey.H] = ImGuiKey.H;
        KeyMap[KeyboardKey.I] = ImGuiKey.I;
        KeyMap[KeyboardKey.J] = ImGuiKey.J;
        KeyMap[KeyboardKey.K] = ImGuiKey.K;
        KeyMap[KeyboardKey.L] = ImGuiKey.L;
        KeyMap[KeyboardKey.M] = ImGuiKey.M;
        KeyMap[KeyboardKey.N] = ImGuiKey.N;
        KeyMap[KeyboardKey.O] = ImGuiKey.O;
        KeyMap[KeyboardKey.P] = ImGuiKey.P;
        KeyMap[KeyboardKey.Q] = ImGuiKey.Q;
        KeyMap[KeyboardKey.R] = ImGuiKey.R;
        KeyMap[KeyboardKey.S] = ImGuiKey.S;
        KeyMap[KeyboardKey.T] = ImGuiKey.T;
        KeyMap[KeyboardKey.U] = ImGuiKey.U;
        KeyMap[KeyboardKey.V] = ImGuiKey.V;
        KeyMap[KeyboardKey.W] = ImGuiKey.W;
        KeyMap[KeyboardKey.X] = ImGuiKey.X;
        KeyMap[KeyboardKey.Y] = ImGuiKey.Y;
        KeyMap[KeyboardKey.Z] = ImGuiKey.Z;
        KeyMap[KeyboardKey.Space] = ImGuiKey.Space;
        KeyMap[KeyboardKey.Escape] = ImGuiKey.Escape;
        KeyMap[KeyboardKey.Enter] = ImGuiKey.Enter;
        KeyMap[KeyboardKey.Tab] = ImGuiKey.Tab;
        KeyMap[KeyboardKey.BackSpace] = ImGuiKey.Backspace;
        KeyMap[KeyboardKey.Insert] = ImGuiKey.Insert;
        KeyMap[KeyboardKey.Delete] = ImGuiKey.Delete;
        KeyMap[KeyboardKey.Right] = ImGuiKey.RightArrow;
        KeyMap[KeyboardKey.Left] = ImGuiKey.LeftArrow;
        KeyMap[KeyboardKey.Down] = ImGuiKey.DownArrow;
        KeyMap[KeyboardKey.Up] = ImGuiKey.UpArrow;
        KeyMap[KeyboardKey.PageUp] = ImGuiKey.PageUp;
        KeyMap[KeyboardKey.PageDown] = ImGuiKey.PageDown;
        KeyMap[KeyboardKey.Home] = ImGuiKey.Home;
        KeyMap[KeyboardKey.End] = ImGuiKey.End;
        KeyMap[KeyboardKey.CapsLock] = ImGuiKey.CapsLock;
        KeyMap[KeyboardKey.ScrollLock] = ImGuiKey.ScrollLock;
        KeyMap[KeyboardKey.NumLock] = ImGuiKey.NumLock;
        KeyMap[KeyboardKey.PrintScreen] = ImGuiKey.PrintScreen;
        KeyMap[KeyboardKey.Pause] = ImGuiKey.Pause;
        KeyMap[KeyboardKey.F1] = ImGuiKey.F1;
        KeyMap[KeyboardKey.F2] = ImGuiKey.F2;
        KeyMap[KeyboardKey.F3] = ImGuiKey.F3;
        KeyMap[KeyboardKey.F4] = ImGuiKey.F4;
        KeyMap[KeyboardKey.F5] = ImGuiKey.F5;
        KeyMap[KeyboardKey.F6] = ImGuiKey.F6;
        KeyMap[KeyboardKey.F7] = ImGuiKey.F7;
        KeyMap[KeyboardKey.F8] = ImGuiKey.F8;
        KeyMap[KeyboardKey.F9] = ImGuiKey.F9;
        KeyMap[KeyboardKey.F10] = ImGuiKey.F10;
        KeyMap[KeyboardKey.F11] = ImGuiKey.F11;
        KeyMap[KeyboardKey.F12] = ImGuiKey.F12;
        KeyMap[KeyboardKey.ShiftLeft] = ImGuiKey.LeftShift;
        KeyMap[KeyboardKey.ControlLeft] = ImGuiKey.LeftCtrl;
        KeyMap[KeyboardKey.AltLeft] = ImGuiKey.LeftAlt;
        KeyMap[KeyboardKey.WinLeft] = ImGuiKey.LeftSuper;
        KeyMap[KeyboardKey.ShiftRight] = ImGuiKey.RightShift;
        KeyMap[KeyboardKey.ControlRight] = ImGuiKey.RightCtrl;
        KeyMap[KeyboardKey.AltRight] = ImGuiKey.RightAlt;
        KeyMap[KeyboardKey.WinRight] = ImGuiKey.RightSuper;
        KeyMap[KeyboardKey.Menu] = ImGuiKey.Menu;
        KeyMap[KeyboardKey.BracketLeft] = ImGuiKey.LeftBracket;
        KeyMap[KeyboardKey.BackSlash] = ImGuiKey.Backslash;
        KeyMap[KeyboardKey.BracketRight] = ImGuiKey.RightBracket;
        KeyMap[KeyboardKey.Grave] = ImGuiKey.GraveAccent;
        KeyMap[KeyboardKey.Keypad0] = ImGuiKey.Keypad0;
        KeyMap[KeyboardKey.Keypad1] = ImGuiKey.Keypad1;
        KeyMap[KeyboardKey.Keypad2] = ImGuiKey.Keypad2;
        KeyMap[KeyboardKey.Keypad3] = ImGuiKey.Keypad3;
        KeyMap[KeyboardKey.Keypad4] = ImGuiKey.Keypad4;
        KeyMap[KeyboardKey.Keypad5] = ImGuiKey.Keypad5;
        KeyMap[KeyboardKey.Keypad6] = ImGuiKey.Keypad6;
        KeyMap[KeyboardKey.Keypad7] = ImGuiKey.Keypad7;
        KeyMap[KeyboardKey.Keypad8] = ImGuiKey.Keypad8;
        KeyMap[KeyboardKey.Keypad9] = ImGuiKey.Keypad9;
        KeyMap[KeyboardKey.KeypadDecimal] = ImGuiKey.KeypadDecimal;
        KeyMap[KeyboardKey.KeypadDivide] = ImGuiKey.KeypadDivide;
        KeyMap[KeyboardKey.KeypadMultiply] = ImGuiKey.KeypadMultiply;
        KeyMap[KeyboardKey.KeypadMinus] = ImGuiKey.KeypadSubtract;
        KeyMap[KeyboardKey.KeypadPlus] = ImGuiKey.KeypadAdd;
        KeyMap[KeyboardKey.KeypadEnter] = ImGuiKey.KeypadEnter;
    }

    private void UpdateImGuiInput()
    {
        var io = ImGui.GetIO();

        // Ensure text input is enabled so GetTypedText() works
        if (!Input.IsTextInputActive())
            Input.EnableTextInput();

        var mousePosition = Input.GetMousePosition();
        io.AddMousePosEvent(mousePosition.X, mousePosition.Y);
        io.AddMouseButtonEvent(0, Input.IsMouseButtonDown(MouseButton.Left));
        io.AddMouseButtonEvent(1, Input.IsMouseButtonDown(MouseButton.Right));
        io.AddMouseButtonEvent(2, Input.IsMouseButtonDown(MouseButton.Middle));
        io.AddMouseButtonEvent(3, Input.IsMouseButtonDown(MouseButton.X1));
        io.AddMouseButtonEvent(4, Input.IsMouseButtonDown(MouseButton.X2));
        if (Input.IsMouseScrolling(out var wheelDelta))
            io.AddMouseWheelEvent(0f, wheelDelta.Y);

        if(Input.GetTypedText(out var inputText))
            foreach (char ch in inputText)
                io.AddInputCharacter(ch);

        var ctrlDown = Input.IsKeyDown(KeyboardKey.ControlLeft) || Input.IsKeyDown(KeyboardKey.ControlRight);
        if (ctrlDown != _lastControlPressed)
            io.AddKeyEvent(ImGuiKey.ModCtrl, ctrlDown);
        _lastControlPressed = ctrlDown;

        var shiftDown = Input.IsKeyDown(KeyboardKey.ShiftLeft) || Input.IsKeyDown(KeyboardKey.ShiftRight);
        if (shiftDown != _lastShiftPressed)
            io.AddKeyEvent(ImGuiKey.ModShift, shiftDown);
        _lastShiftPressed = shiftDown;

        var altDown = Input.IsKeyDown(KeyboardKey.AltLeft) || Input.IsKeyDown(KeyboardKey.AltRight);
        if (altDown != _lastAltPressed)
            io.AddKeyEvent(ImGuiKey.ModAlt, altDown);
        _lastAltPressed = altDown;

        var superDown = Input.IsKeyDown(KeyboardKey.WinLeft) || Input.IsKeyDown(KeyboardKey.WinRight);
        if (superDown != _lastSuperPressed)
            io.AddKeyEvent(ImGuiKey.ModSuper, superDown);
        _lastSuperPressed = superDown;

        foreach (var (key, imGuiKey) in KeyMap)
        {
            if (Input.IsKeyPressed(key))
                io.AddKeyEvent(imGuiKey, true);
            else if (Input.IsKeyReleased(key))
                io.AddKeyEvent(imGuiKey, false);
        }
    }

    private unsafe void RenderImDrawData(ImDrawDataPtr drawData, GraphicsDevice gd, CommandList cl)
    {
        uint vertexOffsetInVertices = 0;
        uint indexOffsetInElements = 0;

        if (drawData.CmdListsCount == 0)
            return;

        var totalVbSize = (uint)(drawData.TotalVtxCount * Unsafe.SizeOf<ImDrawVert>());
        if (totalVbSize > _vertexBuffer.SizeInBytes)
        {
            gd.DisposeWhenIdle(_vertexBuffer);
            _vertexBuffer = gd.ResourceFactory.CreateBuffer(new BufferDescription(
                (uint)(totalVbSize * 1.5f),
                BufferUsage.VertexBuffer | BufferUsage.Dynamic
            ));
        }

        var totalIbSize = (uint)(drawData.TotalIdxCount * sizeof(ushort));
        if (totalIbSize > _indexBuffer.SizeInBytes)
        {
            gd.DisposeWhenIdle(_indexBuffer);
            _indexBuffer = gd.ResourceFactory.CreateBuffer(new BufferDescription(
                (uint)(totalIbSize * 1.5f),
                BufferUsage.IndexBuffer | BufferUsage.Dynamic
            ));
        }

        for (var i = 0; i < drawData.CmdListsCount; i++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[i];

            cl.UpdateBuffer(
                _vertexBuffer,
                vertexOffsetInVertices * (uint)Unsafe.SizeOf<ImDrawVert>(),
                (nint)cmdList.VtxBuffer.Data,
                (uint)(cmdList.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>())
            );

            cl.UpdateBuffer(
                _indexBuffer,
                indexOffsetInElements * sizeof(ushort),
                (nint)cmdList.IdxBuffer.Data,
                (uint)(cmdList.IdxBuffer.Size * sizeof(ushort))
            );

            vertexOffsetInVertices += (uint)cmdList.VtxBuffer.Size;
            indexOffsetInElements += (uint)cmdList.IdxBuffer.Size;
        }

        // Setup orthographic projection matrix into our constant buffer
        var io = ImGui.GetIO();
        var mvp = Matrix4x4.CreateOrthographicOffCenter(
            0f,
            io.DisplaySize.X,
            io.DisplaySize.Y,
            0.0f,
            -1.0f,
            1.0f
        );

        _graphicsDevice.UpdateBuffer(_projMatrixBuffer, 0, ref mvp);

        cl.SetVertexBuffer(0, _vertexBuffer);
        cl.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
        cl.SetPipeline(_pipeline);
        cl.SetGraphicsResourceSet(0, _mainResourceSet);

        drawData.ScaleClipRects(io.DisplayFramebufferScale);

        // Render command lists
        var vtxOffset = 0;
        var idxOffset = 0;
        for (var n = 0; n < drawData.CmdListsCount; n++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[n];
            for (var cmdI = 0; cmdI < cmdList.CmdBuffer.Size; cmdI++)
            {
                ImDrawCmd imDrawCmd = cmdList.CmdBuffer[cmdI];
                if (imDrawCmd.UserCallback != null)
                    throw new Exception();

                ImTextureID texId = imDrawCmd.TexRef.GetTexID();
                nint textureId = (nint)texId.Handle;
                if (textureId != 0)
                {
                    ResourceSet? resourceSet = null;
                    if (textureId == FontAtlasId)
                        resourceSet = _fontTextureResourceSet;
                    else
                        resourceSet = GetImageResourceSet(textureId);

                    if (resourceSet != null)
                        cl.SetGraphicsResourceSet(1, resourceSet);
                }

                cl.SetScissorRect(
                    0,
                    (uint)imDrawCmd.ClipRect.X,
                    (uint)imDrawCmd.ClipRect.Y,
                    (uint)(imDrawCmd.ClipRect.Z - imDrawCmd.ClipRect.X),
                    (uint)(imDrawCmd.ClipRect.W - imDrawCmd.ClipRect.Y)
                );

                cl.DrawIndexed(imDrawCmd.ElemCount, 1,
                    imDrawCmd.IdxOffset + (uint)idxOffset,
                    (int)imDrawCmd.VtxOffset + vtxOffset, 0
                );
            }
            vtxOffset += cmdList.VtxBuffer.Size;
            idxOffset += cmdList.IdxBuffer.Size;
        }
    }

    /// <summary>
    /// Frees all graphics resources used by the renderer.
    /// </summary>
    public void Dispose()
    {
        _vertexBuffer.Dispose();
        _indexBuffer.Dispose();
        _projMatrixBuffer.Dispose();
        _effect.Dispose();
        _layout.Dispose();
        _textureLayout.Dispose();
        _pipeline.Dispose();
        _mainResourceSet.Dispose();

        // Font textures are now managed in _ownedResources
        foreach (var resource in _ownedResources)
            resource?.Dispose();

        GC.SuppressFinalize(this);
    }
}
