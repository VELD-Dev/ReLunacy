using System.Numerics;
using System.Runtime.CompilerServices;
using Bliss.CSharp.Interact;
using Bliss.CSharp.Interact.Keyboards;
using Bliss.CSharp.Interact.Mice;
using Veldrith;
using Veldrith.SPIRV;

namespace ReLunacy.Utility;

// Credits to Sparkle Engine https://github.com/MrScautHD/Sparkle/ (PR-34: https://github.com/MrScautHD/Sparkle/pull/34)
// A modified version of Veldrid.ImGui's ImGuiRenderer, manages input for ImGui and renders its DrawLists.
public class ImGuiController : IDisposable
{
    private struct ResourceSetInfo(nint imGuiBinding, ResourceSet resourceSet)
    {
        public readonly nint ImGuiBinding = imGuiBinding;
        public readonly ResourceSet ResourceSet = resourceSet;
    }

    private GraphicsDevice _graphicsDevice;
    private bool _frameBegun;

    private DeviceBuffer _vertexBuffer = null!;
    private DeviceBuffer _indexBuffer = null!;
    private DeviceBuffer _projMatrixBuffer = null!;
    private Shader[] _shaders = null!;
    private ResourceLayout _layout = null!;
    private ResourceLayout _textureLayout = null!;
    private Pipeline _pipeline = null!;
    private ResourceSet _mainResourceSet = null!;

    private readonly Dictionary<int, (Texture Texture, TextureView View, ResourceSet ResourceSet)> _managedTextures = [];

    private int _windowWidth;
    private int _windowHeight;
    private readonly Vector2 _scaleFactor = Vector2.One;

    private readonly Dictionary<TextureView, ResourceSetInfo> _setsByView = [];
    private readonly Dictionary<Texture, TextureView?> _autoViewsByTexture = [];
    private readonly Dictionary<nint, ResourceSetInfo> _viewsById = [];
    private readonly List<IDisposable?> _ownedResources = [];
    private int _lastAssignedId = 100;

    private static readonly Dictionary<KeyboardKey, ImGuiKey> KeyMap = [];
    private static bool _lastControlPressed;
    private static bool _lastShiftPressed;
    private static bool _lastAltPressed;
    private static bool _lastSuperPressed;

    public ImGuiController(GraphicsDevice graphicsDevice, OutputDescription outputDescription, int width, int height)
    {
        _graphicsDevice = graphicsDevice;
        _windowWidth = width;
        _windowHeight = height;

        SetupKeymap();

        ImGui.CreateContext();
        var io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset | ImGuiBackendFlags.RendererHasTextures;
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard | ImGuiConfigFlags.DockingEnable;
        io.Fonts.Flags |= ImFontAtlasFlags.NoBakedLines;

        unsafe
        {
            io.Fonts.AddFontDefault();
            var fontAwesomePath = Path.Combine(Program.EditorPath, "Assets", "Fonts", "fa-solid-900.ttf");
            if (File.Exists(fontAwesomePath))
            {
                var config = ImGui.ImFontConfig();
                config.MergeMode = true;
                config.PixelSnapH = true;
                config.GlyphMinAdvanceX = 13f;
                ushort[] ranges = [0xf000, 0xf9ff, 0];
                fixed (ushort* rangesPtr = ranges)
                {
                    io.Fonts.AddFontFromFileTTF(fontAwesomePath, 13f, config);
                }
                config.Destroy();
            }
        }

        CreateDeviceResources(graphicsDevice, outputDescription);
        SetPerFrameImGuiData(1f / 60f);
        ImGui.NewFrame();
        _frameBegun = true;
    }

    public void Resize(int width, int height)
    {
        _windowWidth = width;
        _windowHeight = height;
    }

