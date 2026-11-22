using System.Diagnostics;
using System.Numerics;
using NeoVeldrid;
using ReLunacy.Core.Frames.DockedFrames;
using ReLunacy.Core.Project;
using ReLunacy.Engine.Assets.Audio;
using ReLunacy.Engine.Assets.Browser;
using ReLunacy.Engine.Assets.Effects;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Loading.Readers;
using ReLunacy.Engine.Projects;
using ReLunacy.Engine.Rendering;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Engine.Rendering.Vulkan;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility;
using LunaLog = ReLunacy.Utility.LunaLog;

namespace ReLunacy.Core.Placement;

/// <summary>
/// Every asset thumbnail of the editor, made when it is first shown. A thumbnail is asked for each frame its tile is
/// drawn (<see cref="Get(AssetEntry)"/>); <see cref="Pump"/> then works through what was asked for, the newest asks first,
/// and forgets asks whose tile went away. Each one is looked for on disk first (<see cref="ThumbnailCache"/>, on worker
/// threads); a miss is decoded on a worker (textures) or rendered offscreen by the raw-Vulkan renderer (classes, shrubs,
/// UFrags, foliage, shaders on a sphere), a little per frame, then written back. The images live in small GPU textures,
/// at most <see cref="MaxResident"/> of them, least recently drawn dropped first; all go with the level.
/// </summary>
public sealed class ThumbnailService
{
    public static ThumbnailService Singleton { get; } = new();

    public const int Size = 128;
    /// <summary>Thumbnail textures kept on the GPU (64 KB each).</summary>
    public const int MaxResident = 384;
    private const int MaxJobs = 4;
    private const int UploadsPerFrame = 24;
    private const double RenderBudgetMs = 6.0;
    private const int MaxRendersPerFrame = 2;
    // A tile not drawn for this many frames has scrolled away: its ask is dropped.
    private const int DropAfterFrames = 2;
    // A class not read yet is read only once its tile has stayed in view this long, so a scroll past reads nothing.
    private const int ReadAfterFrames = 12;

    public enum State { None, Pending, Ready, Unavailable }

    private enum Stage { New, Loading, Render, Building, Upload, Ready, Failed }

    private sealed class Slot(AssetKey key)
    {
        public readonly AssetKey Key = key;
        public object? Tag;
        public bool Imported;
        public Stage Stage = Stage.New;
        public Task<object?>? Job;
        public SoundWaveform? Waveform;
        public bool DiskOnly;
        public ulong Stamp;
        public bool Cacheable;
        public ThumbnailImage? Image;
        public Texture? Texture;
        public Scene? Scene;
        public int Episode, Order, LastFrame;
    }

    /// <summary>What one offscreen render draws.</summary>
    private sealed class Scene
    {
        public readonly List<float[]> Vertices = [];
        public readonly List<uint[]> Indices = [];
        public readonly List<RenderMaterial> Materials = [];
        public readonly List<(int Geometry, int Material, Matrix4x4 World)> Instances = [];
        public Vector4? Bounds;
    }

    private readonly Dictionary<AssetKey, Slot> _slots = [];
    private readonly List<(Texture Texture, int Frame)> _retired = [];
    private readonly SceneLighting _lighting = new();
    private static readonly IReadOnlyList<(Matrix4x4 world, Vector4 color, uint pickId)> NoVolumes = [];

    private object? _level;
    private int _frame = -1, _order, _pumped = -1;
    private ThumbnailCache? _cache;
    private string? _cacheRoot;
    private ulong _levelStamp;
    private (LunaProject? Project, int Revision) _stampFor;
    private readonly Dictionary<int, int> _effectTextures = [];
    private LevelSounds? _sounds;

    // --- Asking ------------------------------------------------------------------------------------

    /// <summary>Whether this kind gets a thumbnail of its own.</summary>
    public static bool Supports(AssetKind kind) => kind is AssetKind.Moby or AssetKind.Tie or AssetKind.Shrub or AssetKind.UFrag
        or AssetKind.Foliage or AssetKind.Texture or AssetKind.EffectTexture or AssetKind.Shader or AssetKind.Sound;

