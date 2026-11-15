// Ported from ReLunacy Resurgence by Fianshoo (GPL-3.0). "Docs/reverse/..." paths cite that repository.
using System.Numerics;

namespace ReLunacy.Engine.Assets.Effects;

/// <summary>
/// Field offsets of one game family's effect records. Two families exist (PROVEN by record size and by
/// the position of every relocated pointer on 55 levels):
/// <list type="bullet">
///   <item><see cref="QForce"/> - QForce, All 4 One and Into the Nexus: package 424, quad 384, light 384,
///   emitter 932, FX entry 216. The sizes are the EBOOT's own class sizes (FxDefPkg 173 = 424,
///   FxDefQuad 176 = 384, FxDefLite 179 = 384, FxDefEmitter 206 = 932, FxDefFX 169 = 216,
///   <c>ReLunacy.Engine/Resources/FullFrontalAssault/ClassLayout.tsv</c>).</item>
///   <item><see cref="CrackInTime"/> - A Crack in Time: 336 / 372 / 376 / 896 / 212. Keys sit at the same
///   offsets; the controls block is shifted (-0x10 from the spawn style on), the FxDefFX placement sits 4 bytes
///   earlier from +0x20, and the shader -> class table differs (docs/reverse/acit_vfx.md).</item>
/// </list>
/// </summary>
public sealed class EffectLayout
{
    public required string Name { get; init; }
    public required int PackageSize { get; init; }
    public required int QuadSize { get; init; }
    public required int LightSize { get; init; }
    public required int EmitterSize { get; init; }
    public required int ComponentSize { get; init; }

    public required int PackageKeysOffset { get; init; }

    public int QuadTexturesOffset { get; init; } = 0x50;
    public required int QuadTextureCountOffset { get; init; }
    public int QuadLifetimeOffset { get; init; } = -1;

    public int EmitterTexturesOffset { get; init; } = 0xD4;
    public required int EmitterTextureCountOffset { get; init; }
    public required int EmitterShaderByteOffset { get; init; }
    public required int EmitterDistortKeysOffset { get; init; }
    public int EmitterDurationOffset { get; init; } = -1;
    public int EmitterDirectionOffset { get; init; } = -1;
    public int EmitterSpreadOffset { get; init; } = -1;
    /// <summary>FxEmtSpawnStyleControl: style byte, min/max cone angle bytes, +4 inherit-velocity float (QForce +0x328).</summary>
    public int EmitterSpawnStyleOffset { get; init; } = -1;
    /// <summary>FxEmtTurbAccelDirControl, 32 bytes (QForce +0x338): mode byte, then acceleration, thrust, turbulence,
    /// drag, min/max speed and wind floats at +4..+0x1C.</summary>
    public int EmitterMotionOffset { get; init; } = -1;
    /// <summary>FxEmtBeamControl, 3 floats (QForce +0x1EC).</summary>
    public int EmitterBeamOffset { get; init; } = -1;
    /// <summary>FxEmtStreakControl, 32 bytes (QForce +0x1F8).</summary>
    public int EmitterStreakOffset { get; init; } = -1;

    /// <summary>Byte the emitter's frame-oriented particles take their orientation mode from (QForce +0x31F).</summary>
    public int EmitterFacingOffset { get; init; } = -1;
    /// <summary>Shader selector -> emitter class, the EBOOT's table; a shader past its end is class 0.</summary>
    public byte[] EmitterClassByShader { get; init; } = QForceClassByShader;
    /// <summary>Class 0 means the emitter is off: the binder never runs it (A Crack in Time's shader 12).</summary>
    public bool ClassZeroDisables { get; init; }

    /// <summary>FxDefLite: lifetime (0 = permanent) and the intensity every colour key's alpha is multiplied by.</summary>
    public int LightLifetimeOffset { get; init; } = -1;
    public int LightIntensityOffset { get; init; } = -1;

    public bool ComponentPlacementKnown { get; init; }
    /// <summary>FxDefFX placement fields (QForce offsets by default).</summary>
    public int ComponentAttachedOffset { get; init; } = 0x24;
    public int ComponentOrientationOffset { get; init; } = 0x26;
    public int ComponentTiltOffset { get; init; } = 0x27;
    public int ComponentAzimuthOffset { get; init; } = 0x2A;
    public int ComponentOffsetModeOffset { get; init; } = 0x2E;
    /// <summary>Offset Z; spread Z, offset X, spread X, offset Y, spread Y follow every 4 bytes.</summary>
    public int ComponentOffsetOffset { get; init; } = 0x30;
    public int ComponentCycleMaxOffset { get; init; } = 0xC0;
    public int ComponentCycleMinOffset { get; init; } = 0xC4;

