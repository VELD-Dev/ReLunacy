using System.Numerics;
using NeoVeldrid;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Engine.Rendering.Vulkan;
using ReLunacy.Utility;

namespace ReLunacy.Core.Placement;

/// <summary>
/// The Asset Explorer's thumbnails: each Moby or Tie class rendered once, offscreen, by the same raw-Vulkan
/// renderer the Asset Viewer previews with (a one-asset scene, framed from its bounding sphere), then copied into
/// a small texture of its own so the renderer can go. The renders run a few per frame, from <see cref="Get"/>'s
/// requests; the cache belongs to the loaded level and is dropped when another is loaded.
/// </summary>
public sealed class AssetThumbnails
{
    public static AssetThumbnails Singleton { get; } = new();

    public const uint Size = 128;
    private const int PerFrame = 1;

    public readonly record struct Key(bool Tie, ulong Id);

    private readonly Dictionary<Key, Texture> _done = [];
    private readonly HashSet<Key> _failed = [];
    private readonly List<Key> _queue = [];
    private readonly HashSet<Key> _queued = [];
    private readonly List<(Texture Texture, int Frame)> _retired = [];
    private object? _level;
    private readonly SceneLighting _lighting = new();

    private static readonly IReadOnlyList<(Matrix4x4 world, Vector4 color, uint pickId)> NoVolumes = [];

    /// <summary>The thumbnail's image, or null while it is waiting to be rendered (it is queued on the first ask).</summary>
    public ImTextureRef? Get(bool tie, ulong id)
    {
        var window = LunaWindow.Instance;
        CheckLevel(window.Level);
        var key = new Key(tie, id);
        if (_done.TryGetValue(key, out var texture))
            return window.imGuiController.GetOrCreateImGuiBinding(window.GraphicsDevice.ResourceFactory, texture);
        if (!_failed.Contains(key) && _queued.Add(key)) _queue.Add(key);
        return null;
    }

    /// <summary>The thumbnail was tried and could not be rendered (nothing to draw).</summary>
    public bool Failed(bool tie, ulong id) => _failed.Contains(new Key(tie, id));

    /// <summary>Renders the next queued thumbnails; once a frame, before the thumbnails are drawn.</summary>
    public void Pump()
    {
        var window = LunaWindow.Instance;
        CheckLevel(window.Level);
        ReleaseRetired();
        var assets = window.AssetManager;
        if (assets == null || assets.HasPendingUploads) return;
        for (int n = 0; n < PerFrame && _queue.Count > 0; n++)
        {
            var key = _queue[0];
            _queue.RemoveAt(0);
            _queued.Remove(key);
            try
            {
                if (Render(window, assets, key) is { } texture) _done[key] = texture;
                else _failed.Add(key);
            }
            catch (Exception e)
            {
                _failed.Add(key);
                ReLunacy.Utility.LunaLog.LogError($"[Thumbnails] {(key.Tie ? "Tie" : "Moby")} 0x{key.Id:X} failed: {e.Message}");
            }
        }
    }

    /// <summary>Thumbnails still waiting.</summary>
    public int Pending => _queue.Count;

    private void CheckLevel(object? level)
    {
        if (ReferenceEquals(level, _level)) return;
        _level = level;
        var frame = ImGui.GetFrameCount();
        foreach (var texture in _done.Values) _retired.Add((texture, frame));
        _done.Clear();
        _failed.Clear();
        _queue.Clear();
        _queued.Clear();
    }

    /// <summary>Frees what a level change dropped, two frames on: this frame's draw list may still name it.</summary>
    private void ReleaseRetired()
    {
        int frame = ImGui.GetFrameCount();
        for (int i = _retired.Count - 1; i >= 0; i--)
        {
            if (frame - _retired[i].Frame < 2) continue;
            LunaWindow.Instance.imGuiController.ReleaseImGuiBinding(_retired[i].Texture);
            _retired[i].Texture.Dispose();
            _retired.RemoveAt(i);
        }
    }