    /// <summary>The longest a resident (Scream) sound is rendered for its waveform: a loop would never end.</summary>
    public const double WaveformSeconds = 20.0;
    // A stream is decoded whole up to this, as the Sound Banks player keeps it.
    private const double StreamSeconds = 900.0;
    // Decoding shares the level's sound files and Scream bank: one at a time.
    private static readonly object SoundLock = new();
    private const int MaxWaveforms = 4096;

    /// <summary>The entry's thumbnail, or null while it is made (it is asked for by this call) or when it has none.</summary>
    public ImTextureRef? Get(AssetEntry entry) => Get(entry.Key, entry.Tag, entry.Imported);

    /// <summary>As <see cref="Get(AssetEntry)"/>, by key; <paramref name="tag"/> is the entry's Tag where the kind needs it.</summary>
    public ImTextureRef? Get(AssetKey key, object? tag = null, bool imported = false)
    {
        if (!Supports(key.Kind) || key.Kind == AssetKind.Sound) return null;
        var slot = Request(key, tag, imported);
        var window = LunaWindow.Instance;
        return slot.Texture != null ? window.imGuiController.GetOrCreateImGuiBinding(window.GraphicsDevice.ResourceFactory, slot.Texture) : null;
    }

    /// <summary>A sound's waveform (the Tag is its BankSound), or null while it is decoded (it is asked for by this call).</summary>
    public SoundWaveform? Waveform(AssetKey key, object? tag) => key.Kind == AssetKind.Sound ? Request(key, tag, false).Waveform : null;

    private Slot Request(AssetKey key, object? tag, bool imported)
    {
        var window = LunaWindow.Instance;
        CheckLevel(window.Level);
        int frame = ImGui.GetFrameCount();
        if (frame != _frame) { _frame = frame; _order = 0; }
        if (!_slots.TryGetValue(key, out var slot)) _slots[key] = slot = new Slot(key);
        if (slot.LastFrame < frame - 1) slot.Episode = frame;
        if (slot.LastFrame != frame) slot.Order = _order++;
        slot.LastFrame = frame;
        slot.Tag = tag ?? slot.Tag;
        slot.Imported |= imported;
        return slot;
    }

    public State StateOf(AssetKey key)
    {
        if (!Supports(key.Kind) || !_slots.TryGetValue(key, out var slot)) return Supports(key.Kind) ? State.Pending : State.None;
        return slot.Stage switch { Stage.Ready => State.Ready, Stage.Failed => State.Unavailable, _ => State.Pending };
    }

    /// <summary>Thumbnails asked for and not ready yet.</summary>
    public int Pending => _slots.Values.Count(s => s.Stage is not (Stage.Ready or Stage.Failed));

    /// <summary>Drops an asset's thumbnail (it was removed or changed).</summary>
    public void Forget(AssetKey key)
    {
        if (_slots.Remove(key, out var slot)) Retire(slot);
    }

    /// <summary>Drops every thumbnail, in memory and on disk (the project's and the user's).</summary>
    public void ClearCache()
    {
        foreach (var slot in _slots.Values) Retire(slot);
        _slots.Clear();
        var roots = new List<string> { UserRoot };
        if (ProjectSession.Current is { } project) roots.Add(ProjectRoot(project));
        _ = Task.Run(() =>
        {
            foreach (string root in roots)
                if (!ThumbnailCache.Clear(root)) LunaLog.LogWarn($"[Thumbnails] Could not clear {root}");
        });
    }

    /// <summary>The user's thumbnail folder, used when no project is open.</summary>
    public static string UserRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReLunacy", "cache", "thumbnails");

    /// <summary>A project's thumbnail folder: in its data folder, which nothing saves or installs from.</summary>
    public static string ProjectRoot(LunaProject project) => Path.Combine(project.DataFolder, "cache", "thumbnails");