    // EBOOT 0x00F2EF6C (QForce): shader 0,1,2,7,10,13 -> 1; 3 -> 2; 4 -> 3; 5 -> 4; 6 -> 5; 8,11 -> 6; 9 -> 7; 12 -> 8.
    private static readonly byte[] QForceClassByShader = [1, 1, 1, 2, 3, 4, 5, 1, 6, 7, 1, 6, 8, 1];
    // EBOOT 0x00DB49B0 (A Crack in Time): the same, but seven classes and shader 12 -> 0 (off). PROVEN.
    private static readonly byte[] CrackInTimeClassByShader = [1, 1, 1, 2, 3, 4, 5, 1, 6, 7, 1, 6, 0, 1];

    /// <summary>Tools of Destruction / Quest for Booty: eleven piece lists, one per emitter class, read by
    /// <see cref="EffectLibrary"/>'s old-engine path (EffectLibrary.OldEngine.cs). Read-only.</summary>
    public bool IsOldEngine { get; init; }

    public static readonly EffectLayout QForce = new()
    {
        Name = "QForce / All 4 One / Into the Nexus",
        PackageSize = 424, QuadSize = 384, LightSize = 384, EmitterSize = 932, ComponentSize = 216,
        PackageKeysOffset = 0x14C,
        QuadTextureCountOffset = 0x130,
        QuadLifetimeOffset = 0x150,
        EmitterTextureCountOffset = 0x1B4,
        EmitterShaderByteOffset = 0x1D2,
        EmitterDistortKeysOffset = 0x1D8,
        EmitterDurationOffset = 0x324,
        EmitterDirectionOffset = 0x25C,
        EmitterSpreadOffset = 0x290,
        EmitterSpawnStyleOffset = 0x328,
        EmitterMotionOffset = 0x338,
        EmitterBeamOffset = 0x1EC,
        EmitterStreakOffset = 0x1F8,
        EmitterFacingOffset = 0x31F,
        ComponentPlacementKnown = true,
    };

    public static readonly EffectLayout CrackInTime = new()
    {
        Name = "A Crack in Time",
        PackageSize = 336, QuadSize = 372, LightSize = 376, EmitterSize = 896, ComponentSize = 212,
        PackageKeysOffset = 0xF4,
        QuadTextureCountOffset = 0x134,
        EmitterTextureCountOffset = 0x1B8,
        QuadLifetimeOffset = 0x144,
        EmitterShaderByteOffset = 0x1C6,
        EmitterDistortKeysOffset = 0x1CC,
        EmitterDurationOffset = 0x314,
        EmitterDirectionOffset = 0x24C,
        EmitterSpreadOffset = 0x280,
        EmitterSpawnStyleOffset = 0x318,
        EmitterMotionOffset = 0x328,
        EmitterBeamOffset = 0x1E0,
        EmitterStreakOffset = 0x1EC,
        // QForce's +0x31F less 0x10: non-zero on 109 emitters, 62 of them shader 10 (INFERRED).
        EmitterFacingOffset = 0x30F,
        EmitterClassByShader = CrackInTimeClassByShader,
        ClassZeroDisables = true,
        LightLifetimeOffset = 0x148,
        LightIntensityOffset = 0x14C,
        // QForce minus the always-zero word at +0x20; the restart cycle sits at +0xC4 / +0xC8.
        ComponentPlacementKnown = true,
        ComponentAttachedOffset = 0x20,
        ComponentOrientationOffset = 0x22,
        ComponentTiltOffset = 0x23,
        ComponentAzimuthOffset = 0x26,
        ComponentOffsetModeOffset = 0x2A,
        ComponentOffsetOffset = 0x2C,
        ComponentCycleMaxOffset = 0xC4,
        ComponentCycleMinOffset = 0xC8,
    };

