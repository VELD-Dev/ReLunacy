// Ported from ReLunacy Resurgence by Fianshoo (GPL-3.0), adapted to this editor's scene and renderer.
using System.Buffers.Binary;
using System.Numerics;
using NeoVeldrid;
using ReLunacy.Engine.Assets.Effects;
using ReLunacy.Engine.Loading.Readers;
using ReLunacy.Engine.Rendering;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Engine.Rendering.Vulkan;
using ReLunacy.Engine.Scene;

namespace ReLunacy.Utility;

/// <summary>
/// Plays, in the level view, the effect of every FxController a level places: Tools of Destruction and Quest for
/// Booty (class 384, read from gameplay.dat; see <see cref="LevelEffects"/>), and A Crack in Time (class 0x3D0, the
/// scene's controller entities, so moving or hiding one moves or hides its effect). Each nearby controller runs its
/// own <see cref="EffectSimulator"/> at its position and orientation (the game hands the effect the controller's
/// matrix); the sprites go to the renderer's depth-tested effect pass (VulkanRenderer.Effects.cs). Only the
/// <see cref="MaxInstances"/> nearest whose reach comes within <see cref="MaxDistance"/> of the camera play, so a
/// level with hundreds of placed effects costs what the camera can see, while a nebula hundreds of units wide still
/// shows from afar. A continuous effect starts prewarmed, as it would look after playing a while.
///
/// The level's effect files are read once per level: their bytes on the main thread (the level's archives are
/// not shared across threads), the parsing and texture decoding in the background, the upload here.
/// </summary>
internal static class LevelEffectPlayer
{
    private sealed class Instance
    {
        public required EffectSimulator Simulator;
        public required EffectPackage Package;
        public required Random Random;
        public Quaternion Rotation;
        public float SinceStart;
        public float Wait;
    }

    /// <summary>One controller that can play this frame: what it plays, where, and how often.</summary>
    private readonly record struct Placed(object Owner, Vector3 Position, Quaternion Rotation, EffectPackage Package,
        float RepeatMin, float RepeatMax, int Seed, bool Paused = false);

    /// <summary>What the background load hands back: the effect data and every texture's mips, ready to upload, or
    /// why there is none.</summary>
    private sealed record Loaded(LevelEffects? Effects, TextureLevels?[] Textures, string? Message,
        Dictionary<EffectPackage, float>? Reaches = null);

    public static float MaxDistance = 150f;
    public static int MaxInstances = 48;

    // A Crack in Time's FxController class data: the Tags key, start active, retrigger delay min / max.
    private const int KeyOffset = 0x14, StartActiveOffset = 0x1C, RepeatMinOffset = 0x24, RepeatMaxOffset = 0x28;

    private static LevelData? _level;
    private static Task<Loaded>? _loading;
    private static Loaded? _pendingUpload;
    private static LevelEffects? _effects;
    private static bool _crackInTime;
    private static (OldEffectController Controller, EffectResolution Resolution)[] _placed = [];
    private static readonly Dictionary<uint, EffectResolution> Resolved = [];
    private static readonly List<GpuTexture?> Gpu = [];
    private static object _textureToken = new();
    private static GraphicsDevice? _device;

    private static readonly Dictionary<object, Instance> Playing = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<EffectPackage, float> Lengths = new(ReferenceEqualityComparer.Instance);
    // How far each effect's sprites reach from its controller, measured once per effect.
    private static readonly Dictionary<EffectPackage, float> Reaches = new(ReferenceEqualityComparer.Instance);
    private static readonly List<Placed> Available = [];
    private static readonly List<(int Slot, float Distance)> Candidates = [];
    private static readonly HashSet<object> Keep = new(ReferenceEqualityComparer.Instance);
    private static readonly List<EffectBillboard> Sprites = [];
    private static readonly List<EffectBillboard> Local = [];
    private static readonly List<EffectLightMarker> Lights = [];
    private static readonly EffectSpriteBatch Batch = new();
    private static bool _cleared = true;

    // Editor-only play state per controller (the entity, or the old-engine controller): true plays, false pauses,
    // absent follows the game's rule, which is the controller's start-active flag. Never saved to the level.
    private static readonly Dictionary<object, bool> Forced = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<object> Restarts = new(ReferenceEqualityComparer.Instance);

    /// <summary>Whether <paramref name="controller"/> plays now: the editor's choice, else its start-active flag.</summary>
    public static bool IsPlaying(object controller) =>
        Forced.TryGetValue(controller, out bool forced) ? forced : StartsActive(controller);