    /// <summary>The texture of the level's effect set that stands for an effect: the first its emitters and quads use,
    /// its child effects' included; -1 when there is none.</summary>
    public int EffectTexture(AssetIndex index, AssetEntry effect)
    {
        CheckLevel(LunaWindow.Instance.Level);
        int package = (int)effect.Id;
        if (_effectTextures.TryGetValue(package, out int found)) return found;
        found = -1;
        var seen = new HashSet<AssetKey> { effect.Key };
        var queue = new Queue<AssetKey>([effect.Key]);
        while (queue.Count > 0 && found < 0 && seen.Count < 64)
            foreach (var use in index.References.Uses(index.Get(queue.Dequeue())!))
            {
                if (use.Kind == AssetKind.EffectTexture) { found = (int)use.Id; break; }
                if (use.Kind == AssetKind.Effect && index.Get(use) != null && seen.Add(use)) queue.Enqueue(use);
            }
        return _effectTextures[package] = found;
    }

    // --- Pump --------------------------------------------------------------------------------------

    /// <summary>Moves the thumbnails along; once a frame (later calls in the frame do nothing).</summary>
    public void Pump()
    {
        int frame = ImGui.GetFrameCount();
        if (_pumped == frame) return;
        _pumped = frame;
        var window = LunaWindow.Instance;
        CheckLevel(window.Level);
        ReleaseRetired(frame);
        if (window.Level is not { } level || window.AssetManager is not { } assets) return;
        UpdateCache(window, level);
        _sounds = window.fileManager is { } files ? SoundReader.For(files) : null;

        // Finished jobs: a disk hit or a decode is uploaded; a disk miss goes to be rendered.
        foreach (var slot in _slots.Values)
        {
            if (slot.Stage != Stage.Loading || slot.Job is not { IsCompleted: true } job) continue;
            var result = job.IsCompletedSuccessfully ? job.Result : null;
            slot.Job = null;
            if (result is ThumbnailImage image) { slot.Image = image; slot.Stage = Stage.Upload; }
            else if (result is SoundWaveform waveform) { slot.Waveform = waveform; slot.Stage = Stage.Ready; }
            else slot.Stage = slot.DiskOnly ? Stage.Render : Stage.Failed;
        }

        // Asks whose tile went away.
        var stale = _slots.Values.Where(s => s.Stage is Stage.New or Stage.Render or Stage.Building && s.LastFrame < frame - DropAfterFrames).Select(s => s.Key).ToList();
        foreach (var key in stale) _slots.Remove(key);

        var waiting = _slots.Values.Where(s => s.Stage is not (Stage.Ready or Stage.Failed)).ToList();
        waiting.Sort((a, b) => a.Episode != b.Episode ? b.Episode.CompareTo(a.Episode) : a.Order.CompareTo(b.Order));

        int jobs = waiting.Count(s => s.Stage == Stage.Loading);
        foreach (var slot in waiting)
        {
            if (jobs >= MaxJobs) break;
            if (slot.Stage != Stage.New) continue;
            try { Start(slot, level, assets); }
            catch (Exception e) { Fail(slot, e); }
            if (slot.Stage == Stage.Loading) jobs++;
        }

        int uploads = 0;
        foreach (var slot in waiting)
        {
            if (uploads >= UploadsPerFrame) break;
            if (slot.Stage != Stage.Upload || slot.Image is not { } image) continue;
            slot.Texture = Upload(window.GraphicsDevice, image);
            slot.Image = null;
            slot.Stage = Stage.Ready;
            uploads++;
        }

        RenderSome(window, level, assets, waiting, frame);
        Evict(frame);
    }