    private Texture? Render(LunaWindow window, AssetManager assets, Key key)
    {
        var meshes = new List<RenderMesh>();
        Vector3 centre;
        float radius;
        if (key.Tie)
        {
            if (!assets.Ties.TryGetValue(key.Id, out var model) || window.Level is not { } level || !level.Ties.TryGetValue(key.Id, out var tie)) return null;
            meshes.AddRange(model.Meshes);
            (centre, radius) = tie.GetBoundingSphere();
        }
        else
        {
            if (!assets.Mobys.TryGetValue(key.Id, out var models) || window.Level is not { } level || !level.Mobys.TryGetValue(key.Id, out var moby)) return null;
            foreach (var model in models) meshes.AddRange(model.Meshes);
            (centre, radius) = moby.GetBoundingSphere();
        }

        var verts = new List<float[]>();
        var idx = new List<uint[]>();
        var materials = new List<VkMaterialDesc>();
        var instances = new List<(int, int, Matrix4x4, Vector4, object, float, uint)>();
        var geoRemap = new Dictionary<int, int>();
        var matRemap = new Dictionary<RenderMaterial, int>(ReferenceEqualityComparer.Instance);
        foreach (var mesh in meshes)
        {
            if (!VulkanSceneCapture.TryGet(mesh, out int gi)) continue;
            if (!geoRemap.TryGetValue(gi, out int geoSlot))
            {
                geoSlot = verts.Count;
                verts.Add(VulkanSceneCapture.VertexData[gi]);
                idx.Add(VulkanSceneCapture.Indices[gi]);
                geoRemap[gi] = geoSlot;
            }
            if (!matRemap.TryGetValue(mesh.Material, out int matSlot))
            {
                matSlot = materials.Count;
                materials.Add(VkMaterialBuilder.Build(mesh.Material, assets));
                matRemap[mesh.Material] = matSlot;
            }
            // A sphere no cull can drop, as the Asset Viewer's preview uses.
            instances.Add((geoSlot, matSlot, Matrix4x4.Identity, new Vector4(0f, 0f, 0f, 1e9f), null!, -1f, 0u));
        }
        if (instances.Count == 0) return null;

        var gd = window.GraphicsDevice;
        using var renderer = new VulkanRenderer(
            gd, verts, idx, materials, instances, null, assets.EnvironmentCubemapView?.Target, Size, Size,
            oldEngineMobys: assets.OldEngineMobys,
            cubeResources: assets.EnvironmentCubeResources,
            standardShaders: assets.StandardShaders);

        // From the front and a little above and to the side, far enough to hold the whole sphere.
        radius = MathF.Max(radius, 0.01f);
        const float fov = 35f;
        float distance = radius / MathF.Sin(float.DegreesToRadians(fov) * 0.5f) * 1.05f;
        var camera = new EditorCamera(
            centre + Vector3.Normalize(new Vector3(0.55f, 0.4f, -0.75f)) * distance, centre, Vector3.UnitY,
            fov, MathF.Max(distance * 0.01f, 0.001f), distance + radius * 2f);
        camera.Resize(Size, Size);
        camera.Update();

        renderer.ClearColour = Program.Settings.AssetViewerBackground;
        renderer.RetailBackfaceCulling = Program.Settings.BackfaceCulling;
        renderer.Frame(camera.GetView(), camera.GetProjection(), _lighting.BuildLightData(camera.Position),
            NoVolumes, 0.1f, selected: null, outlineColor: default, outlineThickness: 0f,
            cameraPosition: camera.Position, mobyDistanceCulling: false, lit: Program.Settings.EnableLighting);
        renderer.SubmitFrameBlocking();

        // The renderer's image goes with it, so it is copied into a texture that stays.
        var copy = gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            Size, Size, 1, 1, renderer.ColorTexture.Format, TextureUsage.Sampled));
        var commands = gd.ResourceFactory.CreateCommandList();
        commands.Begin();
        commands.CopyTexture(renderer.ColorTexture, copy);
        commands.End();
        gd.SubmitCommands(commands);
        gd.WaitForIdle();
        commands.Dispose();
        return copy;
    }
}