    /// <summary>Plays a paused or inactive controller, or pauses a playing one.</summary>
    public static void Toggle(object controller) => Forced[controller] = !IsPlaying(controller);

    /// <summary>Plays <paramref name="controller"/>'s effect again from the start.</summary>
    public static void Restart(object controller)
    {
        Forced[controller] = true;
        Restarts.Add(controller);
    }

    private static bool StartsActive(object controller) => controller switch
    {
        OldEffectController old => old.StartsActive,
        EntityController { Controller.Settings: { Length: > StartActiveOffset + 3 } s } =>
            BinaryPrimitives.ReadUInt32BigEndian(s.AsSpan(StartActiveOffset)) != 0,
        _ => false,
    };

    public static bool Enabled => !Program.Settings.HideEffects;

    /// <summary>Whether the open level has effects the view can play.</summary>
    public static bool HasEffects => _placed.Length > 0 || (_crackInTime && _effects?.Conduit != null);

    /// <summary>The open level's effect data, read on first use; null while it is read and where there is none.</summary>
    public static LevelEffects? Current
    {
        get
        {
            EnsureLevel();
            return _effects;
        }
    }

    /// <summary>Whether the open level's effects are still being read.</summary>
    public static bool IsLoading => _loading != null;

    /// <summary>The open level's effect textures, slot = VFX index (null where one could not be decoded).</summary>
    public static IReadOnlyList<GpuTexture?> Textures => Gpu;

    /// <summary>Changes whenever <see cref="Textures"/> does: a renderer bound to another token rebinds.</summary>
    public static object TextureToken => _textureToken;

    /// <summary>Reads the open level's effects and uploads their textures on <paramref name="device"/>, for a
    /// view that plays effects without <see cref="Update"/> (the Asset Viewer).</summary>
    public static void Prepare(GraphicsDevice device)
    {
        _device = device;
        EnsureLevel();
        Upload();
    }

    /// <summary>What an FxController key plays in the open level (cached per key), or null without effect data.</summary>
    public static EffectResolution? ResolveKey(uint key)
    {
        if (Current is not { } effects) return null;
        if (!Resolved.TryGetValue(key, out EffectResolution? resolution))
            Resolved[key] = resolution = effects.Resolve(key);
        return resolution;
    }

    /// <summary>Every FxController of the open level that plays <paramref name="package"/>, with its key: the scene's
    /// controllers in A Crack in Time (with their region), else the gameplay.dat ones by index (no region, no entity).</summary>
    public static List<(EntityRegion? Region, EntityController? Entity, int Index, uint Key)> ControllersOf(EffectPackage package)
    {
        var found = new List<(EntityRegion?, EntityController?, int, uint)>();
        if (Current == null) return found;
        if (!_crackInTime)
        {
            foreach (var (controller, resolution) in _placed)
                if (ReferenceEquals(resolution.Package, package)) found.Add((null, null, controller.Index, controller.Key));
            return found;
        }
        foreach (EntityRegion region in EntityManager.Singleton.Regions)
            foreach (Entity entity in region.ControllerInstances.Entities)
            {
                if (entity is not EntityController c || c.Controller.Class != LevelEffects.CrackInTimeFxControllerClass
                    || c.Controller.Settings.Length < KeyOffset + 4) continue;
                uint key = BinaryPrimitives.ReadUInt32BigEndian(c.Controller.Settings.AsSpan(KeyOffset));
                if (key != 0 && ReferenceEquals(ResolveKey(key)?.Package, package)) found.Add((region, c, c.Controller.Index, key));
            }
        return found;
    }

