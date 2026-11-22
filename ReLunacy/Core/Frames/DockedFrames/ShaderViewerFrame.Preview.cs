using System.Numerics;
using ReLunacy.Engine.Rendering;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Engine.Rendering.Vulkan;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;
using LunaLog = ReLunacy.Utility.LunaLog;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>The preview: the material on a sphere, cube or plane, drawn by the level view's raw-Vulkan renderer from the
/// material the scene builds for this shader, as a probe-lit Moby (the route the game's Moby programs take).</summary>
public sealed partial class ShaderViewerFrame
{
    private enum PreviewShape { Sphere, Cube, Plane }

    private readonly Viewport3D _viewport = new();
    private readonly MouseGrabHandler _rotateGrab = new() { mouseButton = ReLunacy.Utility.MouseButton.Right };
    private readonly MouseGrabHandler _panGrab = new() { mouseButton = ReLunacy.Utility.MouseButton.Middle };
    private readonly SceneLighting _lighting = new();
    private EditorCamera Camera;
    private VulkanRenderer? _renderer;
    private uint _texWidth = 300, _texHeight = 300;
    private PreviewShape _shape = PreviewShape.Sphere;
    private bool _previewDirty = true, _disposePending, _builtWhileUploading;
    // The device textures the renderer was built with: streaming swaps them for bigger ones, and the preview follows.
    private readonly List<(GpuTexture Gpu, NeoVeldrid.Texture Device)> _builtTextures = [];
    private bool _usesGameProgram;

    private static readonly IReadOnlyList<(Matrix4x4 world, Vector4 color, uint pickId)> NoVolumes = [];
    private static readonly Dictionary<PreviewShape, (float[] Vertices, uint[] Indices)> Shapes = [];

    private static EditorCamera NewCamera()
    {
        const float distance = 3.4f;
        return new EditorCamera(Vector3.Normalize(new Vector3(0.45f, 0.35f, -0.8f)) * distance, Vector3.Zero, Vector3.UnitY,
            35f, 0.01f, 100f);
    }

    private void DisposePreview()
    {
        _renderer?.Dispose();
        _renderer = null;
        _builtTextures.Clear();
        _previewDirty = true;
    }

