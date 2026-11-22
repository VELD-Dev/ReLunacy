using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Games;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>Fall of Man stores gameplay tables inside each named 0x25000 group.</summary>
internal sealed class FallOfManGameplayReader(IGFile gameplay, IGFile? debug)
{
    private const uint GameplaySection = 0x25000;
    internal const uint DebugSection = 0x25500;
    private const int GroupStride = 0xD0;
    private const int PathStride = 0x10;
    private const int VolumeStride = 0x90;
    private const int ClueStride = 0xA0;
    private const int ControllerStride = 0x30;

    internal readonly record struct Group(string Name, uint Offset, long DebugStart, long DebugCount);
    internal sealed record GroupData(List<Volume> Volumes, List<GameplayPath> Paths, List<GameplayController> Controllers,
        IReadOnlyDictionary<uint, string> MobyNames);

    private sealed class GroupNames
    {
        public Dictionary<uint, string> Volumes { get; } = [];
        public Dictionary<uint, string> Paths { get; } = [];
        public Dictionary<uint, string> Mobys { get; } = [];
        public Dictionary<uint, string> Clues { get; } = [];
        public Dictionary<uint, string> Controllers { get; } = [];
    }

    public IReadOnlyList<Group> Groups { get; } = ReadGroups(gameplay);

    private static List<Group> ReadGroups(IGFile gameplay)
    {
        var stream = gameplay.sh;
        var section = gameplay.QuerySection(GameplaySection);
        var groups = new List<Group>();
        if (section.id != GameplaySection || !Fits(stream, section.offset, 0x28)) return groups;
        uint start = stream.ReadUInt32(section.offset + 0x20);
        uint count = stream.ReadUInt32();
        if (start == 0 || !Fits(stream, start, count * (long)GroupStride)) return groups;

        long debugStart = 0;
        for (uint i = 0; i < count; i++)
        {
            uint offset = start + i * GroupStride;
            stream.Seek(offset);
            string name = ReadName(stream, 0x40);
            long debugCount = 0;
            // The debug table concatenates groups, with seven families interspersed within each group.
            for (uint table = 0x40; table <= 0xA0; table += 0x10)
                debugCount += stream.ReadUInt32(offset + table + 4);
            groups.Add(new Group(name.Length > 0 ? name : $"Group_{i}", offset, debugStart, debugCount));
            debugStart += debugCount;
        }
        return groups;
    }

    public GroupData Read(Group group)
    {
        var names = ReadNames(group);
        var paths = ReadPaths(group, names.Paths);
        var volumes = ReadVolumes(group, names.Volumes);
        ReadClues(group, names.Clues, volumes);
        var controllers = ReadControllers(group, names.Controllers);
        PathRoles.Assign(paths, volumes, controllers);
        return new GroupData(volumes, paths, controllers, names.Mobys);
    }

    private GroupNames ReadNames(Group group)
    {
        var result = new GroupNames();
        if (debug == null) return result;
        var section = debug.QuerySection(DebugSection);
        var stream = debug.sh;
        if (section.id != DebugSection || !Fits(stream, section.offset, 8)) return result;
        uint start = stream.ReadUInt32(section.offset);
        uint count = stream.ReadUInt32();
        // Without a complete name table, local indices repeated by multiplayer groups are ambiguous.
        if (start == 0 || !Fits(stream, start, count * 16L)
            || (Groups.Count > 1 && count != Groups.Sum(g => g.DebugCount)))
            return result;

        long end = Groups.Count == 1 ? count : group.DebugStart + group.DebugCount;
        for (long i = group.DebugStart; i < end; i++)
        {
            stream.Seek(start + i * 0x10);
            uint name = stream.ReadUInt32();
            stream.ReadUInt32(); // GUID string pointer
            uint index = stream.ReadUInt32();
            uint kind = stream.ReadUInt32();
            var names = kind switch
            {
                0 => result.Mobys,
                1 => result.Paths,
                2 => result.Volumes,
                3 => result.Clues,
                6 => result.Controllers,
                _ => null,
            };
            if (names == null || name == 0 || !Fits(stream, name, 1)) continue;
            stream.Seek(name);
            string value = ReadName(stream, (int)Math.Min(512, stream.BaseStream.Length - name));
            if (value.Length > 0) names.TryAdd(index, value);
        }
        return result;
    }

    private List<GameplayPath> ReadPaths(Group group, Dictionary<uint, string> names)
    {
        var paths = new List<GameplayPath>();
        var stream = gameplay.sh;
        uint start = stream.ReadUInt32(group.Offset + 0x50);
        uint count = stream.ReadUInt32();
        if (start == 0 || !Fits(stream, start, count * (long)PathStride)) return paths;

        for (uint i = 0; i < count; i++)
        {
            uint first = stream.ReadUInt32(start + i * PathStride);
            uint pointCount = stream.ReadUInt32();
            if (pointCount != 0 && (first == 0 || !Fits(stream, first, pointCount * 16L))) continue;
            var points = new List<Vector3>((int)pointCount);
            stream.Seek(first);
            for (uint point = 0; point < pointCount; point++)
            {
                points.Add(new Vector3(stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle()));
                stream.ReadSingle(); // The fourth component is -1 in retail, so derive distance from XYZ.
            }
            paths.Add(new GameplayPath(i, names.GetValueOrDefault(i) ?? $"Path_{i}", (int)i, null, points, kind: 0));
        }
        return paths;
    }

