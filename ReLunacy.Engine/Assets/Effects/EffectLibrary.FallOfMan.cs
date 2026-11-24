using System.Numerics;

namespace ReLunacy.Engine.Assets.Effects;

public sealed partial class EffectLibrary
{
    private static readonly TrackSpec[] FallOfManEmitterTracks =
    [
        new("Spawn rate", 0x04, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Spawn life", 0x10, KeyKind.Scalar20, EffectTrackDomain.PieceLife),
        new("Spawn speed", 0x1C, KeyKind.Scalar20, EffectTrackDomain.PieceLife),
        new("Spawn radius", 0x28, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Emitter alpha", 0x34, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Emitter scale", 0x40, KeyKind.Scalar16, EffectTrackDomain.PieceLife),
        new("Particle colour", 0x4C, KeyKind.Color32, EffectTrackDomain.ParticleLife),
        new("Particle alpha", 0x58, KeyKind.Scalar16, EffectTrackDomain.ParticleLife),
        new("Particle size", 0x64, KeyKind.Scalar16, EffectTrackDomain.ParticleLife),
        new("Particle spin", 0x70, KeyKind.Rot16, EffectTrackDomain.ParticleLife),
    ];

    private static EffectLibrary ReadFallOfMan(EffectChunkFile file)
    {
        EffectLayout layout = EffectLayout.FallOfMan;
        var packages = new List<EffectPackage>();
        var quads = new List<EffectQuad>();
        var lights = new List<EffectLight>();
        var emitters = new List<EffectEmitter>();
        var validation = new List<string>();
        var pieces = new Dictionary<(int Type, int Index), EffectPiece>();
        var root = file.ChunksWithId(5).First();
        int listCount = root.Length < 4 ? 0 : (int)Math.Min(file.U32(root.Offset), (uint)((root.Length - 4) / 4));
        for (int i = 0; i < listCount; i++)
        {
            int list = file.Pointer(root.Offset + 4 + 4 * i);
            int listChunk = file.ChunkIndexAt(list);
            if (listChunk < 0 || file.Chunks[listChunk].End - list < 12) continue;
            int type = (int)file.U32(list);
            int array = file.Pointer(list + 8);
            int chunk = file.ChunkIndexAt(array);
            if (array == 0 || chunk < 0) continue;
            int count = (int)Math.Min(file.U32(list + 4), (uint)((file.Chunks[chunk].End - array) / 4));
            for (int j = 0; j < count; j++)
            {
                int at = file.Pointer(array + j * 4);
                int record = file.ChunkIndexAt(at);
                if (record < 0 || file.Chunks[record].Offset != at) continue;
                int size = file.Chunks[record].Length;
                EffectPiece? piece = type switch
                {
                    0 when size == 0x68 => ReadFallOfManPackage(file, j, at),
                    2 when size == 0xE0 => ReadOldQuad(file, quads.Count, at),
                    3 when size == 0xDC => ReadOldLight(file, lights.Count, at),
                    6 or 7 or 11 or 12 or 13 or 16 or 17 or 22 or 23 or 24 or 25 when size >= 0x150
                        => ReadFallOfManEmitter(file, type, emitters.Count, at, size),
                    _ => null,
                };
                switch (piece)
                {
                    case EffectPackage package: packages.Add(package); break;
                    case EffectQuad quad: quads.Add(quad); break;
                    case EffectLight light: lights.Add(light); break;
                    case EffectEmitter emitter: emitters.Add(emitter); break;
                    default: validation.Add($"Unsupported Fall of Man effect piece {type}:{j} (0x{size:X} bytes)."); continue;
                }
                pieces[(type, j)] = piece;
            }
        }

        foreach (var package in packages)
            foreach (var component in package.Components)
            {
                component.Piece = pieces.GetValueOrDefault((component.Raw[0x28], component.PieceIndex));
                if (component.Piece == null)
                    validation.Add($"{package.DisplayName} component {component.Slot} has an unresolved piece.");
            }

        var globalIds = new Dictionary<int, int>();
        var table = file.ChunksWithId(9).FirstOrDefault();
        int space = 0;
        if (table.Length >= 8)
        {
            // game.self 0x6B1008 looks up keys >= 10000 after the local range stored at +4.
            int count = (int)Math.Min(file.U32(table.Offset), (uint)((table.Length - 8) / 4));
            int sharedStart = (int)file.U32(table.Offset + 4);
            space = count;
            for (int slot = 0; slot < count; slot++)
            {
                int index = (int)file.U32(table.Offset + 8 + slot * 4);
                if (pieces.GetValueOrDefault((0, index)) is not EffectPackage package) continue;
                int id = slot < sharedStart ? slot : 10000 + slot - sharedStart;
                globalIds[id] = packages.IndexOf(package);
                package.GlobalIds.Add(id);
            }
        }

        return new EffectLibrary
        {
            File = file, Layout = layout, Packages = packages, Quads = quads, Lights = lights, Emitters = emitters,
            GlobalIds = globalIds, GlobalIdSpace = space, Validation = validation,
        };
    }

    private static EffectPackage ReadFallOfManPackage(EffectChunkFile file, int index, int at)
    {
        const int size = 0x68;
        var components = new List<EffectComponent>();
        int array = file.Pointer(at);
        int chunk = file.ChunkIndexAt(array);
        if (array >= 4 && chunk >= 0)
        {
            int count = (int)Math.Min(file.U32(array - 4), (uint)((file.Chunks[chunk].End - array) / size));
            for (int i = 0; i < count; i++)
            {
                int c = array + i * size;
                var type = file.U8(c + 0x28) switch
                {
                    0 => EffectPieceType.Package,
                    2 => EffectPieceType.Quad,
                    3 => EffectPieceType.Light,
                    _ => EffectPieceType.Emitter,
                };
                components.Add(new EffectComponent
                {
                    Slot = i, FileOffset = c, Type = type, PieceIndex = file.U16(c + 0x1E),
                    StartDelay = Finite(file.F32(c)), SpawnChance = Math.Clamp(Finite(file.F32(c + 8)), 0f, 1f),
                    Offset = new Vector3(0f, 0f, Finite(file.F32(c + 0x20))),
                    OffsetSpread = new Vector3(0f, 0f, Finite(file.F32(c + 0x24))),
                    Attached = file.U8(c + 0x1C) == 1, OrientationMode = file.U8(c + 0x29),
                    TiltMinDegrees = file.U8(c + 0x2A), TiltMaxDegrees = file.U8(c + 0x2B),
                    AzimuthMinDegrees = file.S16(c + 0x2C), AzimuthMaxDegrees = file.S16(c + 0x2E),
                    CycleMax = Finite(file.F32(c + 0x50)), CycleMin = Finite(file.F32(c + 0x54)),
                    OldEngine = true, Raw = RawOf(file, c, size),
                });
            }
        }

        return new EffectPackage
        {
            Type = EffectPieceType.Package, Index = index, FileOffset = at,
            Raw = RawOf(file, at, size), Components = components, ScalarTracks = [], ColorTracks = [],
        };
    }

    private static EffectEmitter ReadFallOfManEmitter(EffectChunkFile file, int type, int index, int at, int size)
    {
        bool spin = type is 6 or 7 or >= 22;
        int shift = spin ? 0 : -0x0C;
        int motion = type switch
        {
            6 or 7 or 22 => 0x13C,
            11 or 12 => 0x134,
            13 => 0x130,
            16 or 17 => 0x164,
            23 => 0x140,
            25 => 0x138,
            _ => 0x144,
        };
        // Fall of Man's emitter names (game.self) group several acceleration variants under
        // each of the preview renderer's classes.
        int oldType = type switch
        {
            11 or 12 or 13 => OldEngineEffectTypes.Beam,
            16 or 17 => OldEngineEffectTypes.Streak,
            22 => OldEngineEffectTypes.Smoke,
            23 => OldEngineEffectTypes.Flame,
            24 => OldEngineEffectTypes.Distort,
            25 => OldEngineEffectTypes.Trail,
            _ => OldEngineEffectTypes.CamFacing,
        };
        int count = Math.Min((int)file.U8(at + 0x100 + shift), 8);
        var (scalars, colors) = ReadTracks(file, at, spin ? FallOfManEmitterTracks : FallOfManEmitterTracks[..^1]);
        float period = Finite(file.F32(at + 0x108 + shift));
        float duration = Finite(file.F32(at + 0x10C + shift));
        return new EffectEmitter
        {
            Type = EffectPieceType.Emitter, Index = index, FileOffset = at, Raw = RawOf(file, at, size),
            ScalarTracks = scalars, ColorTracks = colors,
            Textures = ReadOldTextures(file, at + 0x7C + shift, count), TextureCount = count,
            OldEngineType = oldType, EmitterClass = OldEngineEffectTypes.EmitterClass(oldType),
            ShaderId = oldType switch
            {
                OldEngineEffectTypes.Beam => 3,
                OldEngineEffectTypes.Streak => 4,
                OldEngineEffectTypes.Smoke => 5,
                OldEngineEffectTypes.Flame => 6,
                OldEngineEffectTypes.Distort => 8,
                OldEngineEffectTypes.Trail => 9,
                _ => 0,
            },
            IsDistortion = oldType == OldEngineEffectTypes.Distort,
            Direction = Vector3.UnitZ, SpreadDegrees = 180f,
            OldPeriod = period, OldPeriodDefault = duration, Duration = period > 1e-4f ? period : duration,
            SpawnStyle = file.U8(at + 0x128 + shift),
            ConeMinDegrees = file.U8(at + 0x129 + shift), ConeMaxDegrees = file.U8(at + 0x12A + shift),
            SpawnInheritVelocity = Finite(file.F32(at + 0x12C + shift)),
            BeamStretch = oldType == OldEngineEffectTypes.Beam ? Finite(file.F32(at + 0x12C)) : 0,
            StreakPoints = oldType == OldEngineEffectTypes.Streak ? file.U8(at + 0x12D) : 0,
            MotionMode = file.U8(at + motion),
            Acceleration = Finite(file.F32(at + motion + 4)),
        };
    }
}