    private void DrawPreview(double deltaTime)
    {
        float status = ImGui.GetFrameHeightWithSpacing();
        var avail = ImGui.GetContentRegionAvail();
        if (ImGui.BeginChild("##viewport", new Vector2(avail.X, MathF.Max(1f, avail.Y - status)), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            _viewport.Begin($"shader_preview_{_tuid:X}");
            if (_viewport.HasArea && (_texWidth != (uint)_viewport.PixelWidth || _texHeight != (uint)_viewport.PixelHeight))
            {
                _texWidth = (uint)_viewport.PixelWidth;
                _texHeight = (uint)_viewport.PixelHeight;
                Camera.Resize(_texWidth, _texHeight);
                try { _renderer?.Resize(LunaWindow.Instance.GraphicsDevice, _texWidth, _texHeight); }
                catch (Exception e) { LunaLog.LogError($"[ShaderViewer] resize failed: {e.Message}"); DisposePreview(); }
            }

            HandleCameraInput();
            Camera.Update();

            var assets = LunaWindow.Instance.AssetManager;
            if (_renderer != null && (TexturesChanged() || (_builtWhileUploading && assets is { HasPendingUploads: false })))
                _previewDirty = true;
            if (_previewDirty && _viewport.HasArea)
            {
                _previewDirty = false;
                RebuildPreview();
            }

            bool waitingForTextures = _builtWhileUploading && assets?.HasPendingUploads == true;
            if (_renderer != null && !waitingForTextures)
            {
                try
                {
                    _renderer.ClearColour = Program.Settings.AssetViewerBackground;
                    _renderer.RetailBackfaceCulling = Program.Settings.BackfaceCulling;
                    _renderer.Frame(Camera.GetView(), Camera.GetProjection(), _lighting.BuildLightData(Camera.Position),
                        NoVolumes, 0.1f, selected: null, outlineColor: default, outlineThickness: 0f,
                        cameraPosition: Camera.Position, mobyDistanceCulling: false, lit: Program.Settings.EnableLighting);
                    _renderer.SubmitFrame();
                }
                catch (Exception e)
                {
                    LunaLog.LogError($"[ShaderViewer] preview frame failed: {e.Message}");
                    DisposePreview();
                    _previewDirty = false;
                }
            }

            if (_renderer != null && !waitingForTextures)
                _viewport.DrawImage(LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(LunaWindow.Instance.GraphicsDevice.ResourceFactory, _renderer.ColorTexture));
            else
                _viewport.DrawEmpty();
            DrawPreviewToolbar();
            _viewport.End();
        }
        ImGui.EndChild();

        if (_material == null)
            ImGui.TextDisabled(LM.Get("GUI_Frame_ShaderViewer_NoPreview"));
        else if (_renderer != null)
            ImGui.TextDisabled(LM.Get(_usesGameProgram ? "GUI_Frame_ShaderViewer_GameProgram" : "GUI_Frame_ShaderViewer_EditorShading"));
    }

    private void DrawPreviewToolbar()
    {
        var overlay = _viewport.Overlay;
        Shape(PreviewShape.Sphere, Icons.CircleDot, "GUI_Frame_ShaderViewer_Sphere");
        Shape(PreviewShape.Cube, Icons.Cube, "GUI_Frame_ShaderViewer_Cube");
        Shape(PreviewShape.Plane, Icons.VectorSquare, "GUI_Frame_ShaderViewer_Plane");
        overlay.Gap();
        if (overlay.Button(Icons.Home, LM.Get("GUI_Frame_ShaderViewer_ResetView")))
        {
            Camera = NewCamera();
            Camera.Resize(_texWidth, _texHeight);
        }

        void Shape(PreviewShape shape, string icon, string tooltip)
        {
            bool on = _shape == shape;
            if (overlay.ToggleButton($"{icon}##{shape}", ref on, LM.Get(tooltip)) && _shape != shape)
            {
                _shape = shape;
                _previewDirty = true;
            }
        }
    }

    private void HandleCameraInput()
    {
        bool allow = _viewport.AllowCameraInput;
        bool rotating = _rotateGrab.TryGrabMouse(allow);
        bool panning = _panGrab.TryGrabMouse(allow);
        _viewport.SetMouseCaptured(rotating || panning);
        if (rotating || panning)
        {
            Vector2 delta = Input.GetMouseDelta();
            if (rotating)
            {
                Vector2 rotation = delta * Program.Settings.CamSensivity;
                Camera.SetPitch(Camera.GetPitch() - rotation.Y, true);
                Camera.SetYaw(Camera.GetYaw() - rotation.X, true);
            }
            if (panning)
            {
                float distance = Vector3.Distance(Camera.Position, Camera.Target);
                float perPixel = 2f * distance * MathF.Tan(float.DegreesToRadians(Camera.Fov) * 0.5f) / Math.Max(1, _viewport.PixelHeight);
                Vector3 forward = Camera.GetForward();
                Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Camera.Up));
                Vector3 up = Vector3.Normalize(Vector3.Cross(right, forward));
                Vector3 shift = right * (-delta.X * perPixel) + up * (delta.Y * perPixel);
                Camera.Position += shift;
                Camera.Target += shift;
            }
        }
        if (allow && Input.IsMouseScrolling(out var scroll))
        {
            float distance = Vector3.Distance(Camera.Position, Camera.Target);
            // Wheel up comes closer; kept off the surface and inside the far plane.
            Camera.MoveToTarget(Math.Clamp(-scroll.Y * 0.1f * distance, 1.2f - distance, 40f - distance));
        }
    }

    private bool TexturesChanged()
    {
        foreach (var (gpu, device) in _builtTextures)
            if (!ReferenceEquals(gpu.DeviceTexture, device)) return true;
        return false;
    }

    private void RebuildPreview()
    {
        _renderer?.Dispose();
        _renderer = null;
        _builtTextures.Clear();
        _usesGameProgram = false;

        var window = LunaWindow.Instance;
        var assets = window.AssetManager;
        if (assets == null || EnsureRenderMaterial() is not { } material) return;

        _lighting.OldEngineMode = assets.OldEngineMobys;
        _lighting.SetEnvironment(_lighting.OldEngineMode ? window.Level?.LightingEnvironment : null);

        foreach (var key in material.GetMaterialMapKeys())
            if (material.GetMaterialMap(key)?.Texture is { } gpu)
                _builtTextures.Add((gpu, gpu.DeviceTexture));
        _builtWhileUploading = assets.HasPendingUploads;

        var (vertices, indices) = ShapeGeometry(_shape);
        var instances = new List<(int, int, Matrix4x4, Vector4, object, float, uint)>
        {
            // No entity behind it: the kind is named instead, so the renderer takes the Moby route.
            (0, 0, Matrix4x4.Identity, new Vector4(0f, 0f, 0f, 1e9f), SceneEntityKind.Moby, -1f, 0u),
        };
        try
        {
            _renderer = new VulkanRenderer(
                window.GraphicsDevice, [vertices], [indices], [VkMaterialBuilder.Build(material, assets)], instances, null,
                assets.EnvironmentCubemapView?.Target, _texWidth, _texHeight,
                oldEngineMobys: assets.OldEngineMobys,
                cubeResources: assets.EnvironmentCubeResources,
                standardShaders: assets.StandardShaders);
            var (translated, _) = _renderer.RetailShaderStats;
            _usesGameProgram = translated > 0 || _renderer.StandardShaderDraws > 0;
        }
        catch (Exception e)
        {
            LunaLog.LogError($"[ShaderViewer] preview init failed: {e.Message}");
            _renderer = null;
        }
    }

    internal static (float[] Vertices, uint[] Indices) SphereGeometry() => ShapeGeometry(PreviewShape.Sphere);

    private static (float[] Vertices, uint[] Indices) ShapeGeometry(PreviewShape shape)
    {
        if (Shapes.TryGetValue(shape, out var cached)) return cached;
        var vertices = new List<Vertex3D>();
        var indices = new List<uint>();
        switch (shape)
        {
            case PreviewShape.Sphere: BuildSphere(vertices, indices); break;
            case PreviewShape.Cube:
                foreach (var n in (ReadOnlySpan<Vector3>)[Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ])
                    AddFace(vertices, indices, n, 1.4f);
                break;
            default: AddFace(vertices, indices, -Vector3.UnitZ, 2.4f, centred: true); break;
        }
        var result = (VulkanSceneCapture.Interleave([.. vertices]), indices.ToArray());
        Shapes[shape] = result;
        return result;
    }

    /// <summary>A square face of side <paramref name="size"/>, its normal <paramref name="n"/>, wound to face it.</summary>
    private static void AddFace(List<Vertex3D> vertices, List<uint> indices, Vector3 n, float size, bool centred = false)
    {
        Vector3 u = MathF.Abs(n.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY;
        Vector3 tangent = Vector3.Normalize(Vector3.Cross(u, n));
        Vector3 bitangent = Vector3.Cross(n, tangent);
        uint first = (uint)vertices.Count;
        float half = size * 0.5f;
        for (int corner = 0; corner < 4; corner++)
        {
            float sx = corner is 0 or 3 ? -1f : 1f;
            float sy = corner < 2 ? -1f : 1f;
            Vector3 position = (centred ? Vector3.Zero : n * half) + (tangent * sx + bitangent * sy) * half;
            vertices.Add(new Vertex3D(position, new Vector2(sx * 0.5f + 0.5f, 0.5f - sy * 0.5f), Vector2.Zero, n, new Vector4(tangent, 1f), Vector4.One));
        }
        AddTriangle(vertices, indices, first, first + 1, first + 2);
        AddTriangle(vertices, indices, first, first + 2, first + 3);
    }

    private static void BuildSphere(List<Vertex3D> vertices, List<uint> indices)
    {
        const int rings = 48, segments = 96;
        for (int r = 0; r <= rings; r++)
        {
            float v = r / (float)rings;
            float theta = v * MathF.PI;
            for (int s = 0; s <= segments; s++)
            {
                float u = s / (float)segments;
                float phi = u * MathF.Tau;
                var n = new Vector3(MathF.Sin(theta) * MathF.Cos(phi), MathF.Cos(theta), MathF.Sin(theta) * MathF.Sin(phi));
                // d(position)/du, then the handedness that makes cross(n, t) * w follow increasing v.
                var t = new Vector3(-MathF.Sin(phi), 0f, MathF.Cos(phi));
                var dv = new Vector3(MathF.Cos(theta) * MathF.Cos(phi), -MathF.Sin(theta), MathF.Cos(theta) * MathF.Sin(phi));
                float w = Vector3.Dot(Vector3.Cross(n, t), dv) >= 0f ? 1f : -1f;
                vertices.Add(new Vertex3D(n, new Vector2(u * 2f, v), Vector2.Zero, n, new Vector4(t, w), Vector4.One));
            }
        }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                uint a = (uint)(r * (segments + 1) + s), b = a + segments + 1;
                if (r > 0) AddTriangle(vertices, indices, a, b, a + 1);
                if (r < rings - 1) AddTriangle(vertices, indices, a + 1, b, b + 1);
            }
    }

    /// <summary>Adds a triangle wound counter-clockwise about its vertices' normal (front-facing to the renderer's culling).</summary>
    private static void AddTriangle(List<Vertex3D> vertices, List<uint> indices, uint a, uint b, uint c)
    {
        Vector3 pa = vertices[(int)a].Position, pb = vertices[(int)b].Position, pc = vertices[(int)c].Position;
        Vector3 normal = vertices[(int)a].Normal + vertices[(int)b].Normal + vertices[(int)c].Normal;
        if (Vector3.Dot(Vector3.Cross(pb - pa, pc - pa), normal) < 0f) (b, c) = (c, b);
        indices.AddRange([a, b, c]);
    }
}