    private void CreateDeviceResources(GraphicsDevice gd, OutputDescription outputDescription)
    {
        _graphicsDevice = gd;
        var factory = gd.ResourceFactory;

        _vertexBuffer = factory.CreateBuffer(new BufferDescription(10000, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        _vertexBuffer.Name = "ImGui Vertex Buffer";

        _indexBuffer = factory.CreateBuffer(new BufferDescription(2000, BufferUsage.IndexBuffer | BufferUsage.Dynamic));
        _indexBuffer.Name = "ImGui Index Buffer";

        _projMatrixBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _projMatrixBuffer.Name = "ImGui Projection Buffer";

        var vertexLayoutDescription = new VertexLayoutDescription(
            new VertexElementDescription("in_position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("in_texCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("in_color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Byte4Norm));

        var shaderDir = Path.Combine(Program.EditorPath, "Shaders", "ImGui");
        byte[] imguiVertData = File.ReadAllBytes(Path.Combine(shaderDir, "default.vert"));
        byte[] imguiFragData = File.ReadAllBytes(Path.Combine(shaderDir, "default.frag"));

        _shaders = factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex, imguiVertData, "main"),
            new ShaderDescription(ShaderStages.Fragment, imguiFragData, "main"),
            new CrossCompileOptions());

        var shaderSet = new ShaderSetDescription([vertexLayoutDescription], _shaders);

        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ProjectionMatrixBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
            new ResourceLayoutElementDescription("MainSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

        _textureLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("MainTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment)));

        var pipelineDescription = new GraphicsPipelineDescription(
            BlendStateDescription.SINGLE_ALPHA_BLEND,
            new DepthStencilStateDescription(false, false, ComparisonKind.Always),
            new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise,
                depthClipEnabled: true, depthBias: 0, slopeScaledDepthBias: 0f, depthBiasClamp: 0f,
                scissorTestEnabled: true),
            PrimitiveTopology.TriangleList,
            shaderSet,
            [_layout, _textureLayout],
            outputDescription,
            ResourceBindingModel.Default);

        _pipeline = factory.CreateGraphicsPipeline(ref pipelineDescription);

        // Point-sampled for ALL ImGui drawing, deliberately: texture-inspection previews
        // (TexturesExplorer etc.) must show raw texels, and the 3D viewport image is blitted 1:1
        // (its render texture is sized to the viewport), so filtering it would be a no-op anyway.
        // Scene texture filtering lives entirely on the 3D side — see
        // AssetManager.SetTextureFiltering. A previous attempt to make this per-binding (rebinding
        // resource set 0 inside the per-command loop below) was suspected during a GPUVM-fault
        // investigation and reverted, but never confirmed as the cause — the fault was in fact the
        // lit effect's descriptor set numbering, see AssetManager.BuildLitModelEffect. Restoring
        // the per-binding sampler here is probably safe; it just hasn't been retried since.
        _mainResourceSet = factory.CreateResourceSet(new ResourceSetDescription(_layout, _projMatrixBuffer, gd.PointSampler));
    }

    public ImTextureRef GetOrCreateImGuiBinding(ResourceFactory factory, TextureView textureView)
    {
        if (_setsByView.TryGetValue(textureView, out var rsi))
            return new ImTextureRef { TexID = (ImTextureID)rsi.ImGuiBinding };

        var resourceSet = factory.CreateResourceSet(new ResourceSetDescription(_textureLayout, textureView));
        rsi = new ResourceSetInfo(GetNextImGuiBindingId(), resourceSet);

        _setsByView.Add(textureView, rsi);
        _viewsById.Add(rsi.ImGuiBinding, rsi);
        _ownedResources.Add(resourceSet);

        return new ImTextureRef { TexID = (ImTextureID)rsi.ImGuiBinding };
    }

    private nint GetNextImGuiBindingId() => _lastAssignedId++;

    public ImTextureRef GetOrCreateImGuiBinding(ResourceFactory factory, Texture texture)
    {
        if (_autoViewsByTexture.TryGetValue(texture, out var textureView))
            return GetOrCreateImGuiBinding(factory, textureView!);

        textureView = factory.CreateTextureView(texture);
        _autoViewsByTexture.Add(texture, textureView);
        _ownedResources.Add(textureView);

        return GetOrCreateImGuiBinding(factory, textureView);
    }

    public ResourceSet GetImageResourceSet(nint imGuiBinding)
    {
        if (!_viewsById.TryGetValue(imGuiBinding, out var tvi))
            throw new InvalidOperationException("No registered ImGui binding with id " + imGuiBinding);
        return tvi.ResourceSet;
    }

    public void ClearCachedImageResources()
    {
        foreach (var resource in _ownedResources) resource?.Dispose();
        _ownedResources.Clear();
        _setsByView.Clear();
        _viewsById.Clear();
        _autoViewsByTexture.Clear();
        _lastAssignedId = 100;
    }

    private unsafe void ProcessTextureUpdates(ImDrawDataPtr drawData, GraphicsDevice gd)
    {
        for (int i = 0; i < drawData.Textures.Size; i++)
        {
            var texData = drawData.Textures[i];

            switch (texData.Status)
            {
                case ImTextureStatus.WantCreate:
                case ImTextureStatus.WantUpdates:
                {
                    int uniqueId = texData.UniqueID;
                    int width = texData.Width;
                    int height = texData.Height;

                    if (_managedTextures.TryGetValue(uniqueId, out var existing))
                    {
                        existing.ResourceSet.Dispose();
                        existing.View.Dispose();
                        existing.Texture.Dispose();
                    }

                    var gpuTexture = gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
                        (uint)width, (uint)height, 1, 1, PixelFormat.R8G8B8A8UNorm, TextureUsage.Sampled));
                    gpuTexture.Name = $"ImGui Managed Texture {uniqueId}";

                    gd.UpdateTexture(gpuTexture, (nint)texData.Pixels, (uint)(texData.BytesPerPixel * width * height), 0, 0, 0, (uint)width, (uint)height, 1, 0, 0);

                    var view = gd.ResourceFactory.CreateTextureView(gpuTexture);
                    var resourceSet = gd.ResourceFactory.CreateResourceSet(new ResourceSetDescription(_textureLayout, view));

                    _managedTextures[uniqueId] = (gpuTexture, view, resourceSet);

                    texData.SetTexID((ImTextureID)(nint)uniqueId);
                    texData.SetStatus(ImTextureStatus.Ok);
                    break;
                }
                case ImTextureStatus.WantDestroy:
                {
                    int uniqueId = texData.UniqueID;
                    if (_managedTextures.TryGetValue(uniqueId, out var toDestroy))
                    {
                        toDestroy.ResourceSet.Dispose();
                        toDestroy.View.Dispose();
                        toDestroy.Texture.Dispose();
                        _managedTextures.Remove(uniqueId);
                    }
                    texData.SetStatus(ImTextureStatus.Destroyed);
                    break;
                }
            }
        }
    }

    private ResourceSet? GetResourceSetForTexture(ImTextureID texId)
    {
        nint id = (nint)texId;
        if (id == 0) return null;

        if (_managedTextures.TryGetValue((int)id, out var managed))
            return managed.ResourceSet;

        if (_viewsById.TryGetValue(id, out var rsi))
            return rsi.ResourceSet;

        return null;
    }

    public void Render(GraphicsDevice gd, CommandList cl)
    {
        if (!_frameBegun) return;

        _frameBegun = false;
        ImGui.Render();
        var drawData = ImGui.GetDrawData();
        ProcessTextureUpdates(drawData, gd);
        RenderImDrawData(drawData, gd, cl);
    }

    public void Update(float deltaSeconds)
    {
        if (_frameBegun) ImGui.Render();

        SetPerFrameImGuiData(deltaSeconds);
        UpdateImGuiInput();

        _frameBegun = true;
        ImGui.NewFrame();
    }

    private void SetPerFrameImGuiData(float deltaSeconds)
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(_windowWidth / _scaleFactor.X, _windowHeight / _scaleFactor.Y);
        io.DisplayFramebufferScale = _scaleFactor;
        io.DeltaTime = deltaSeconds;
    }

    private static void SetupKeymap()
    {
        if (KeyMap.Count > 0) return;

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

        // Only capture text input while ImGui actually wants it (a text field is focused).
        // Leaving SDL text-input mode permanently on routes letter keys through the IME/
        // composition path instead of delivering plain key-down events, which silently
        // breaks WASD-style shortcuts anywhere outside of a text box.
        if (io.WantTextInput && !Input.IsTextInputActive())
            Input.EnableTextInput();
        else if (!io.WantTextInput && Input.IsTextInputActive())
            Input.DisableTextInput();

        var mousePosition = Input.GetMousePosition();
        io.AddMousePosEvent(mousePosition.X, mousePosition.Y);
        io.AddMouseButtonEvent(0, Input.IsMouseButtonDown(MouseButton.Left));
        io.AddMouseButtonEvent(1, Input.IsMouseButtonDown(MouseButton.Right));
        io.AddMouseButtonEvent(2, Input.IsMouseButtonDown(MouseButton.Middle));
        io.AddMouseButtonEvent(3, Input.IsMouseButtonDown(MouseButton.X1));
        io.AddMouseButtonEvent(4, Input.IsMouseButtonDown(MouseButton.X2));
        if (Input.IsMouseScrolling(out var wheelDelta))
            io.AddMouseWheelEvent(0f, wheelDelta.Y);

        if (Input.GetTypedText(out var inputText))
            foreach (char ch in inputText)
                io.AddInputCharacter(ch);

        var ctrlDown = Input.IsKeyDown(KeyboardKey.ControlLeft) || Input.IsKeyDown(KeyboardKey.ControlRight);
        if (ctrlDown != _lastControlPressed) io.AddKeyEvent(ImGuiKey.ModCtrl, ctrlDown);
        _lastControlPressed = ctrlDown;

        var shiftDown = Input.IsKeyDown(KeyboardKey.ShiftLeft) || Input.IsKeyDown(KeyboardKey.ShiftRight);
        if (shiftDown != _lastShiftPressed) io.AddKeyEvent(ImGuiKey.ModShift, shiftDown);
        _lastShiftPressed = shiftDown;

        var altDown = Input.IsKeyDown(KeyboardKey.AltLeft) || Input.IsKeyDown(KeyboardKey.AltRight);
        if (altDown != _lastAltPressed) io.AddKeyEvent(ImGuiKey.ModAlt, altDown);
        _lastAltPressed = altDown;

        var superDown = Input.IsKeyDown(KeyboardKey.WinLeft) || Input.IsKeyDown(KeyboardKey.WinRight);
        if (superDown != _lastSuperPressed) io.AddKeyEvent(ImGuiKey.ModSuper, superDown);
        _lastSuperPressed = superDown;

        foreach (var (key, imGuiKey) in KeyMap)
        {
            if (Input.IsKeyPressed(key)) io.AddKeyEvent(imGuiKey, true);
            else if (Input.IsKeyReleased(key)) io.AddKeyEvent(imGuiKey, false);
        }
    }

    private unsafe void RenderImDrawData(ImDrawDataPtr drawData, GraphicsDevice gd, CommandList cl)
    {
        uint vertexOffsetInVertices = 0;
        uint indexOffsetInElements = 0;

        if (drawData.CmdListsCount == 0) return;

        var totalVbSize = (uint)(drawData.TotalVtxCount * Unsafe.SizeOf<ImDrawVert>());
        if (totalVbSize > _vertexBuffer.SizeInBytes)
        {
            gd.DisposeWhenIdle(_vertexBuffer);
            _vertexBuffer = gd.ResourceFactory.CreateBuffer(new BufferDescription((uint)(totalVbSize * 1.5f), BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        }

        var totalIbSize = (uint)(drawData.TotalIdxCount * sizeof(ushort));
        if (totalIbSize > _indexBuffer.SizeInBytes)
        {
            gd.DisposeWhenIdle(_indexBuffer);
            _indexBuffer = gd.ResourceFactory.CreateBuffer(new BufferDescription((uint)(totalIbSize * 1.5f), BufferUsage.IndexBuffer | BufferUsage.Dynamic));
        }

        for (var i = 0; i < drawData.CmdListsCount; i++)
        {
            var cmdList = drawData.CmdLists[i];

            cl.UpdateBuffer(_vertexBuffer, vertexOffsetInVertices * (uint)Unsafe.SizeOf<ImDrawVert>(), (nint)cmdList.VtxBuffer.Data, (uint)(cmdList.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>()));
            cl.UpdateBuffer(_indexBuffer, indexOffsetInElements * sizeof(ushort), (nint)cmdList.IdxBuffer.Data, (uint)(cmdList.IdxBuffer.Size * sizeof(ushort)));

            vertexOffsetInVertices += (uint)cmdList.VtxBuffer.Size;
            indexOffsetInElements += (uint)cmdList.IdxBuffer.Size;
        }

        var io = ImGui.GetIO();
        var mvp = Matrix4x4.CreateOrthographicOffCenter(0f, io.DisplaySize.X, io.DisplaySize.Y, 0.0f, -1.0f, 1.0f);
        _graphicsDevice.UpdateBuffer(_projMatrixBuffer, 0, ref mvp);

        cl.SetVertexBuffer(0, _vertexBuffer);
        cl.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
        cl.SetPipeline(_pipeline);
        cl.SetGraphicsResourceSet(0, _mainResourceSet);

        drawData.ScaleClipRects(io.DisplayFramebufferScale);

        var vtxOffset = 0;
        var idxOffset = 0;
        for (var n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];
            for (var cmdI = 0; cmdI < cmdList.CmdBuffer.Size; cmdI++)
            {
                var imDrawCmdPtr = cmdList.CmdBuffer[cmdI];
                if (imDrawCmdPtr.UserCallback != null) throw new Exception();

                var texId = imDrawCmdPtr.GetTexID();
                var resourceSet = GetResourceSetForTexture(texId);
                if (resourceSet != null) cl.SetGraphicsResourceSet(1, resourceSet);

                cl.SetScissorRect(0,
                    (uint)imDrawCmdPtr.ClipRect.X,
                    (uint)imDrawCmdPtr.ClipRect.Y,
                    (uint)(imDrawCmdPtr.ClipRect.Z - imDrawCmdPtr.ClipRect.X),
                    (uint)(imDrawCmdPtr.ClipRect.W - imDrawCmdPtr.ClipRect.Y));

                cl.DrawIndexed(imDrawCmdPtr.ElemCount, 1, imDrawCmdPtr.IdxOffset + (uint)idxOffset, (int)imDrawCmdPtr.VtxOffset + vtxOffset, 0);
            }
            vtxOffset += cmdList.VtxBuffer.Size;
            idxOffset += cmdList.IdxBuffer.Size;
        }
    }

    public void Dispose()
    {
        _vertexBuffer.Dispose();
        _indexBuffer.Dispose();
        _projMatrixBuffer.Dispose();
        foreach (var shader in _shaders)
            shader.Dispose();
        _layout.Dispose();
        _textureLayout.Dispose();
        _pipeline.Dispose();
        _mainResourceSet.Dispose();

        foreach (var (_, managed) in _managedTextures)
        {
            managed.ResourceSet.Dispose();
            managed.View.Dispose();
            managed.Texture.Dispose();
        }
        _managedTextures.Clear();

        foreach (var resource in _ownedResources) resource?.Dispose();

        GC.SuppressFinalize(this);
    }
}