    /// <summary>Steps every nearby effect and hands this frame's sprites to <paramref name="renderer"/>.</summary>
    public static void Update(VulkanRenderer renderer, EditorCamera camera, float deltaTime, GraphicsDevice device)
    {
        _device = device;
        EnsureLevel();
        Upload();
        Available.Clear();
        if (Enabled && _effects != null) CollectPlaced();
        if (Available.Count == 0)
        {
            if (!_cleared) { Batch.Clear(); renderer.SetEffectSprites(Batch); _cleared = true; }
            Playing.Clear();
            return;
        }
        LevelEffects effects = _effects!;

        Vector3 eye = camera.Position;
        Candidates.Clear();
        for (int i = 0; i < Available.Count; i++)
        {
            // Distance to the nearest edge of the effect, so a wide one plays while any of it can be near.
            float distance = Vector3.Distance(Available[i].Position, eye) - Reach(effects, Available[i].Package);
            if (distance <= MaxDistance) Candidates.Add((i, distance));
        }
        Candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        if (Candidates.Count > MaxInstances) Candidates.RemoveRange(MaxInstances, Candidates.Count - MaxInstances);

        Keep.Clear();
        foreach (var (slot, _) in Candidates) Keep.Add(Available[slot].Owner);
        foreach (object gone in Playing.Keys.Where(k => !Keep.Contains(k)).ToList())
            Playing.Remove(gone);

        Sprites.Clear();
        Lights.Clear();
        float dt = Math.Clamp(deltaTime, 0f, 0.1f);
        foreach (var (slot, _) in Candidates)
        {
            Placed placed = Available[slot];
            // A controller given another effect starts over with it.
            if (!Playing.TryGetValue(placed.Owner, out Instance? instance) || !ReferenceEquals(instance.Package, placed.Package))
            {
                var random = new Random(placed.Seed);
                instance = new Instance
                {
                    Random = random,
                    Package = placed.Package,
                    Rotation = placed.Rotation,
                    // The controller's rotation goes in as the spawn frame; its translation is added below.
                    Simulator = new EffectSimulator(effects.Library, placed.Package,
                        new EffectSimulationSettings { MaxParticles = 1500, SpawnFrame = Matrix4x4.CreateFromQuaternion(placed.Rotation) },
                        placed.Seed),
                };
                instance.Wait = RepeatDelay(effects, placed, random);
                // Staggered, so a row of identical controllers does not flash in step.
                instance.SinceStart = instance.Wait * random.NextSingle();
                // A continuous effect has been playing since the level started: show it filled up.
                instance.Simulator.Prewarm();
                Playing[placed.Owner] = instance;
            }
            else if (instance.Rotation != placed.Rotation)
            {
                // Turned in the editor: the effect follows without restarting.
                instance.Rotation = placed.Rotation;
                instance.Simulator.SetSpawnFrame(Matrix4x4.CreateFromQuaternion(placed.Rotation));
            }

            EffectSimulator sim = instance.Simulator;
            if (Restarts.Remove(placed.Owner))
            {
                // A restart shows the effect from its first frame, so it is not prewarmed.
                sim.Reset();
                instance.SinceStart = 0f;
                instance.Wait = RepeatDelay(effects, placed, instance.Random);
            }
            // Paused: the simulation stays as it is and its sprites stay visible.
            if (!placed.Paused)
            {
            sim.Step(dt);
            instance.SinceStart += dt;
            // A bounded effect plays again after the controller's retrigger delay, drawn per play, or 1 s after it
            // ends when it has none: in game that one plays once, at activation, before the camera arrives.
            if (sim.Finished && instance.SinceStart >= instance.Wait)
            {
                sim.Reset();
                instance.SinceStart = 0f;
                instance.Wait = RepeatDelay(effects, placed, instance.Random);
            }
            }

            Local.Clear();
            int lightStart = Lights.Count;
            sim.Collect(Local, Lights);
            // The simulator already works in world axes with the controller's rotation: only move it into place.
            Matrix4x4 world = Matrix4x4.CreateTranslation(placed.Position);
            foreach (EffectBillboard local in Local)
                Sprites.Add(EffectSpace.Transform(local, world));
            for (int i = lightStart; i < Lights.Count; i++)
            {
                EffectLightMarker l = Lights[i];
                l.Position = Vector3.Transform(l.Position, world);
                Lights[i] = l;
            }
        }

        if (!ReferenceEquals(renderer.EffectTextureOwner, _textureToken))
            renderer.SetEffectTextures(device, Gpu.Select(g => g?.DeviceTexture).ToList(), _textureToken);

        Matrix4x4 view = camera.GetView();
        Batch.Clear();
        Batch.Add(Sprites, view);
        // Effect lights have no lighting pass here: a faint glow shows where they are.
        foreach (EffectLightMarker light in Lights)
            Batch.AddGlow(light.Position, Math.Clamp(light.Radius * 0.3f, 0.05f, 3f),
                new Vector4(light.Color.X, light.Color.Y, light.Color.Z, 0.3f), view);
        renderer.SetEffectSprites(Batch);
        _cleared = Batch.VertexCount == 0;
    }