    private void RenderSome(LunaWindow window, LevelData level, AssetManager assets, List<Slot> waiting, int frame)
    {
        // Textures a render samples must be on the GPU first.
        if (assets.HasPendingUploads) return;
        var clock = Stopwatch.StartNew();
        int renders = 0;
        bool read = false;
        foreach (var slot in waiting)
        {
            if (renders >= MaxRendersPerFrame || clock.Elapsed.TotalMilliseconds > RenderBudgetMs || assets.HasPendingUploads) break;
            if (slot.Stage is not (Stage.Render or Stage.Building) || slot.LastFrame < frame - 1) continue;
            try
            {
                if (slot.Stage == Stage.Render)
                {
                    bool needsRead = NeedsRead(slot.Key, level);
                    if (needsRead && (read || frame - slot.Episode < ReadAfterFrames)) continue;
                    read |= needsRead;
                    slot.Scene = Gather(slot, level, assets);
                    if (slot.Scene == null) { slot.Stage = Stage.Failed; continue; }
                    slot.Stage = Stage.Building;
                    // Its textures were only just queued: drawn once they are up.
                    if (assets.HasPendingUploads) break;
                }
                var image = Render(window, assets, slot.Scene!, slot.Key.Kind);
                slot.Scene = null;
                renders++;
                if (image == null) { slot.Stage = Stage.Failed; continue; }
                slot.Image = image;
                slot.Texture = Upload(window.GraphicsDevice, image);
                slot.Image = null;
                slot.Stage = Stage.Ready;
                Store(slot, image);
            }
            catch (Exception e)
            {
                slot.Scene = null;
                Fail(slot, e);
            }
        }
    }

    private static void Fail(Slot slot, Exception e)
    {
        slot.Stage = Stage.Failed;
        LunaLog.LogError($"[Thumbnails] {slot.Key} failed: {e.Message}");
    }

    // --- Level, cache folder, stamps -----------------------------------------------------------------

    private void CheckLevel(object? level)
    {
        if (ReferenceEquals(level, _level)) return;
        _level = level;
        foreach (var slot in _slots.Values) Retire(slot);
        _slots.Clear();
        _cache = null;
        _cacheRoot = null;
        _effectTextures.Clear();
        _stampFor = default;
    }

    private void UpdateCache(LunaWindow window, LevelData level)
    {
        var project = ProjectSession.Current;
        string root = project != null ? ProjectRoot(project) : UserRoot;
        string? source = window.fileManager?.SourcePath;
        if (string.IsNullOrEmpty(source)) { _cache = null; return; }
        if (_cache == null || _cacheRoot != root)
        {
            _cacheRoot = root;
            _cache = new ThumbnailCache(root, level.GameDefinition?.Id.ToString() ?? "unknown", source, Size);
        }
        // The level's files, and what the project holds over them: a save or an install makes new thumbnails.
        if (_stampFor.Project != project || _stampFor.Revision != (project?.Revision ?? -1) || _levelStamp == 0)
        {
            _stampFor = (project, project?.Revision ?? -1);
            ulong stamp = ThumbnailCache.LevelFilesStamp(source);
            if (project != null)
                foreach (var archive in project.Archives())
                    foreach (var entry in archive.Entries)
                        stamp = ThumbnailCache.Combine(ThumbnailCache.Combine(stamp, entry.Path), entry.Sha256);
            _levelStamp = stamp;
        }
    }

    /// <summary>The stamp of a rendered thumbnail: the level's, the asset's place in it and the look of the render.</summary>
    private ulong RenderStamp(AssetKey key, LevelData level, object? tag)
    {
        var settings = Program.Settings;
        ulong stamp = ThumbnailCache.Combine(_levelStamp, key.Id);
        stamp = ThumbnailCache.Combine(stamp, $"{settings.AssetViewerBackground}|{settings.EnableLighting}|{settings.BackfaceCulling}");
        switch (key.Kind)
        {
            case AssetKind.Moby when level.MobyLibrary.TryGetPointer(key.Id, out var pointer):
            case AssetKind.Tie when level.TieLibrary.TryGetPointer(key.Id, out pointer):
                stamp = ThumbnailCache.Combine(ThumbnailCache.Combine(stamp, pointer.offset), pointer.length);
                break;
            case AssetKind.Shrub when level.Shrubs.TryGetValue(key.Id, out var shrub):
                stamp = ThumbnailCache.Combine(ThumbnailCache.Combine(stamp, shrub.SourcePath), shrub.MaterialTUID);
                break;
            case AssetKind.UFrag:
                // Its id is its zone's position in the loaded region.
                stamp = ThumbnailCache.Combine(stamp, level.Region?.Name);
                break;
            case AssetKind.Shader:
                stamp = ThumbnailCache.Combine(stamp, "shader viewer sphere");
                break;
        }
        return stamp;
    }

