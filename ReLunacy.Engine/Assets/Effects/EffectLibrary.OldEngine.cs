// Ported from ReLunacy Resurgence by Fianshoo (GPL-3.0). "Docs/reverse/..." paths cite that repository.
using System.Numerics;

namespace ReLunacy.Engine.Assets.Effects;

/// <summary>
/// Tools of Destruction's effect.dat (Docs/reverse/TOD_EFFECTS_2026-10-09.md). Same IGHW 0.2 tree as the new engine
/// (every chunk the target of exactly one pointer on the 23 readable levels), but eleven piece lists - one per emitter
/// class, named by the EBOOT (<see cref="OldEngineEffectTypes"/>) - and smaller records:
/// <code>
///   package (0xA8)   +0x00 ptr FxDefFX[n] (n = u32 before the array, also the byte +0xA0)
///   FxDefFX (0x80)   +0x00 delay   +0x20 u16 piece index   +0x24/+0x2C/+0x34 offset Z/X/Y, each followed by its spread
///                    +0x3C type   +0x3D orientation mode   +0x3E/+0x3F tilt min/max   s16 +0x40/+0x42 azimuth min/max
///                    +0x44 scale   +0x6C/+0x70 restart cycle max/min
///   quad (0xE0)      keys +0x00 colour, +0x0C alpha, +0x18 size, +0x24, +0x30 rotation; FxTexs +0x40 (count +0xC4); life +0xCC
///   lite (0xDC)      keys +0x00 colour, +0x0C, +0x18, +0x24, +0x30 (all halved: radii)
///   emitter          keys +0x04 rate, +0x1C life, +0x2C speed, +0x38 radius, +0x44 emitter alpha, +0x50 emitter scale,
///                    +0x5C particle colour, +0x68 alpha, +0x74 size, +0x80 spin (not Beam/Streak);
///                    FxTexs, FxEmtLifeControl, FxEmtSpawnStyleControl, lighting, the class block, FxEmtTurbAccelDirControl
/// </code>
/// What each key track is comes from the EBOOT's load-time conversion of its keys (0x00600254 for every emitter,
/// 0x005FF250 quads, 0x005FFA5C lights: x255 = alpha, x0.5 x256 = size, degrees -> s16 radians = rotation): PROVEN.
/// The FxDefFX fields past the piece type and index are read by analogy with QForce's FxDefFX (same field order,
/// interleaved value/spread offsets, s16 azimuth pair): INFERRED until the SPU job fxvis is run.
/// </summary>
public sealed partial class EffectLibrary
{
    /// <summary>The nine key headers every Tools of Destruction emitter starts with (0x005FDF4C copies them in this order;
    /// 0x00600254 converts them: rate x10, life clamped to [0.001, 65.5] s, radius x256, emitter alpha x255, emitter scale
    /// x256, particle alpha x255, particle size x0.5 x256; the first six take the emitter's period as their time base, the
    /// last four - flagged in a second byte - the particle's life). PROVEN.</summary>
    private static readonly TrackSpec[] OldEmitterTracks =
    [
        new("Spawn rate", 0x04, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Spawn life", 0x1C, KeyKind.Scalar20, EffectTrackDomain.PieceLife),
        new("Spawn speed", 0x2C, KeyKind.Scalar20, EffectTrackDomain.PieceLife),
        new("Spawn radius", 0x38, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Emitter alpha", 0x44, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Emitter scale", 0x50, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Particle colour", 0x5C, KeyKind.Color32, EffectTrackDomain.ParticleLife),
        new("Particle alpha", 0x68, KeyKind.Scalar16, EffectTrackDomain.ParticleLife),
        new("Particle size", 0x74, KeyKind.Scalar16, EffectTrackDomain.ParticleLife),
    ];

    /// <summary>The tenth header, degrees per second, mode at +0x0D (the conversion in 0x006013B8 swaps on that byte).</summary>
    private static readonly TrackSpec OldEmitterSpin = new("Particle spin", 0x80, KeyKind.Rot16, EffectTrackDomain.ParticleLife);

    private static readonly TrackSpec[] OldQuadTracks =
    [
        new("Colour", 0x00, KeyKind.Color32, EffectTrackDomain.PieceLife),
        new("Alpha", 0x0C, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Size", 0x18, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Track 0x24", 0x24, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Rotation", 0x30, KeyKind.Rot16, EffectTrackDomain.PieceLife),
    ];

    /// <summary>Lights: colour, then four tracks the EBOOT halves (radii). +0x0C is 3..10, +0x18 4..30, +0x24/+0x30 are 1.</summary>
    private static readonly TrackSpec[] OldLightTracks =
    [
        new("Colour", 0x00, KeyKind.Color32, EffectTrackDomain.PieceLife),
        new("Inner radius?", 0x0C, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Outer radius?", 0x18, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Track 0x24", 0x24, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Track 0x30", 0x30, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
    ];

    /// <summary>Where the blocks after the keys sit in each emitter class (old-engine offsets). The shared part (textures,
    /// life control, spawn style, lighting) is PROVEN by the per-type preparation (0x006013B8 hands 0x00600254 the life
    /// control at +0x118, or +0x10C for Beam and Streak); the motion block is where its mode byte (9 = gravity on 10 315
    /// of 14 162 distinct emitters) is followed by the 1024 speed cap seven words later in every class: STRONG.</summary>
    private readonly record struct OldEmitterLayout(int Textures, int TextureCount, int Life, int SpawnStyle, int Motion, int ClassBlock);

    private static OldEmitterLayout OldLayoutOf(int type) => type switch
    {
        OldEngineEffectTypes.Beam => new(0x80, 0x104, 0x10C, 0x12C, 0x144, 0x13C),
        OldEngineEffectTypes.Streak => new(0x80, 0x104, 0x10C, 0x12C, 0x168, 0x13C),
        OldEngineEffectTypes.Flame => new(0x8C, 0x110, 0x118, 0x138, 0x150, 0x148),
        OldEngineEffectTypes.Distort => new(0x8C, 0x110, 0x118, 0x138, 0x154, 0x148),
        OldEngineEffectTypes.Swipe => new(0x8C, 0x110, 0x118, 0x138, 0x158, 0x148),
        _ => new(0x8C, 0x110, 0x118, 0x138, 0x14C, 0x148), // CamFacing, Smoke, Trail
    };

    private static EffectLibrary ReadOldEngine(EffectChunkFile file, Dictionary<int, int[]> lists, List<string> validation)
    {
        EffectLayout layout = EffectLayout.ToolsOfDestruction;
        var packages = new List<EffectPackage>();
        var quads = new List<EffectQuad>();
        var lights = new List<EffectLight>();
        var emitters = new List<EffectEmitter>();
        // (list type, index in that list) -> the emitter, for FxDefFX resolution.
        var emitterIndex = new Dictionary<(int Type, int Index), int>();

        // Pieces first, packages last: an FxDefFX then names its emitter by its index in the flattened Emitters list.
        foreach (var (type, records) in lists.OrderBy(kv => kv.Key == OldEngineEffectTypes.Package ? 1 : 0).ThenBy(kv => kv.Key))
        {
            int size = OldEngineEffectTypes.RecordSize(type);
            if (size < 0)
            {
                if (records.Length > 0)
                    validation.Add($"{OldEngineEffectTypes.Name(type)} ({records.Length} records) is skipped by the game's binder - kept out of the model.");
                continue;
            }
            for (int i = 0; i < records.Length; i++)
            {
                int at = records[i];
                if (at == 0) { validation.Add($"List {type} entry {i} is null."); continue; }
                int chunk = file.ChunkIndexAt(at);
                if (chunk >= 0 && file.Chunks[chunk].Length != size)
                    validation.Add($"{OldEngineEffectTypes.Name(type)} {i} is {file.Chunks[chunk].Length} bytes (the binder copies {size}).");
                switch (type)
                {
                    case OldEngineEffectTypes.Package: packages.Add(ReadOldPackage(file, i, at, emitterIndex)); break;
                    case OldEngineEffectTypes.Quads: quads.Add(ReadOldQuad(file, i, at)); break;
                    case OldEngineEffectTypes.Lites: lights.Add(ReadOldLight(file, i, at)); break;
                    default:
                        emitterIndex[(type, i)] = emitters.Count;
                        emitters.Add(ReadOldEmitter(file, type, emitters.Count, at));
                        break;
                }
            }
        }

        var globals = new Dictionary<int, int>();
        int space = 0;
        EffectChunkFile.Chunk? table = file.ChunksWithId(0x500).Cast<EffectChunkFile.Chunk?>().FirstOrDefault();
        if (table is { } t && t.Length >= 8)
        {
            space = (int)file.U32(t.Offset);
            int n = Math.Min(space, (t.Length - 8) / 4);
            for (int id = 0; id < n; id++)
            {
                uint pkg = file.U32(t.Offset + 8 + 4 * id);
                if (pkg == 0xFFFFFFFF) continue;
                globals[id] = (int)pkg;
                if (pkg < (uint)packages.Count) packages[(int)pkg].GlobalIds.Add(id);
                else validation.Add($"Effect id {id} maps to package {pkg}, past the {packages.Count} packages.");
            }
        }
        else validation.Add("No 0x500 effect id table.");

        var library = new EffectLibrary
        {
            File = file,
            Layout = layout,
            Packages = packages,
            Quads = quads,
            Lights = lights,
            Emitters = emitters,
            GlobalIds = globals,
            GlobalIdSpace = space,
            Validation = validation,
        };

        foreach (EffectPackage package in packages)
        {
            foreach (EffectComponent component in package.Components)
            {
                component.Piece = component.PieceIndex >= 0 ? library.Resolve(component.Type, component.PieceIndex) : null;
                if (component.Piece == null)
                    validation.Add($"{package.DisplayName} component {component.Slot} names {OldEngineEffectTypes.Name(component.Raw[0x3C])} " +
                                   $"{(component.Raw[0x20] << 8) | component.Raw[0x21]}, which does not exist.");
            }
        }

        int[] inbound = file.InboundPointerCounts();
        int notTree = 0;
        for (int i = 0; i < inbound.Length; i++)
            if (file.Chunks[i].Id != 0x100 && inbound[i] != 1) notTree++;
        if (notTree > 0)
            validation.Add($"{notTree} chunk(s) are not the target of exactly one pointer (retail: 0).");

        return library;
    }

    private static EffectPieceType OldPieceType(int listType) => listType switch
    {
        OldEngineEffectTypes.Package => EffectPieceType.Package,
        OldEngineEffectTypes.Quads => EffectPieceType.Quad,
        OldEngineEffectTypes.Lites => EffectPieceType.Light,
        _ => EffectPieceType.Emitter,
    };

    private static EffectPackage ReadOldPackage(EffectChunkFile file, int index, int at, Dictionary<(int Type, int Index), int> emitterIndex)
    {
        const int size = 0x80;
        var components = new List<EffectComponent>();
        int array = file.Pointer(at);
        if (array != 0)
        {
            int count = (int)file.U32(array - 4);
            for (int k = 0; k < count; k++)
            {
                int c = array + k * size;
                int listType = file.U8(c + 0x3C);
                // Decal, Function and MoveFacing pieces are skipped by the binder (0x005FE8F4): nothing to place.
                if (!OldEngineEffectTypes.IsEmitter(listType) && listType > OldEngineEffectTypes.Lites) continue;
                int piece = file.U16(c + 0x20);
                // An emitter is named inside its class's list; the model keeps one Emitters list (-1 = missing).
                if (OldEngineEffectTypes.IsEmitter(listType))
                    piece = emitterIndex.TryGetValue((listType, piece), out int flat) ? flat : -1;
                components.Add(new EffectComponent
                {
                    Slot = k,
                    FileOffset = c,
                    Type = OldPieceType(listType),
                    PieceIndex = piece,
                    StartDelay = Finite(file.F32(c + 0x00)),
                    Offset = new Vector3(Finite(file.F32(c + 0x2C)), Finite(file.F32(c + 0x34)), Finite(file.F32(c + 0x24))),
                    OffsetSpread = new Vector3(Finite(file.F32(c + 0x30)), Finite(file.F32(c + 0x38)), Finite(file.F32(c + 0x28))),
                    Attached = file.U8(c + 0x1C) == 1,
                    OffsetMode = file.U8(c + 0x1E),
                    OrientationMode = file.U8(c + 0x3D),
                    TiltMinDegrees = file.U8(c + 0x3E),
                    TiltMaxDegrees = file.U8(c + 0x3F),
                    AzimuthMinDegrees = file.S16(c + 0x40),
                    AzimuthMaxDegrees = file.S16(c + 0x42),
                    CycleMax = Finite(file.F32(c + 0x6C)),
                    CycleMin = Finite(file.F32(c + 0x70)),
                    OldEngine = true,
                    SpawnChance = Math.Clamp(Finite(file.F32(c + 0x08)), 0f, 1f),
                    VisibleMin = Finite(file.F32(c + 0x60)),
                    VisibleMax = Finite(file.F32(c + 0x5C)),
                    Raw = file.Data.AsSpan(c, size).ToArray(),
                });
            }
        }

        return new EffectPackage
        {
            Type = EffectPieceType.Package,
            Index = index,
            FileOffset = at,
            Raw = RawOf(file, at, EffectLayout.ToolsOfDestruction.PackageSize),
            Components = components,
            ScalarTracks = [],
            ColorTracks = [],
        };
    }

    private static List<EffectTexture> ReadOldTextures(EffectChunkFile file, int at, int count)
    {
        var textures = new List<EffectTexture>(count);
        for (int i = 0; i < count; i++)
        {
            int t = at + 0x10 * i;
            textures.Add(new EffectTexture
            {
                FieldOffset = t,
                OldEngine = true,
                Handles = [file.U16(t), file.U16(t + 2), file.U16(t + 4), file.U16(t + 6)],
                Word10 = file.F32(t + 0x08),
                Flags14 = file.U32(t + 0x0C),
            });
        }
        return textures;
    }

    private static EffectQuad ReadOldQuad(EffectChunkFile file, int index, int at)
    {
        var (scalars, colors) = ReadTracks(file, at, OldQuadTracks);
        int count = file.U8(at + 0xC4);
        return new EffectQuad
        {
            Type = EffectPieceType.Quad,
            Index = index,
            FileOffset = at,
            Raw = RawOf(file, at, EffectLayout.ToolsOfDestruction.QuadSize),
            ScalarTracks = scalars,
            ColorTracks = colors,
            Textures = ReadOldTextures(file, at + 0x40, Math.Clamp(count, 1, 8)),
            TextureCount = count,
            // +0xCC: the quad's life; every key track takes it as its time base (0x005FF250 reads it per track). PROVEN.
            Lifetime = Finite(file.F32(at + 0xCC)),
        };
    }

    private static EffectLight ReadOldLight(EffectChunkFile file, int index, int at)
    {
        var (scalars, colors) = ReadTracks(file, at, OldLightTracks);
        return new EffectLight
        {
            Type = EffectPieceType.Light,
            Index = index,
            FileOffset = at,
            Raw = RawOf(file, at, EffectLayout.ToolsOfDestruction.LightSize),
            ScalarTracks = scalars,
            ColorTracks = colors,
        };
    }

    private static EffectEmitter ReadOldEmitter(EffectChunkFile file, int type, int index, int at)
    {
        OldEmitterLayout l = OldLayoutOf(type);
        var specs = new List<TrackSpec>(OldEmitterTracks);
        if (OldEngineEffectTypes.HasSpin(type)) specs.Add(OldEmitterSpin);
        var (scalars, colors) = ReadTracks(file, at, specs);
        int count = file.U8(at + l.TextureCount);
        int emitterClass = OldEngineEffectTypes.EmitterClass(type);
        float period = Finite(file.F32(at + l.Life));
        float periodDefault = Finite(file.F32(at + l.Life + 4));
        int m = at + l.Motion;
        int s = at + l.SpawnStyle;

        return new EffectEmitter
        {
            Type = EffectPieceType.Emitter,
            Index = index,
            FileOffset = at,
            Raw = RawOf(file, at, OldEngineEffectTypes.RecordSize(type)),
            ScalarTracks = scalars,
            ColorTracks = colors,
            Textures = ReadOldTextures(file, at + l.Textures, Math.Clamp(count, 1, 8)),
            TextureCount = count,
            OldEngineType = type,
            OldPeriod = period,
            OldPeriodDefault = periodDefault,
            // The class is the list; no shader selector byte exists. ShaderId keeps the new engine's number for the class
            // (shader -> class table 0x00F2EF6C read backwards: 0 Cam, 3 Beam, 4 Streak, 5 Smoke, 6 Flame, 8 Distort,
            // 9 Trail, 12 Swipe) so code keyed on it treats both engines alike.
            ShaderId = emitterClass switch { 2 => 3, 3 => 4, 4 => 5, 5 => 6, 6 => 8, 7 => 9, 8 => 12, _ => 0 },
            EmitterClass = emitterClass,
            IsDistortion = type == OldEngineEffectTypes.Distort,
            // The emitter period: the load-time preparation copies +0x04 into +0x00 when +0x00 is 0 (0x00600254).
            Duration = period > 1e-4f ? period : periodDefault,
            Direction = Vector3.UnitZ,
            SpreadDegrees = 180f,
            SpawnStyle = file.U8(s),
            ConeMinDegrees = file.U8(s + 1),
            ConeMaxDegrees = file.U8(s + 2),
            SpawnInheritVelocity = Finite(file.F32(s + 4)),
            // Beam: one float after the lighting block (10, 1, 200, 3 ... on 1 574 beams) - its length, read like QForce's
            // FxEmtBeamControl maximum. INFERRED.
            BeamStretch = type == OldEngineEffectTypes.Beam ? Finite(file.F32(at + l.ClassBlock)) : 0f,
            // Streak: FxEmtStreakControl as on QForce, points per streak at its second byte. STRONG (same packing).
            StreakPoints = type == OldEngineEffectTypes.Streak ? file.U8(at + l.ClassBlock + 1) : 0,
            MotionMode = file.U8(m),
            Acceleration = Finite(file.F32(m + 0x04)),
            Thrust = Finite(file.F32(m + 0x08)),
            Turbulence = Finite(file.F32(m + 0x0C)),
            Drag = Finite(file.F32(m + 0x10)),
            SpeedMin = Finite(file.F32(m + 0x14)),
            SpeedMax = Finite(file.F32(m + 0x18)),
            Wind = Finite(file.F32(m + 0x1C)),
        };
    }

    /// <summary>Old engine: where the global effect texture pack starts in the level's combined texture set
    /// (<see cref="VfxTextureSet.ReadOldEngine"/>), so handles without the 0x8000 bit resolve into it.</summary>
    public void BindGlobalTextures(int globalBase)
    {
        if (!Layout.IsOldEngine) return;
        foreach (EffectEmitter e in Emitters)
            foreach (EffectTexture t in e.Textures) t.GlobalBase = globalBase;
        foreach (EffectQuad q in Quads)
            foreach (EffectTexture t in q.Textures) t.GlobalBase = globalBase;
    }

    /// <summary>
    /// Smoke and Flame sprites multiply several textures (fragment programs 0x2ED and 0x2F0 / 0x2F1, see
    /// <see cref="VfxTextureSet.Composite"/>): every Smoke FxTexture names three (two noises and a soft mask, 4 469 / 4 469
    /// on the 23 levels), every Flame one two. Points each of those textures at the composite of its layers.
    /// </summary>
    public void ComposeOldEngineLayers(VfxTextureSet textures)
    {
        if (!Layout.IsOldEngine) return;
        foreach (EffectEmitter e in Emitters)
        {
            int layers = e.OldEngineType switch { OldEngineEffectTypes.Smoke => 3, OldEngineEffectTypes.Flame => 2, _ => 1 };
            if (layers < 2) continue;
            foreach (EffectTexture t in e.Textures.Take(e.TextureCount))
            {
                int[] indices = Enumerable.Range(0, layers).Select(t.TextureIndex).ToArray();
                if (indices.All(i => i >= 0 && i < textures.Entries.Count))
                    t.CompositeIndex = textures.Composite(indices, premultiply: t.SpriteBlend == EffectSpriteBlend.Screen);
            }
        }
    }
}