    /// <summary>Every controller that plays now, into <see cref="Available"/>.</summary>
    private static void CollectPlaced()
    {
        if (!_crackInTime)
        {
            for (int i = 0; i < _placed.Length; i++)
            {
                var (controller, resolution) = _placed[i];
                // One that starts inactive waits for a script, so the game shows nothing there during play.
                if (resolution.Package == null || !(controller.StartsActive || Forced.ContainsKey(controller))) continue;
                Available.Add(new Placed(controller, controller.Position, controller.Rotation, resolution.Package,
                    controller.RepeatMin, controller.RepeatMax, i * 7919 + 17, !IsPlaying(controller)));
            }
            return;
        }

        // A Crack in Time: the scene's FxControllers, read live, so an edit to one shows at once.
        foreach (EntityRegion region in EntityManager.Singleton.Regions)
        {
            if (!region.allowRender) continue;
            foreach (Entity entity in region.ControllerInstances.Entities)
            {
                if (entity is not EntityController { allowRender: true } c
                    || c.Controller.Class != LevelEffects.CrackInTimeFxControllerClass) continue;
                byte[] settings = c.Controller.Settings;
                if (settings.Length < RepeatMaxOffset + 4) continue;
                uint key = BinaryPrimitives.ReadUInt32BigEndian(settings.AsSpan(KeyOffset));
                // One that starts inactive waits for a script, so the game shows nothing there during play.
                if (key == 0 || !(IsPlaying(c) || Forced.ContainsKey(c))) continue;
                if (ResolveKey(key)?.Package is not { } package) continue;
                float min = Seconds(settings, RepeatMinOffset), max = Seconds(settings, RepeatMaxOffset);
                Available.Add(new Placed(c, c.Transform.Translation, c.Transform.Rotation, package,
                    MathF.Min(min, max), MathF.Max(min, max), (int)(c.Controller.Tuid ^ (c.Controller.Tuid >> 32)) * 7919 + 17, !IsPlaying(c)));
            }
        }
    }