    // --- Starting ----------------------------------------------------------------------------------

    private void Start(Slot slot, LevelData level, AssetManager assets)
    {
        var key = slot.Key;
        var cache = _cache;
        switch (key.Kind)
        {
            case AssetKind.Sound:
                StartWaveform(slot, cache);
                return;
            case AssetKind.Texture:
                if (!level.AllTextures.TryGetValue(key.Id, out var texture)) { slot.Stage = Stage.Failed; return; }
                StartDecode(slot, cache, () => DecodeTexture(texture), () => TextureStamp(texture));
                return;
            case AssetKind.EffectTexture:
            {
                if (LevelEffectPlayer.Current is not { } effects || effects.Textures[(int)key.Id] is not { } vfx) { slot.Stage = Stage.Failed; return; }
                var set = effects.Textures;
                int index = (int)key.Id;
                StartDecode(slot, cache, () => set.DecodeRgba(index, out int w, out int h) is { } rgba && w > 0 && h > 0
                    ? ThumbnailCache.Fit(rgba, w, h, Size) : null,
                    () => vfx.Rgba != null ? ThumbnailCache.Hash(vfx.Rgba) : ThumbnailCache.Combine(vfx.Hash, TextureStamp(vfx.Texture)));
                return;
            }
        }

        // Rendered: looked for on disk first.
        slot.Cacheable = cache != null && !slot.Imported && !IsPendingClass(key, level);
        if (!slot.Cacheable) { slot.Stage = Stage.Render; return; }
        slot.Stamp = RenderStamp(key, level, slot.Tag);
        ulong stamp = slot.Stamp;
        slot.DiskOnly = true;
        slot.Stage = Stage.Loading;
        slot.Job = Task.Run<object?>(() => cache!.TryLoad(key, stamp));
    }

    /// <summary>A worker job: the waveform from disk, or decoded (a stream) or rendered (a Scream sound), written back.</summary>
    private void StartWaveform(Slot slot, ThumbnailCache? cache)
    {
        if (slot.Tag is not BankSound sound || _sounds is not { } sounds) { slot.Stage = Stage.Failed; return; }
        var key = slot.Key;
        ulong stamp = ThumbnailCache.Combine(ThumbnailCache.Combine(_levelStamp, $"{sound.Bank}|{sound.BankIndex}|{sound.Name}"), (ulong)SoundWaveform.DefaultBuckets);
        slot.DiskOnly = false;
        slot.Stage = Stage.Loading;
        slot.Job = Task.Run<object?>(() =>
        {
            if (cache?.TryLoadBytes(key, stamp, ".wave") is { } bytes && SoundWaveform.Decode(bytes) is { } hit) return hit;
            SoundWaveform? waveform;
            lock (SoundLock)
            {
                var source = SoundReader.Open(sounds, sound.Bank, sound.BankIndex);
                waveform = source == null ? null
                    : SoundWaveform.Render(source, source is PcmSource ? StreamSeconds : WaveformSeconds);
            }
            if (waveform != null) cache?.StoreBytes(key, stamp, ".wave", waveform.Encode());
            return waveform;
        });
    }

    /// <summary>A worker job: the stamp, the disk, and on a miss the decode, written back.</summary>
    private static void StartDecode(Slot slot, ThumbnailCache? cache, Func<ThumbnailImage?> decode, Func<ulong> stamp)
    {
        var key = slot.Key;
        bool cacheable = cache != null && !slot.Imported;
        slot.DiskOnly = false;
        slot.Stage = Stage.Loading;
        slot.Job = Task.Run<object?>(() =>
        {
            ulong s = cacheable ? stamp() : 0;
            if (cacheable && cache!.TryLoad(key, s) is { } hit) return hit;
            var image = decode();
            if (image != null && cacheable) cache!.Store(key, s, image);
            return image;
        });
    }

