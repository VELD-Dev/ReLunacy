// Ported from ReLunacy Resurgence by Fianshoo (GPL-3.0). "Docs/reverse/..." paths cite that repository.
using System.Buffers.Binary;
using System.Numerics;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Assets.Effects;

/// <summary>
/// One FxController placed in an old-engine gameplay.dat (class 384, Docs/reverse/TOD_EFFECTS_2026-10-09.md section 8):
/// where it sits and what its class data says. The controller record (0x77C0, 0x30 bytes) is
/// <code>
///   +0x00 class data offset   +0x04 position   +0x10 rotation (radians, applied Z, Y, X)   +0x1C scale   +0x28 class data length
/// </code>
/// and the class data a small IGHW whose 0x25200 section starts with the class id:
/// <code>
///   +0x14 u32 fxconduit key (a Tags value)   +0x1F u8 start active   +0x24 / +0x28 f32 retrigger min / max (s)
/// </code>
/// </summary>
public sealed record OldEffectController(int Index, Vector3 Position, Quaternion Rotation, uint Key, bool StartsActive,
    float RepeatMin, float RepeatMax);

/// <summary>What one controller resolves to: the package it plays (null when it resolves to none) and why.</summary>
public sealed record EffectResolution(EffectPackage? Package, int EffectId, string Description);

/// <summary>
/// A level's effect data, read once from the level's files: on the old engine effect.dat, the level's and the game's
/// VFX textures, fxconduit and the FxController placements of gameplay.dat; on A Crack in Time effect.dat,
/// vfx_system_header / texel and fxconduit (its FxControllers are the scene's controller entities).
/// </summary>
public sealed class LevelEffects
{
    /// <summary>Tools of Destruction's FxController (Quest for Booty shares the class table).</summary>
    public const uint FxControllerClass = 384;
    /// <summary>EventType ACTIVE in ToD's /scripts/eventtypes.lua: the record an FxController plays while on.</summary>
    public const uint ActiveEvent = 1;
    /// <summary>A Crack in Time's FxController; its class data +0x14 is the Tags key (docs/reverse/acit_vfx.md section 5).</summary>
    public const uint CrackInTimeFxControllerClass = 0x3D0;
    /// <summary>EventType ACTIVE in A Crack in Time's /scripts/eventtypes.lua.</summary>
    public const uint CrackInTimeActiveEvent = 0;

    /// <summary>The FxController class and the ACTIVE event this level's conduit is looked up with.</summary>
    public uint ControllerClass { get; init; } = FxControllerClass;
    public uint ActiveEventType { get; init; } = ActiveEvent;

    private const uint ControllerSection = 0x77C0;
    private const uint ClassDataSection = 0x25200;

    public required EffectLibrary Library { get; init; }
    public required VfxTextureSet Textures { get; init; }
    public EffectConduit? Conduit { get; init; }
    public string? ConduitError { get; init; }
    public string? TextureError { get; init; }
    public IReadOnlyList<OldEffectController> Controllers { get; init; } = [];

    /// <summary>The level files <see cref="TryLoad"/> reads.</summary>
    public static readonly string[] FileNames = ["effect.dat", "system.tph", "system.tp", "fxconduit_packed.dat", "gameplay.dat"];

    /// <summary>The level files <see cref="TryLoadCrackInTime"/> reads.</summary>
    public static readonly string[] CrackInTimeFileNames = ["effect.dat", "vfx_system_header.dat", "vfx_system_texel.dat", "fxconduit_packed.dat"];