    private static float Seconds(byte[] bytes, int at)
    {
        float value = BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(at));
        return float.IsFinite(value) ? Math.Max(0f, value) : 0f;
    }

    private static float RepeatDelay(LevelEffects effects, Placed placed, Random random)
    {
        if (placed.RepeatMax <= 0f) return 1f + EstimatedLength(effects, placed.Package);
        return placed.RepeatMin + (placed.RepeatMax - placed.RepeatMin) * random.NextSingle();
    }

    /// <summary>How long one play of a bounded effect lasts (0 for a continuous one), cached per package.</summary>
    private static float EstimatedLength(LevelEffects effects, EffectPackage package)
    {
        if (Lengths.TryGetValue(package, out float length)) return length;
        var probe = new EffectSimulator(effects.Library, package, new EffectSimulationSettings { MaxParticles = 1 });
        length = probe.Continuous ? 0f : probe.PlayLength;
        Lengths[package] = length;
        return length;
    }

    /// <summary>How far <paramref name="package"/>'s sprites reach from its origin, cached per package.</summary>
    private static float Reach(LevelEffects effects, EffectPackage package)
    {
        if (Reaches.TryGetValue(package, out float reach)) return reach;
        Reaches[package] = reach = MeasureReach(effects, package);
        return reach;
    }

    private static float MeasureReach(LevelEffects effects, EffectPackage package)
    {
        var probe = new EffectSimulator(effects.Library, package, new EffectSimulationSettings { MaxParticles = 1500 });
        return probe.MeasureReach(Math.Clamp(probe.PlayLength, 2f, 4f));
    }

    /// <summary>Starts reading a newly opened level's effects, and takes them once read.</summary>
    private static void EnsureLevel()
    {
        var window = Core.LunaWindow.Instance;
        if (!ReferenceEquals(_level, window.Level))
        {
            Drop();
            _level = window.Level;
            if (_level != null && window.fileManager is { } files)
            {
                bool crackInTime = _level.GameDefinition?.Id == Engine.Games.GameId.ACrackInTime;
                if (_level.IsOldEngine || crackInTime)
                {
                    // The bytes now, while nothing else reads the archives; the rest off the main thread.
                    var bytes = new Dictionary<string, byte[]?>();
                    foreach (string name in crackInTime ? LevelEffects.CrackInTimeFileNames : LevelEffects.FileNames)
                        bytes[name] = LevelEffects.ReadLevelFile(files, name);
                    string folder = LevelEffects.LevelFolder(files);
                    _crackInTime = crackInTime;
                    _loading = Task.Run(() => Load(bytes, folder, crackInTime));
                }
            }
        }

        if (_loading is not { IsCompleted: true } done) return;
        _loading = null;
        Loaded? loaded = done.IsCompletedSuccessfully ? done.Result : null;
        if (done.Exception is { } failure) LunaLog.LogError($"[Effects] {failure.GetBaseException().Message}");
        if (loaded?.Message is { } message) LunaLog.LogWarn($"[Effects] {message}");
        if (loaded?.Effects is not { } effects) return;

        _effects = effects;
        _placed = [.. effects.Controllers.Select(c => (c, effects.Resolve(c)))];
        if (loaded.Reaches != null)
            foreach (var (package, reach) in loaded.Reaches) Reaches[package] = reach;
        _pendingUpload = loaded;

        var library = effects.Library;
        string placedText = _crackInTime
            ? $"{effects.ControllerKeys().Count} FxController keys"
            : $"{_placed.Count(p => p.Resolution.Package != null)} of {_placed.Length} FxControllers resolve";
        LunaLog.LogInfo($"[Effects] {library.Packages.Count} effects, {library.Emitters.Count} emitters, {library.Quads.Count} quads; {placedText}.");
        if (effects.TextureError is { } textureError) LunaLog.LogWarn($"[Effects] {textureError}");
        if (effects.Conduit == null) LunaLog.LogWarn($"[Effects] fxconduit: {effects.ConduitError}");
    }

    /// <summary>Uploads the read level's textures once the view's device is known.</summary>
    private static void Upload()
    {
        if (_pendingUpload is not { } loaded || _device == null) return;
        _pendingUpload = null;
        int failed = 0;
        foreach (TextureLevels? levels in loaded.Textures)
        {
            GpuTexture? gpu = null;
            try { if (levels != null) gpu = new GpuTexture(_device, levels); }
            catch { gpu = null; }
            if (gpu == null) failed++;
            Gpu.Add(gpu);
        }
        _textureToken = new object();
        if (failed > 0) LunaLog.LogWarn($"[Effects] {failed} of {loaded.Textures.Length} effect textures could not be decoded.");
    }

    private static Loaded Load(Dictionary<string, byte[]?> bytes, string folder, bool crackInTime)
    {
        string? error;
        LevelEffects? effects = crackInTime
            ? LevelEffects.TryLoadCrackInTime(name => bytes.GetValueOrDefault(name), out error)
            : LevelEffects.TryLoad(name => bytes.GetValueOrDefault(name), folder, out error);
        if (effects == null) return new Loaded(null, [], error);
        var textures = new TextureLevels?[effects.Textures.Entries.Count];
        for (int i = 0; i < textures.Length; i++)
        {
            try
            {
                byte[]? rgba = effects.Textures.DecodeRgba(i, out int w, out int h);
                if (rgba != null && w > 0 && h > 0) textures[i] = TextureLevels.Prepare((uint)w, (uint)h, rgba);
            }
            catch { textures[i] = null; }
        }
        // Every placed effect's reach now, off the main thread, so the view does not stall measuring them.
        var reaches = new Dictionary<EffectPackage, float>(ReferenceEqualityComparer.Instance);
        IEnumerable<EffectPackage?> packages = crackInTime
            ? effects.ControllerKeys().Select(k => effects.Resolve(k).Package)
            : effects.Controllers.Select(c => effects.Resolve(c).Package);
        foreach (EffectPackage? package in packages)
        {
            if (package == null || reaches.ContainsKey(package)) continue;
            try { reaches[package] = MeasureReach(effects, package); }
            catch { /* measured on first use instead */ }
        }
        return new Loaded(effects, textures, error, reaches);
    }

    /// <summary>Forgets the previous level's effects; its textures go once the device is idle.</summary>
    private static void Drop()
    {
        if (_device != null)
            foreach (GpuTexture? texture in Gpu)
                if (texture != null) _device.DisposeWhenIdle(texture.DeviceTexture);
        Gpu.Clear();
        _textureToken = new object();
        _effects = null;
        _crackInTime = false;
        _placed = [];
        _loading = null;
        _pendingUpload = null;
        Playing.Clear();
        Forced.Clear();
        Restarts.Clear();
        Lengths.Clear();
        Reaches.Clear();
        Resolved.Clear();
    }
}