    private void Store(Slot slot, ThumbnailImage image)
    {
        if (!slot.Cacheable || _cache is not { } cache) return;
        var key = slot.Key;
        ulong stamp = slot.Stamp;
        _ = Task.Run(() => cache.Store(key, stamp, image));
    }

    private static ulong TextureStamp(ITexture texture)
    {
        ulong stamp = ThumbnailCache.Combine(0, ((ulong)texture.Width << 32) | texture.Height);
        stamp = ThumbnailCache.Combine(stamp, $"{texture.Format}|{texture.MipmapCount}");
        return ThumbnailCache.Hash(texture.GetPixelData(), stamp);
    }

    /// <summary>Decoded from the smallest level that covers the thumbnail, where the texture has its own mips.</summary>
    private static ThumbnailImage? DecodeTexture(ITexture texture)
    {
        var levels = TextureUploadPreparer.Prepare(texture, false, Size);
        if (levels == null || levels.Levels.Length == 0) return null;
        return ThumbnailCache.Fit(levels.Levels[0], (int)levels.Width, (int)levels.Height, Size, flipRows: true, opaque: true);
    }

    private static bool IsPendingClass(AssetKey key, LevelData level) => key.Kind switch
    {
        AssetKind.Moby => level.MobyLibrary.IsPending(key.Id),
        AssetKind.Tie => level.TieLibrary.IsPending(key.Id),
        _ => false,
    };

    private static bool NeedsRead(AssetKey key, LevelData level) => key.Kind switch
    {
        AssetKind.Moby => !level.Mobys.ContainsKey(key.Id),
        AssetKind.Tie => !level.Ties.ContainsKey(key.Id),
        _ => false,
    };

    // --- Rendering ---------------------------------------------------------------------------------

    /// <summary>What the slot's render draws; its textures are queued for upload here. Null when there is nothing.</summary>
    private Scene? Gather(Slot slot, LevelData level, AssetManager assets)
    {
        var scene = new Scene();
        var materialSlots = new Dictionary<RenderMaterial, int>(ReferenceEqualityComparer.Instance);
        var geometrySlots = new Dictionary<int, int>();

        int Material(RenderMaterial material)
        {
            if (materialSlots.TryGetValue(material, out int at)) return at;
            scene.Materials.Add(material);
            return materialSlots[material] = scene.Materials.Count - 1;
        }

        void Registered(RenderMesh mesh, RenderMaterial material, Matrix4x4 world)
        {
            if (!VulkanSceneCapture.TryGet(mesh, out int gi)) return;
            if (!geometrySlots.TryGetValue(gi, out int geometry))
            {
                scene.Vertices.Add(VulkanSceneCapture.VertexData[gi]);
                scene.Indices.Add(VulkanSceneCapture.Indices[gi]);
                geometrySlots[gi] = geometry = scene.Vertices.Count - 1;
            }
            scene.Instances.Add((geometry, Material(material), world));
        }

        void Detached(IEnumerable<IMesh> meshes)
        {
            using var low = assets.LowResolutionBuilds();
            foreach (var (vertices, indices, material) in assets.BuildDetachedGeometry(meshes))
            {
                scene.Vertices.Add(vertices);
                scene.Indices.Add(indices);
                scene.Instances.Add((scene.Vertices.Count - 1, Material(material), Matrix4x4.Identity));
            }
        }

        var key = slot.Key;
        switch (key.Kind)
        {
            case AssetKind.Moby:
                if (assets.Mobys.TryGetValue(key.Id, out var models))
                    foreach (var model in models) foreach (var mesh in model.Meshes) Registered(mesh, mesh.Material, Matrix4x4.Identity);
                else if (level.MobyLibrary.Get(key.Id) is { } moby)
                    Detached(moby.Bangles.SelectMany(b => b.Meshes));
                break;
            case AssetKind.Tie:
                if (assets.Ties.TryGetValue(key.Id, out var tieModel))
                    foreach (var mesh in tieModel.Meshes) Registered(mesh, mesh.Material, Matrix4x4.Identity);
                else if (level.TieLibrary.Get(key.Id) is { } tie)
                    Detached(tie.Meshes);
                break;
            case AssetKind.Shrub:
                if (level.Shrubs.TryGetValue(key.Id, out var shrub)) Detached(shrub.Meshes);
                break;
            case AssetKind.UFrag:
            {
                if (slot.Tag is not IUFrag ufrag) return null;
                var entity = EntityManager.Singleton.AllEntities().OfType<EntityUFrag>().FirstOrDefault(e => ReferenceEquals(e.UFrag, ufrag));
                if (entity == null) return null;
                foreach (var (mesh, material, world, _) in entity.GetRenderablesForVk()) Registered(mesh, material, world);
                break;
            }
            case AssetKind.Foliage:
            {
                var entity = EntityManager.Singleton.AllEntities().OfType<EntityFoliage>().FirstOrDefault(e => e.BaseFoliage.Id == key.Id);
                if (entity == null) return null;
                // Cards turn to the camera in the shader: their anchors alone undersize them.
                scene.Bounds = entity.WorldBoundingSphere;
                foreach (var (mesh, material, world, _) in entity.GetRenderablesForVk()) Registered(mesh, material, world);
                break;
            }
            case AssetKind.Shader:
            {
                if (LunaWindow.Instance.Reader?.FindShaderMaterial(key.Id) is not { } material) return null;
                RenderMaterial built;
                using (assets.LowResolutionBuilds()) built = assets.GetOrBuildMaterial(material);
                var (vertices, indices) = ShaderViewerFrame.SphereGeometry();
                scene.Vertices.Add(vertices);
                scene.Indices.Add(indices);
                scene.Instances.Add((0, Material(built), Matrix4x4.Identity));
                scene.Bounds = new Vector4(0f, 0f, 0f, 1f);
                break;
            }
        }
        return scene.Instances.Count > 0 ? scene : null;
    }