    /// <summary>One of <see cref="FileNames"/> from the level, null when it has none. A folder level holds its
    /// gameplay.dat open without sharing it, so that one is copied from the open stream.</summary>
    public static byte[]? ReadLevelFile(FileManager files, string name)
    {
        if (files.igfiles.GetValueOrDefault(name) is { } open)
        {
            Stream stream = open.sh.BaseStream;
            long position = stream.Position;
            try
            {
                var copy = new byte[stream.Length];
                stream.Position = 0;
                stream.ReadExactly(copy);
                return copy;
            }
            finally { stream.Position = position; }
        }
        try { return files.ReadOriginal(name); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>The level's folder, where the game's effect pack is looked for (<see cref="OldEngineEffectTexturePack"/>).</summary>
    public static string LevelFolder(FileManager files) =>
        files.folderPath.Length > 0 ? files.folderPath : Path.GetDirectoryName(files.SourcePath) ?? "";

    /// <summary>Reads the effect data of an old-engine level from its files (<paramref name="read"/>: one of
    /// <see cref="FileNames"/>, null when missing). Null, with the reason, when effect.dat is missing or unreadable.</summary>
    public static LevelEffects? TryLoad(Func<string, byte[]?> read, string levelFolder, out string? error)
    {
        byte[]? effect = read("effect.dat");
        if (effect == null) { error = "effect.dat could not be read."; return null; }
        byte[]? globalHeader = null, globalTexel = null;
        if (OldEngineEffectTexturePack.TryRead(levelFolder, out byte[] h, out byte[] t, out _))
            (globalHeader, globalTexel) = (h, t);
        var effects = FromBytes(effect, read("system.tph"), read("system.tp"), globalHeader, globalTexel,
            read("fxconduit_packed.dat"), out error);
        if (effects == null) return null;
        try
        {
            if (read("gameplay.dat") is { } gameplay)
                return new LevelEffects
                {
                    Library = effects.Library,
                    Textures = effects.Textures,
                    Conduit = effects.Conduit,
                    ConduitError = effects.ConduitError,
                    TextureError = effects.TextureError,
                    Controllers = ReadControllers(gameplay),
                };
        }
        catch (Exception e) { error = $"gameplay.dat FxControllers could not be read: {e.Message}"; }
        return effects;
    }

    /// <summary>A Crack in Time level's effects from its files (<paramref name="read"/>: one of <see cref="CrackInTimeFileNames"/>,
    /// null when missing). Null, with the reason, when effect.dat is missing or unreadable.</summary>
    public static LevelEffects? TryLoadCrackInTime(Func<string, byte[]?> read, out string? error)
    {
        error = null;
        if (read("effect.dat") is not { } effect) { error = "effect.dat could not be read."; return null; }
        EffectLibrary library;
        try { library = EffectLibrary.Read(effect); }
        catch (Exception e) { error = $"effect.dat could not be read: {e.Message}"; return null; }
        if (!ReferenceEquals(library.Layout, EffectLayout.CrackInTime))
            error = $"effect.dat has the {library.Layout.Name} layout.";

        VfxTextureSet textures = VfxTextureSet.Empty;
        string? textureError = null;
        if (read("vfx_system_header.dat") is { } header && read("vfx_system_texel.dat") is { } texel)
        {
            try { textures = VfxTextureSet.Read(header, texel); }
            catch (Exception e) { textureError = $"Effect textures unavailable ({e.Message}); sprites draw a soft glow."; }
        }
        else textureError = "vfx_system_header.dat / vfx_system_texel.dat missing; sprites draw a soft glow.";

        EffectConduit? conduit = null;
        string? conduitError = "fxconduit_packed.dat missing";
        if (read("fxconduit_packed.dat") is { } conduitBytes)
            conduit = EffectConduit.TryRead(conduitBytes, out conduitError);

        return new LevelEffects
        {
            Library = library,
            Textures = textures,
            Conduit = conduit,
            ConduitError = conduitError,
            TextureError = textureError,
            ControllerClass = CrackInTimeFxControllerClass,
            ActiveEventType = CrackInTimeActiveEvent,
        };
    }

    /// <summary>A level from its files' bytes: effect.dat, the level's texture pack, the game's global effect pack
    /// when found, and fxconduit.</summary>
    public static LevelEffects? FromBytes(byte[] effect, byte[]? levelHeader, byte[]? levelTexel,
        byte[]? globalHeader, byte[]? globalTexel, byte[]? conduitBytes, out string? error)
    {
        error = null;
        EffectLibrary library;
        try { library = EffectLibrary.Read(effect); }
        catch (Exception e) { error = $"effect.dat could not be read: {e.Message}"; return null; }

        VfxTextureSet textures = VfxTextureSet.Empty;
        string? textureError = null;
        if (levelHeader != null && levelTexel != null)
        {
            try
            {
                textures = VfxTextureSet.ReadOldEngine(levelHeader, levelTexel, globalHeader, globalTexel);
                library.BindGlobalTextures(textures.GlobalBase);
                library.ComposeOldEngineLayers(textures);
                if (textures.GlobalBase < 0)
                    textureError = "The game's effect textures (processed/texpacks/effect.tp in global_cached.psarc) were not found; " +
                                   "the pieces that use them draw a soft glow.";
                else
                {
                    // Quest for Booty's global pack is Tools of Destruction's plus five textures (69..73): a QfB level
                    // copied into a ToD install names textures the ToD pack does not have.
                    int globalCount = textures.Entries.Count - textures.GlobalBase;
                    int past = library.Emitters.SelectMany(e => e.Textures.Take(Math.Max(1, e.TextureCount)))
                        .Concat(library.Quads.SelectMany(q => q.Textures.Take(Math.Max(1, q.TextureCount))))
                        .Count(x => x.Handles[0] != 0xFFFF && (x.Handles[0] & 0x8000) == 0 && x.Handles[0] >= globalCount);
                    if (past > 0)
                        textureError = $"{past} piece(s) name game effect textures past this install's pack ({globalCount}); they draw a soft glow.";
                }
            }
            catch (Exception e) { textureError = $"Effect textures unavailable ({e.Message}); sprites draw a soft glow."; }
        }
        else textureError = "system.tph / system.tp missing; sprites draw a soft glow.";

        EffectConduit? conduit = null;
        string? conduitError = "fxconduit_packed.dat missing";
        if (conduitBytes != null)
        {
            try { conduit = EffectConduit.TryRead(conduitBytes, out conduitError); }
            catch (Exception e) { conduitError = e.Message; }
        }

        return new LevelEffects
        {
            Library = library,
            Textures = textures,
            Conduit = conduit,
            ConduitError = conduitError,
            TextureError = textureError,
        };
    }

    /// <summary>The FxControllers of an old-engine gameplay.dat, in table order. Controllers of other classes and
    /// records whose class data cannot be read are skipped.</summary>
    public static List<OldEffectController> ReadControllers(byte[] gameplay)
    {
        var result = new List<OldEffectController>();
        var file = new IGFile(new MemoryStream(gameplay, writable: false));
        var section = file.QuerySection(ControllerSection);
        if (section.count == 0 || section.length < 0x30) return result;
        ReadOnlySpan<byte> bytes = gameplay;
        for (int i = 0; i < section.count; i++)
        {
            long at = section.offset + (long)i * section.length;
            if (at + 0x30 > bytes.Length) break;
            ReadOnlySpan<byte> record = bytes.Slice((int)at, 0x30);
            uint dataOffset = BinaryPrimitives.ReadUInt32BigEndian(record);
            uint dataLength = BinaryPrimitives.ReadUInt32BigEndian(record[0x28..]);
            if (dataLength < 0x20 || (ulong)dataOffset + dataLength > (ulong)bytes.Length) continue;
            if (!TryFindClassData(bytes.Slice((int)dataOffset, (int)dataLength), out ReadOnlySpan<byte> data) || data.Length < 0x2C)
                continue;
            if (BinaryPrimitives.ReadUInt32BigEndian(data) != FxControllerClass) continue;

            Vector3 rotation = new(F32(record, 0x10), F32(record, 0x14), F32(record, 0x18));
            float min = Seconds(data, 0x24), max = Seconds(data, 0x28);
            result.Add(new OldEffectController(
                i,
                new Vector3(F32(record, 0x04), F32(record, 0x08), F32(record, 0x0C)),
                // As a Moby's rotation (EntityMoby): Z, then Y, then X.
                Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, rotation.Z)
                                     * Quaternion.CreateFromAxisAngle(Vector3.UnitY, rotation.Y)
                                     * Quaternion.CreateFromAxisAngle(Vector3.UnitX, rotation.X)),
                BinaryPrimitives.ReadUInt32BigEndian(data[0x14..]),
                data[0x1F] != 0,
                MathF.Min(min, max),
                MathF.Max(min, max)));
        }
        return result;
    }