    /// <summary>
    /// Tools of Destruction (and the Quest for Booty levels of the same install): package 0xA8, quad 0xE0, light 0xDC,
    /// emitters 0x16C..0x190 by class, FX entry 0x80 - the sizes the EBOOT's package binder copies (0x005FE8F4),
    /// PROVEN. The emitter fields are per type (<see cref="OldEngineEffectTypes"/>), see EffectLibrary.OldEngine.cs.
    /// </summary>
    public static readonly EffectLayout ToolsOfDestruction = new()
    {
        Name = "Tools of Destruction",
        PackageSize = 0xA8, QuadSize = 0xE0, LightSize = 0xDC, EmitterSize = 0x174, ComponentSize = 0x80,
        PackageKeysOffset = -1,
        QuadTexturesOffset = 0x40,
        QuadTextureCountOffset = 0xC4,
        QuadLifetimeOffset = 0xCC,
        EmitterTexturesOffset = 0x8C,
        EmitterTextureCountOffset = 0x110,
        EmitterShaderByteOffset = -1,
        EmitterDistortKeysOffset = -1,
        IsOldEngine = true,
    };

    public static readonly IReadOnlyList<EffectLayout> All = [QForce, CrackInTime];
}

/// <summary>
/// Everything one level's effect.dat holds, typed. Read-only: nothing here writes a file.
///
/// Root structure (PROVEN on 55 levels of four games):
/// <code>
///   0x100  header  { u32 4, ptr list0, ptr list1, ptr list2, ptr list13, ptr 0x500, ptr 0x800 }
///   0x300  list    { u32 piece type, u32 count, ptr -> 0x700 }      one per type: 0, 1, 2, 13
///   0x700  array   count x ptr -> the type's records (0x400 chunks)
///   0x400  record  FxDefPkg / FxDefQuad / FxDefLite / FxDefEmitter, typed by the list that owns it
///   0x600  u32 n + n x FxDefFX, pointed at by a package's +0x00
///   0x500  { u32 n, u32 1, n x u32 package index or 0xFFFFFFFF }   game-wide effect id -> package
///   0x800  u32 0                                                    OPEN
///   anon   u32 n + n keys, pointed at by a key header
/// </code>
/// </summary>
public sealed partial class EffectLibrary
{
    public required EffectChunkFile File { get; init; }
    public required EffectLayout Layout { get; init; }
    public required IReadOnlyList<EffectPackage> Packages { get; init; }
    public required IReadOnlyList<EffectQuad> Quads { get; init; }
    public required IReadOnlyList<EffectLight> Lights { get; init; }
    public required IReadOnlyList<EffectEmitter> Emitters { get; init; }
    /// <summary>Game-wide effect id -> package index (0x500 table).</summary>
    public required IReadOnlyDictionary<int, int> GlobalIds { get; init; }
    /// <summary>Size of the game-wide id space (3 890 on QForce, 3 381 on All 4 One, 4 636 on Into the Nexus).</summary>
    public int GlobalIdSpace { get; init; }
    /// <summary>Anything that departs from the retail shape. Empty on every retail level.</summary>
    public required IReadOnlyList<string> Validation { get; init; }

    public EffectPiece? Resolve(EffectPieceType type, int index) => type switch
    {
        EffectPieceType.Package => (uint)index < (uint)Packages.Count ? Packages[index] : null,
        EffectPieceType.Quad => (uint)index < (uint)Quads.Count ? Quads[index] : null,
        EffectPieceType.Light => (uint)index < (uint)Lights.Count ? Lights[index] : null,
        EffectPieceType.Emitter => (uint)index < (uint)Emitters.Count ? Emitters[index] : null,
        _ => null,
    };

    public EffectPackage? PackageForGlobalId(int id) =>
        GlobalIds.TryGetValue(id, out int pkg) && (uint)pkg < (uint)Packages.Count ? Packages[pkg] : null;

    // ---------------------------------------------------------------------------------------------
    // Reading

    /// <summary>Color32 is Tools of Destruction's colour key: RGB min, RGB max, time at +0x18, mode at +0x1C (0x005FE2D0).</summary>
    private enum KeyKind { Scalar16, Rot16, Scalar20, Color40, Color32 }

    private readonly record struct TrackSpec(string Name, int Offset, KeyKind Kind, EffectTrackDomain Domain);