    private ThumbnailImage? Render(LunaWindow window, AssetManager assets, Scene scene, AssetKind kind)
    {
        var (centre, radius) = Bounds(scene);
        var materials = scene.Materials.Select(m => VkMaterialBuilder.Build(m, assets)).ToList();
        object owner = kind == AssetKind.Shader ? SceneEntityKind.Moby : null!;
        var instances = scene.Instances
            .Select(i => (i.Geometry, i.Material, i.World, new Vector4(0f, 0f, 0f, 1e9f), owner, -1f, 0u))
            .ToList();

        var gd = window.GraphicsDevice;
        using var renderer = new VulkanRenderer(
            gd, scene.Vertices, scene.Indices, materials, instances, null, assets.EnvironmentCubemapView?.Target, Size, Size,
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
        return ReadBack(gd, renderer.ColorTexture);
    }

    /// <summary>The render target's pixels as RGBA8, opaque.</summary>
    private static ThumbnailImage? ReadBack(GraphicsDevice gd, Texture source)
    {
        var format = source.Format;
        if (format is not (PixelFormat.R16_G16_B16_A16_Float or PixelFormat.R8_G8_B8_A8_UNorm or PixelFormat.B8_G8_R8_A8_UNorm)) return null;
        var factory = gd.ResourceFactory;
        using var staging = factory.CreateTexture(TextureDescription.Texture2D(source.Width, source.Height, 1, 1, format, TextureUsage.Staging));
        using (var commands = factory.CreateCommandList())
        {
            commands.Begin();
            commands.CopyTexture(source, staging);
            commands.End();
            gd.SubmitCommands(commands);
            gd.WaitForIdle();
        }

        int w = (int)source.Width, h = (int)source.Height;
        var rgba = new byte[w * h * 4];
        var map = gd.Map(staging, MapMode.Read);
        try
        {
            unsafe
            {
                for (int y = 0; y < h; y++)
                {
                    byte* row = (byte*)map.Data + y * map.RowPitch;
                    for (int x = 0; x < w; x++)
                    {
                        int o = (y * w + x) * 4;
                        if (format == PixelFormat.R16_G16_B16_A16_Float)
                        {
                            var px = (Half*)row + x * 4;
                            rgba[o] = ToByte((float)px[0]);
                            rgba[o + 1] = ToByte((float)px[1]);
                            rgba[o + 2] = ToByte((float)px[2]);
                        }
                        else
                        {
                            byte* px = row + x * 4;
                            bool bgra = format == PixelFormat.B8_G8_R8_A8_UNorm;
                            rgba[o] = px[bgra ? 2 : 0];
                            rgba[o + 1] = px[1];
                            rgba[o + 2] = px[bgra ? 0 : 2];
                        }
                        rgba[o + 3] = 255;
                    }
                }
            }
        }
        finally
        {
            gd.Unmap(staging);
        }
        return new ThumbnailImage(w, h, rgba);
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)(value * 255f + 0.5f), 0, 255);