    /// <summary>The 0x25200 payload of a class data IGHW (version 0.2: its section table starts at +0x10).
    /// Old-engine class data is big-endian; a little-endian one is not read.</summary>
    private static bool TryFindClassData(ReadOnlySpan<byte> mini, out ReadOnlySpan<byte> payload)
    {
        payload = default;
        if (mini.Length < 0x10 || BinaryPrimitives.ReadUInt32BigEndian(mini) != 0x49474857) return false;
        uint sections = BinaryPrimitives.ReadUInt32BigEndian(mini[0x08..]) & 0x0FFFFFFF;
        if (0x10UL + sections * 0x10UL > (ulong)mini.Length) return false;
        for (int i = 0; i < sections; i++)
        {
            ReadOnlySpan<byte> d = mini.Slice(0x10 + i * 0x10, 0x10);
            if (BinaryPrimitives.ReadUInt32BigEndian(d) != ClassDataSection) continue;
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(d[0x04..]);
            uint count = BinaryPrimitives.ReadUInt32BigEndian(d[0x08..]);
            uint stride = BinaryPrimitives.ReadUInt32BigEndian(d[0x0C..]);
            ulong size = stride == 0 ? count : (ulong)count * stride;
            if ((ulong)offset + size > (ulong)mini.Length) return false;
            payload = mini.Slice((int)offset, (int)size);
            return true;
        }
        return false;
    }