    private List<Volume> ReadVolumes(Group group, Dictionary<uint, string> names)
    {
        var volumes = new List<Volume>();
        var stream = gameplay.sh;
        uint start = stream.ReadUInt32(group.Offset + 0x60);
        uint count = stream.ReadUInt32();
        if (start == 0 || !Fits(stream, start, count * (long)VolumeStride)) return volumes;

        for (uint i = 0; i < count; i++)
        {
            uint offset = start + i * VolumeStride;
            stream.Seek(offset);
            var transform = ReadTransform(stream);
            var shape = ReadShape(stream.ReadUInt32(offset + 0x80));
            volumes.Add(new Volume(i, transform, names.GetValueOrDefault(i) ?? $"Volume_{i}", shape: shape)
            {
                Index = (int)i,
                UsesFallOfManShape = true,
            });
        }
        return volumes;
    }

    private void ReadClues(Group group, Dictionary<uint, string> names, List<Volume> volumes)
    {
        var stream = gameplay.sh;
        uint start = stream.ReadUInt32(group.Offset + 0x70);
        uint count = stream.ReadUInt32();
        if (start == 0 || !Fits(stream, start, count * (long)ClueStride)) return;

        int firstClue = volumes.Count;
        for (uint i = 0; i < count; i++)
        {
            long offset = start + i * (long)ClueStride;
            stream.Seek(offset);
            uint settingsOffset = stream.ReadUInt32();
            var transform = ReadTransform(stream);
            // Clues have their own transform and type enum. Their settings start directly with the
            // type-specific payload, unlike the class-number word at the start of a controller's struct.
            // game.self 0x46A574 and 0x41A3D4 copy this shape into the runtime volume's +0x80.
            stream.Seek(offset + 0x94);
            var shape = ReadShape(stream.ReadUInt32());
            uint type = stream.ReadUInt32();
            uint settingsSize = stream.ReadUInt32();
            string name = names.GetValueOrDefault(i) ?? $"Clue_{i}";
            volumes.Add(new Volume((ulong)firstClue + i, transform, name, shape: shape)
            {
                UsesFallOfManShape = true,
                Clue = new VolumeClue(type, name, ClueClasses.Name(type, [], GameId.RFallOfMan))
                {
                    Tuid = i,
                    Index = (int)i,
                    Settings = OldGameplayReader.ReadClassStruct(stream, settingsOffset, settingsSize) ?? [],
                },
            });
        }
    }

    private List<GameplayController> ReadControllers(Group group, Dictionary<uint, string> names)
    {
        var controllers = new List<GameplayController>();
        var stream = gameplay.sh;
        uint start = stream.ReadUInt32(group.Offset + 0x80);
        uint count = stream.ReadUInt32();
        if (start == 0 || !Fits(stream, start, count * (long)ControllerStride)) return controllers;

        for (uint i = 0; i < count; i++)
        {
            stream.Seek(start + i * (long)ControllerStride);
            uint settingsOffset = stream.ReadUInt32();
            var position = ReadVector(stream);
            var rotation = ReadVector(stream);
            var scale = ReadVector(stream);
            uint settingsSize = stream.ReadUInt32();
            byte[] settings = OldGameplayReader.ReadClassStruct(stream, settingsOffset, settingsSize) ?? [];
            uint cls = settings.Length >= 4 ? BinaryPrimitives.ReadUInt32BigEndian(settings) : 0;
            string? className = GameClassNames.Name(GameId.RFallOfMan, cls);
            controllers.Add(new GameplayController
            {
                Tuid = i,
                Index = (int)i,
                Name = names.GetValueOrDefault(i) ?? $"Controller_{i}",
                Class = cls,
                ClassName = className,
                Settings = settings,
                Position = position,
                Rotation = rotation,
                Scale = scale,
            });
        }
        return controllers;
    }

    private static Matrix4x4 ReadTransform(StreamHelper stream) => new(
        stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle(),
        stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle(),
        stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle(),
        stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle());

    private static Vector3 ReadVector(StreamHelper stream) => new(stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle());

    // game.self 0x4085D4 through 0x408754: full word, with sphere and cylinder swapped from ToD.
    private static VolumeShape ReadShape(uint value) => value switch
    {
        1 => VolumeShape.Sphere,
        2 => VolumeShape.Cylinder,
        3 => VolumeShape.Capsule,
        _ => VolumeShape.Box,
    };

    private static bool Fits(StreamHelper stream, long offset, long size) => offset + size <= stream.BaseStream.Length;

    private static string ReadName(StreamHelper stream, int maxBytes)
    {
        byte[] data = stream.ReadBytes(maxBytes);
        int terminator = Array.IndexOf(data, (byte)0);
        return Encoding.UTF8.GetString(data, 0, terminator >= 0 ? terminator : data.Length);
    }
}