    /// <summary>The scene's bounding sphere: given (foliage), else around its vertices as placed.</summary>
    private static (Vector3 Centre, float Radius) Bounds(Scene scene)
    {
        if (scene.Bounds is { W: > 0f } given) return (new Vector3(given.X, given.Y, given.Z), given.W);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var (geometry, _, world) in scene.Instances)
        {
            var data = scene.Vertices[geometry];
            var localMin = new Vector3(float.MaxValue);
            var localMax = new Vector3(float.MinValue);
            for (int i = 0; i + 2 < data.Length; i += VulkanSceneCapture.FloatsPerVertex)
            {
                var p = new Vector3(data[i], data[i + 1], data[i + 2]);
                if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)) continue;
                localMin = Vector3.Min(localMin, p);
                localMax = Vector3.Max(localMax, p);
            }
            if (localMin.X > localMax.X) continue;
            for (int c = 0; c < 8; c++)
            {
                var corner = new Vector3((c & 1) != 0 ? localMax.X : localMin.X, (c & 2) != 0 ? localMax.Y : localMin.Y, (c & 4) != 0 ? localMax.Z : localMin.Z);
                var p = Vector3.Transform(corner, world);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }
        if (min.X > max.X) return (Vector3.Zero, 1f);
        return ((min + max) * 0.5f, (max - min).Length() * 0.5f);
    }

    // --- GPU textures ------------------------------------------------------------------------------

    private static Texture Upload(GraphicsDevice gd, ThumbnailImage image)
    {
        var texture = gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            (uint)image.Width, (uint)image.Height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        gd.UpdateTexture(texture, image.Rgba, 0, 0, 0, (uint)image.Width, (uint)image.Height, 1, 0, 0);
        return texture;
    }

    /// <summary>Above <see cref="MaxResident"/>, the least recently drawn go; never one drawn this frame or the last.</summary>
    private void Evict(int frame)
    {
        int waveforms = _slots.Values.Count(s => s.Waveform != null);
        if (waveforms > MaxWaveforms)
            foreach (var slot in _slots.Values.Where(s => s.Waveform != null && s.LastFrame < frame - 1).OrderBy(s => s.LastFrame).Take(waveforms - MaxWaveforms).ToList())
                _slots.Remove(slot.Key);

        int resident = _slots.Values.Count(s => s.Texture != null);
        if (resident <= MaxResident) return;
        foreach (var slot in _slots.Values.Where(s => s.Texture != null && s.LastFrame < frame - 1).OrderBy(s => s.LastFrame).Take(resident - MaxResident).ToList())
        {
            Retire(slot);
            _slots.Remove(slot.Key);
        }
    }

    private void Retire(Slot slot)
    {
        if (slot.Texture != null) _retired.Add((slot.Texture, ImGui.GetFrameCount()));
        slot.Texture = null;
        slot.Scene = null;
    }

    /// <summary>Frees dropped textures two frames on: this frame's draw list may still name them.</summary>
    private void ReleaseRetired(int frame)
    {
        for (int i = _retired.Count - 1; i >= 0; i--)
        {
            if (frame - _retired[i].Frame < 2) continue;
            LunaWindow.Instance.imGuiController.ReleaseImGuiBinding(_retired[i].Texture);
            _retired[i].Texture.Dispose();
            _retired.RemoveAt(i);
        }
    }
}