    private static float F32(ReadOnlySpan<byte> bytes, int at)
    {
        float value = BinaryPrimitives.ReadSingleBigEndian(bytes[at..]);
        return float.IsFinite(value) ? value : 0f;
    }

    private static float Seconds(ReadOnlySpan<byte> bytes, int at) => Math.Max(0f, F32(bytes, at));

    /// <summary>The effect a controller plays: its key's fxconduit record for (FxController, ACTIVE), the record's
    /// visual specification, and the effect that names - the first of the record's specifications this level carries.</summary>
    public EffectResolution Resolve(OldEffectController controller) => Resolve(controller.Key);

    /// <summary>The effect an FxController with fxconduit key <paramref name="key"/> plays (see <see cref="Resolve(OldEffectController)"/>).</summary>
    public EffectResolution Resolve(uint key)
    {
        if (Conduit == null) return new EffectResolution(null, -1, $"fxconduit not read ({ConduitError ?? "missing"})");
        var uses = Conduit.FindVisual(ControllerClass, ActiveEventType, key);
        if (uses.Count == 0) return new EffectResolution(null, -1, $"no fxconduit record for key {key}");
        foreach (var (record, spec, _) in uses.OrderByDescending(u => u.Chance))
            // A Crack in Time repeats the id at +0x20; the old engine's +0x20 is another effect, not tried.
            foreach (int id in spec.CrackInTime ? spec.EffectIds() : [spec.EffectId])
                if (Library.PackageForGlobalId(id) is { } package)
                    return new EffectResolution(package, id, $"key {key} -> record {record.Index} -> effect {id}");
        return new EffectResolution(null, uses[0].Spec.EffectId, $"effect {uses[0].Spec.EffectId} is not in this level's effect.dat");
    }

    /// <summary>Every key an FxController of this level can name: the keys of the conduit's (FxController, ACTIVE)
    /// visual records, in key order.</summary>
    public IReadOnlyList<uint> ControllerKeys() => Conduit == null ? [] :
        [.. Conduit.VisualRecords
            .Where(r => r.EventType == ActiveEventType && r.ClassId == ControllerClass && r.Word0C != 0)
            .Select(r => r.Word0C).Distinct().Order()];
}