    /// <summary>FxEmtKeys, in field order. Header positions PROVEN (relocation table + the EBOOT's count
    /// bytes at +0x14/+0x24/+0x30/+0x3C/+0x48/+0x54/+0x60/+0x80/+0x8C/+0x98/+0xA4/+0xC4/+0xD1, read by
    /// 0x007DE3E0 and 0x007DB228). Key sizes PROVEN (FxKeySpawnRate 16, FxKeySpawnLife 20,
    /// FxKeySpawnSpeed 20, FxKeySpawnRadius 16, FxKeyColor 40, FxKeyAlpha/ATest/Scale/Rot 16). The names of
    /// the two halves (emitter time, particle life) are INFERRED from the data: the first half is (1,1,1,1)
    /// on almost every emitter, the second carries the fades and the growth.</summary>
    private static readonly TrackSpec[] EmitterTracks =
    [
        new("Spawn rate", 0x00, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Spawn life", 0x18, KeyKind.Scalar20, EffectTrackDomain.PieceLife),
        new("Spawn speed", 0x28, KeyKind.Scalar20, EffectTrackDomain.PieceLife),
        new("Spawn radius", 0x34, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Emitter colour", 0x40, KeyKind.Color40, EffectTrackDomain.PieceLife),
        new("Emitter alpha", 0x4C, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Emitter alpha test", 0x58, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Emitter scale", 0x78, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Particle colour", 0x84, KeyKind.Color40, EffectTrackDomain.ParticleLife),
        new("Particle alpha", 0x90, KeyKind.Scalar16, EffectTrackDomain.ParticleLife),
        new("Particle alpha test", 0x9C, KeyKind.Scalar16, EffectTrackDomain.ParticleLife),
        new("Particle size", 0xBC, KeyKind.Scalar16, EffectTrackDomain.ParticleLife),
        new("Particle spin", 0xC8, KeyKind.Rot16, EffectTrackDomain.ParticleLife),
    ];

    /// <summary>FxQuadKeys (80 bytes). +0x38 is halved by the binder (0x007DC8EC) - a size.</summary>
    private static readonly TrackSpec[] QuadTracks =
    [
        new("Colour", 0x00, KeyKind.Color40, EffectTrackDomain.PieceLife),
        new("Alpha", 0x0C, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Alpha test", 0x18, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Size", 0x38, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Track 0x44", 0x44, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
    ];

    /// <summary>FxLiteKeys (84 bytes).</summary>
    private static readonly TrackSpec[] LightTracks =
    [
        new("Colour", 0x00, KeyKind.Color40, EffectTrackDomain.PieceLife),
        new("Track 0x0C", 0x0C, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Track 0x18", 0x18, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Inner radius?", 0x24, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Outer radius?", 0x30, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Fade start?", 0x3C, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Fade end?", 0x48, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
    ];

    /// <summary>FxPkgKeys (80 bytes, same header shape as the quad's).</summary>
    private static readonly TrackSpec[] PackageTrackOffsets =
    [
        new("Tint", 0x00, KeyKind.Color40, EffectTrackDomain.PieceLife),
        new("Track +0x0C", 0x0C, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Track +0x18", 0x18, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Scale", 0x38, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Spin", 0x44, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
    ];

    public static EffectLibrary Read(byte[] data) => Read(EffectChunkFile.Parse(data));

    public static EffectLibrary Read(EffectChunkFile file)
    {
        var validation = new List<string>();

        // Lists: type -> record offsets.
        var lists = new Dictionary<int, int[]>();
        foreach (EffectChunkFile.Chunk chunk in file.ChunksWithId(0x300))
        {
            if (chunk.Length < 12) { validation.Add($"0x300 at 0x{chunk.Offset:X} is {chunk.Length} bytes."); continue; }
            int type = (int)file.U32(chunk.Offset);
            int count = (int)file.U32(chunk.Offset + 4);
            int array = file.Pointer(chunk.Offset + 8);
            var records = new int[count];
            for (int i = 0; i < count; i++)
                records[i] = array == 0 ? 0 : file.Pointer(array + 4 * i);
            if (!lists.TryAdd(type, records))
                validation.Add($"Piece type {type} has two 0x300 lists.");
        }

        EffectLayout layout = PickLayout(file, lists, validation);
        if (layout.IsOldEngine) return ReadOldEngine(file, lists, validation);

        var packages = new List<EffectPackage>();
        var quads = new List<EffectQuad>();
        var lights = new List<EffectLight>();
        var emitters = new List<EffectEmitter>();

        foreach (var (type, records) in lists.OrderBy(kv => kv.Key))
        {
            for (int i = 0; i < records.Length; i++)
            {
                int at = records[i];
                if (at == 0) { validation.Add($"List {type} entry {i} is null."); continue; }
                switch ((EffectPieceType)type)
                {
                    case EffectPieceType.Package: packages.Add(ReadPackage(file, layout, i, at)); break;
                    case EffectPieceType.Quad: quads.Add(ReadQuad(file, layout, i, at)); break;
                    case EffectPieceType.Light: lights.Add(ReadLight(file, layout, i, at)); break;
                    case EffectPieceType.Emitter: emitters.Add(ReadEmitter(file, layout, i, at)); break;
                    default: validation.Add($"Unknown piece type {type} ({records.Length} records) - kept out of the model."); i = records.Length; break;
                }
            }
        }

        // Global effect ids.
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

        // Resolve components.
        foreach (EffectPackage package in packages)
        {
            foreach (EffectComponent component in package.Components)
            {
                component.Piece = library.Resolve(component.Type, component.PieceIndex);
                if (component.Piece == null)
                    validation.Add($"{package.DisplayName} component {component.Slot} names {component.Type} {component.PieceIndex}, which does not exist.");
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

    private static EffectLayout PickLayout(EffectChunkFile file, Dictionary<int, int[]> lists, List<string> validation)
    {
        int SizeOf(int type)
        {
            if (!lists.TryGetValue(type, out int[]? records) || records.Length == 0 || records[0] == 0) return -1;
            int chunk = file.ChunkIndexAt(records[0]);
            return chunk < 0 ? -1 : file.Chunks[chunk].Length;
        }

        int pkg = SizeOf(0), emt = SizeOf(13);
        // Tools of Destruction: per-class emitter lists (5..12) or its 0xA8 package. No new-engine level has either.
        if (pkg == EffectLayout.ToolsOfDestruction.PackageSize || lists.Keys.Any(t => t is >= 3 and <= 12))
            return EffectLayout.ToolsOfDestruction;
        foreach (EffectLayout layout in EffectLayout.All)
            if ((pkg < 0 || pkg == layout.PackageSize) && (emt < 0 || emt == layout.EmitterSize))
                return layout;

        validation.Add($"Unknown record sizes (package {pkg}, emitter {emt}); read with the QForce layout, fields past the keys are unreliable.");
        return EffectLayout.QForce;
    }

    private static byte[] RawOf(EffectChunkFile file, int at, int size)
    {
        int chunk = file.ChunkIndexAt(at);
        int length = chunk < 0 ? size : Math.Min(size, file.Chunks[chunk].End - at);
        return file.Data.AsSpan(at, Math.Max(0, length)).ToArray();
    }

    private static EffectPackage ReadPackage(EffectChunkFile file, EffectLayout layout, int index, int at)
    {
        var components = new List<EffectComponent>();
        int array = file.Pointer(at);
        if (array != 0)
        {
            int count = (int)file.U32(array - 4);
            for (int k = 0; k < count; k++)
            {
                int c = array + k * layout.ComponentSize;
                int o = c + layout.ComponentOffsetOffset;
                bool known = layout.ComponentPlacementKnown;
                uint word = file.U32(c);
                EffectScalarTrack? spin = null;
                // The component's only pointer is its optional spin track (+0x78 on the QForce layout);
                // find it by the relocation table rather than a fixed offset so A Crack in Time's 212-byte
                // entry reads too.
                for (int f = 4; f < layout.ComponentSize; f += 4)
                {
                    if (file.IsPointer(c + f) && file.U32(c + f) != 0)
                    {
                        spin = ReadScalarTrack(file, c, new TrackSpec("Spin", f, KeyKind.Scalar16, EffectTrackDomain.PieceLife));
                        break;
                    }
                }
                components.Add(new EffectComponent
                {
                    Slot = k,
                    FileOffset = c,
                    Type = (EffectPieceType)((word >> 8) & 0xFF),
                    PieceIndex = (int)(word >> 16),
                    StartDelay = known ? file.F32(c + 4) : 0f,
                    // FXVis 0x2058 reads the packed offset back as (+0x38, +0x40, +0x30), spread likewise (QForce).
                    Offset = known ? new Vector3(file.F32(o + 8), file.F32(o + 0x10), file.F32(o)) : Vector3.Zero,
                    OffsetSpread = known ? new Vector3(file.F32(o + 0x0C), file.F32(o + 0x14), file.F32(o + 4)) : Vector3.Zero,
                    Attached = known && file.U8(c + layout.ComponentAttachedOffset) == 1,
                    OrientationMode = known ? file.U8(c + layout.ComponentOrientationOffset) : 0,
                    OffsetMode = known ? file.U8(c + layout.ComponentOffsetModeOffset) : 0,
                    TiltMinDegrees = known ? file.U8(c + layout.ComponentTiltOffset) : 0,
                    TiltMaxDegrees = known ? file.U8(c + layout.ComponentTiltOffset + 1) : 0,
                    AzimuthMinDegrees = known ? file.S16(c + layout.ComponentAzimuthOffset) : 0,
                    AzimuthMaxDegrees = known ? file.S16(c + layout.ComponentAzimuthOffset + 2) : 0,
                    // The piece's restart cycle, max then min seconds (0 = none).
                    CycleMax = known ? file.F32(c + layout.ComponentCycleMaxOffset) : 0f,
                    CycleMin = known ? file.F32(c + layout.ComponentCycleMinOffset) : 0f,
                    Spin = spin,
                    Raw = file.Data.AsSpan(c, layout.ComponentSize).ToArray(),
                });
            }
        }

        var (scalars, colors) = ReadTracks(file, at + layout.PackageKeysOffset, PackageTrackOffsets);
        return new EffectPackage
        {
            Type = EffectPieceType.Package,
            Index = index,
            FileOffset = at,
            Raw = RawOf(file, at, layout.PackageSize),
            Components = components,
            ScalarTracks = scalars,
            ColorTracks = colors,
        };
    }

    private static EffectQuad ReadQuad(EffectChunkFile file, EffectLayout layout, int index, int at)
    {
        var (scalars, colors) = ReadTracks(file, at, QuadTracks);
        int count = file.U8(at + layout.QuadTextureCountOffset);
        return new EffectQuad
        {
            Type = EffectPieceType.Quad,
            Index = index,
            FileOffset = at,
            Raw = RawOf(file, at, layout.QuadSize),
            ScalarTracks = scalars,
            ColorTracks = colors,
            Textures = ReadTextures(file, at + layout.QuadTexturesOffset, Math.Clamp(count, 1, 8)),
            TextureCount = count,
            Lifetime = layout.QuadLifetimeOffset >= 0 ? file.F32(at + layout.QuadLifetimeOffset) : 0f,
            Facing = layout.QuadLifetimeOffset >= 0 ? file.U8(at + layout.QuadLifetimeOffset + 0x0E) : 0,
        };
    }

    private static EffectLight ReadLight(EffectChunkFile file, EffectLayout layout, int index, int at)
    {
        var (scalars, colors) = ReadTracks(file, at, LightTracks);
        float intensity = layout.LightIntensityOffset >= 0 ? Finite(file.F32(at + layout.LightIntensityOffset)) : 1f;
        // The binder multiplies every colour key's alpha by the intensity (A Crack in Time 0x69CA7C).
        if (layout.LightIntensityOffset >= 0)
            for (int i = 0; i < colors.Count; i++)
                colors[i] = colors[i].ScaledAlpha(intensity);
        return new EffectLight
        {
            Type = EffectPieceType.Light,
            Index = index,
            FileOffset = at,
            Raw = RawOf(file, at, layout.LightSize),
            ScalarTracks = scalars,
            ColorTracks = colors,
            Lifetime = layout.LightLifetimeOffset >= 0 ? MathF.Max(0f, Finite(file.F32(at + layout.LightLifetimeOffset))) : 0f,
            Intensity = intensity,
        };
    }

    // EBOOT 0x00F2EFA4: distortion flag by shader selector.
    private static readonly bool[] DistortionByShader = [false, false, false, false, false, false, false, true, false, false, false, false, false, true];

    private static EffectEmitter ReadEmitter(EffectChunkFile file, EffectLayout layout, int index, int at)
    {
        var specs = new List<TrackSpec>(EmitterTracks)
        {
            new("Distortion", layout.EmitterDistortKeysOffset, KeyKind.Scalar16, EffectTrackDomain.ParticleLife),
        };
        var (scalars, colors) = ReadTracks(file, at, specs);
        int count = file.U8(at + layout.EmitterTextureCountOffset);
        int shader = file.U8(at + layout.EmitterShaderByteOffset);
        int emitterClass = shader < layout.EmitterClassByShader.Length ? layout.EmitterClassByShader[shader] : 0;

        Vector3 direction = Vector3.UnitZ;
        if (layout.EmitterDirectionOffset >= 0)
        {
            int d = at + layout.EmitterDirectionOffset;
            var v = new Vector3(file.F32(d), file.F32(d + 4), file.F32(d + 8));
            if (v.LengthSquared() > 1e-6f && float.IsFinite(v.X + v.Y + v.Z)) direction = Vector3.Normalize(v);
        }

        return new EffectEmitter
        {
            Type = EffectPieceType.Emitter,
            Index = index,
            FileOffset = at,
            Raw = RawOf(file, at, layout.EmitterSize),
            ScalarTracks = scalars,
            ColorTracks = colors,
            Textures = ReadTextures(file, at + layout.EmitterTexturesOffset, Math.Clamp(count, 1, 8)),
            TextureCount = count,
            ShaderId = shader,
            FacingMode = layout.EmitterFacingOffset >= 0 ? file.U8(at + layout.EmitterFacingOffset) : 0,
            EmitterClass = emitterClass,
            Disabled = layout.ClassZeroDisables && emitterClass == 0,
            IsDistortion = shader < DistortionByShader.Length && DistortionByShader[shader],
            Duration = layout.EmitterDurationOffset >= 0 ? file.F32(at + layout.EmitterDurationOffset) : 0f,
            Direction = direction,
            SpreadDegrees = layout.EmitterSpreadOffset >= 0 ? file.U8(at + layout.EmitterSpreadOffset) : 180f,
            // Unmapped layout (A Crack in Time): any direction for both, as before the style was decoded.
            SpawnStyle = layout.EmitterSpawnStyleOffset >= 0 ? file.U8(at + layout.EmitterSpawnStyleOffset) : 0x05,
            ConeMinDegrees = layout.EmitterSpawnStyleOffset >= 0 ? file.U8(at + layout.EmitterSpawnStyleOffset + 1) : 0,
            ConeMaxDegrees = layout.EmitterSpawnStyleOffset >= 0 ? file.U8(at + layout.EmitterSpawnStyleOffset + 2) : 180,
            SpawnInheritVelocity = layout.EmitterSpawnStyleOffset >= 0 ? Finite(file.F32(at + layout.EmitterSpawnStyleOffset + 4)) : 0f,
            BeamStretch = layout.EmitterBeamOffset >= 0 ? file.F32(at + layout.EmitterBeamOffset) : 0f,
            BeamMinStretch = layout.EmitterBeamOffset >= 0 ? Finite(file.F32(at + layout.EmitterBeamOffset + 4)) : 0f,
            BeamFollowRate = layout.EmitterBeamOffset >= 0 ? Finite(file.F32(at + layout.EmitterBeamOffset + 8)) : 0f,
            StreakPoints = layout.EmitterStreakOffset >= 0 ? file.U8(at + layout.EmitterStreakOffset + 1) : 0,
            // Unmapped layout: no motion block, so no acceleration and no drag (straight lines, as before).
            MotionMode = layout.EmitterMotionOffset >= 0 ? file.U8(at + layout.EmitterMotionOffset) : 0xFF,
            Acceleration = Motion(file, layout, at, 0x04),
            Thrust = Motion(file, layout, at, 0x08),
            Turbulence = Motion(file, layout, at, 0x0C),
            Drag = Motion(file, layout, at, 0x10),
            SpeedMin = Motion(file, layout, at, 0x14),
            SpeedMax = layout.EmitterMotionOffset >= 0 ? Finite(file.F32(at + layout.EmitterMotionOffset + 0x18)) : 1024f,
            Wind = Motion(file, layout, at, 0x1C),
        };
    }

    private static float Motion(EffectChunkFile file, EffectLayout layout, int at, int field) =>
        layout.EmitterMotionOffset >= 0 ? Finite(file.F32(at + layout.EmitterMotionOffset + field)) : 0f;

    private static float Finite(float v) => float.IsFinite(v) ? v : 0f;

    private static List<EffectTexture> ReadTextures(EffectChunkFile file, int at, int count)
    {
        var textures = new List<EffectTexture>(count);
        for (int i = 0; i < count; i++)
        {
            int t = at + 0x1C * i;
            textures.Add(new EffectTexture
            {
                FieldOffset = t,
                Handles = [file.U32(t), file.U32(t + 4), file.U32(t + 8), file.U32(t + 12)],
                Word10 = file.F32(t + 0x10),
                Flags14 = file.U32(t + 0x14),
                Word18 = file.U32(t + 0x18),
            });
        }
        return textures;
    }

    private static (List<EffectScalarTrack>, List<EffectColorTrack>) ReadTracks(
        EffectChunkFile file, int owner, IEnumerable<TrackSpec> specs)
    {
        var scalars = new List<EffectScalarTrack>();
        var colors = new List<EffectColorTrack>();
        foreach (TrackSpec spec in specs)
        {
            if (spec.Kind is KeyKind.Color40 or KeyKind.Color32) colors.Add(ReadColorTrack(file, owner, spec));
            else scalars.Add(ReadScalarTrack(file, owner, spec));
        }
        return (scalars, colors);
    }

    private const int MaxKeys = 32; // the runtime caps every track at 32 keys (0x007D8824)

    private static EffectScalarTrack ReadScalarTrack(EffectChunkFile file, int owner, TrackSpec spec)
    {
        int header = owner + spec.Offset;
        int keys = file.Pointer(header);
        int size = spec.Kind == KeyKind.Scalar20 ? 20 : 16;
        var list = new List<EffectKey>();
        if (keys != 0)
        {
            int n = Math.Min((int)file.U32(keys - 4), MaxKeys);
            for (int i = 0; i < n; i++)
            {
                int k = keys + i * size;
                list.Add(spec.Kind switch
                {
                    KeyKind.Scalar20 => new EffectKey(file.F32(k), file.F32(k + 4), file.F32(k + 8), file.F32(k + 12), file.U8(k + 16)),
                    // FxKeyRot keeps its mode one byte later (+0x0D; the binder reads 0xD(r7), 0x007DB2A4).
                    KeyKind.Rot16 => new EffectKey(file.F32(k), file.F32(k + 4), 0f, file.F32(k + 8), file.U8(k + 13)),
                    _ => new EffectKey(file.F32(k), file.F32(k + 4), 0f, file.F32(k + 8), file.U8(k + 12)),
                });
            }
        }
        return new EffectScalarTrack
        {
            Name = spec.Name,
            FieldOffset = spec.Offset,
            Keys = [.. list],
            Word1 = file.U32(header + 4),
            Domain = spec.Domain,
            KeysOffset = keys,
            KeySize = size,
        };
    }

    private static EffectColorTrack ReadColorTrack(EffectChunkFile file, int owner, TrackSpec spec)
    {
        int header = owner + spec.Offset;
        int keys = file.Pointer(header);
        var list = new List<EffectColorKey>();
        if (keys != 0)
        {
            int n = Math.Min((int)file.U32(keys - 4), MaxKeys);
            for (int i = 0; i < n; i++)
            {
                if (spec.Kind == KeyKind.Color32)
                {
                    // RGB only: the old engine's alpha lives in its own track.
                    int c = keys + i * 32;
                    list.Add(new EffectColorKey(
                        new Vector4(file.F32(c), file.F32(c + 4), file.F32(c + 8), 1f),
                        new Vector4(file.F32(c + 12), file.F32(c + 16), file.F32(c + 20), 1f),
                        file.F32(c + 24),
                        file.U8(c + 28)));
                    continue;
                }
                int k = keys + i * 40;
                list.Add(new EffectColorKey(
                    new Vector4(file.F32(k), file.F32(k + 4), file.F32(k + 8), file.F32(k + 12)),
                    new Vector4(file.F32(k + 16), file.F32(k + 20), file.F32(k + 24), file.F32(k + 28)),
                    file.F32(k + 32),
                    file.U8(k + 36)));
            }
        }
        return new EffectColorTrack
        {
            Name = spec.Name,
            FieldOffset = spec.Offset,
            Keys = [.. list],
            Word1 = file.U32(header + 4),
            Domain = spec.Domain,
            KeysOffset = keys,
        };
    }
}
