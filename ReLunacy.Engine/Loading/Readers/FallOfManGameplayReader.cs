using System.Numerics;
using System.Text;
using ReLunacy.Engine.Assets.LevelElements;
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

    internal readonly record struct Group(string Name, uint Offset, long DebugStart, long DebugCount);

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

    public (List<Volume> Volumes, List<GameplayPath> Paths, IReadOnlyDictionary<uint, string> MobyNames) Read(Group group)
    {
        var (volumeNames, pathNames, mobyNames) = ReadNames(group);
        var paths = ReadPaths(group, pathNames);
        var volumes = ReadVolumes(group, volumeNames);
        PathRoles.Assign(paths, volumes, []);
        return (volumes, paths, mobyNames);
    }

    private (Dictionary<uint, string> Volumes, Dictionary<uint, string> Paths, Dictionary<uint, string> Mobys) ReadNames(Group group)
    {
        var volumes = new Dictionary<uint, string>();
        var paths = new Dictionary<uint, string>();
        var mobys = new Dictionary<uint, string>();
        if (debug == null) return (volumes, paths, mobys);
        var section = debug.QuerySection(DebugSection);
        var stream = debug.sh;
        if (section.id != DebugSection || !Fits(stream, section.offset, 8)) return (volumes, paths, mobys);
        uint start = stream.ReadUInt32(section.offset);
        uint count = stream.ReadUInt32();
        // Without a complete name table, local indices repeated by multiplayer groups are ambiguous.
        if (start == 0 || !Fits(stream, start, count * 16L)
            || (Groups.Count > 1 && count != Groups.Sum(g => g.DebugCount)))
            return (volumes, paths, mobys);

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
                0 => mobys,
                1 => paths,
                2 => volumes,
                _ => null,
            };
            if (names == null || name == 0 || !Fits(stream, name, 1)) continue;
            stream.Seek(name);
            string value = ReadName(stream, (int)Math.Min(512, stream.BaseStream.Length - name));
            if (value.Length > 0) names.TryAdd(index, value);
        }
        return (volumes, paths, mobys);
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
            var transform = new Matrix4x4(
                stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle(),
                stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle(),
                stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle(),
                stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle());
            // game.self 0x4085D4 through 0x408754: full word, with sphere and cylinder swapped from ToD.
            var shape = stream.ReadUInt32(offset + 0x80) switch
            {
                1 => VolumeShape.Sphere,
                2 => VolumeShape.Cylinder,
                3 => VolumeShape.Capsule,
                _ => VolumeShape.Box,
            };
            volumes.Add(new Volume(i, transform, names.GetValueOrDefault(i) ?? $"Volume_{i}", shape: shape)
            {
                Index = (int)i,
                UsesFallOfManShape = true,
            });
        }
        return volumes;
    }

    private static bool Fits(StreamHelper stream, long offset, long size) => offset + size <= stream.BaseStream.Length;

    private static string ReadName(StreamHelper stream, int maxBytes)
    {
        byte[] data = stream.ReadBytes(maxBytes);
        int terminator = Array.IndexOf(data, (byte)0);
        return Encoding.UTF8.GetString(data, 0, terminator >= 0 ? terminator : data.Length);
    }
}
