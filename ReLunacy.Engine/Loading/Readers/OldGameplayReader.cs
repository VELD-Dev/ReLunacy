using System.Buffers.Binary;
using System.Numerics;
using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>What <see cref="OldGameplayReader"/> read besides the volumes and Mobys (which it annotates).</summary>
public sealed class OldGameplayData
{
    public List<GameplayPath> Paths { get; init; } = [];
    public List<GameplayController> Controllers { get; init; } = [];
    public List<GameplayGroup> Pods { get; init; } = [];
    public GameplayReferences References { get; init; } = GameplayReferences.None;

    /// <summary>The records of each family the file holds (Clues, Areas and Scents live in the volumes and the references).</summary>
    public int Clues, Areas, Scents;
    /// <summary>Handles that name nothing the file has (out of range): dropped from the references, counted here.</summary>
    public int UnresolvedReferences;
    /// <summary>Clues whose volume pointer is not a volume record.</summary>
    public int ClueVolumeMisses;
}

/// <summary>
/// The old engine's (Tools of Destruction, Quest for Booty) gameplay families beyond Mobys and volume matrices, all
/// from gameplay.dat, read only (docs/reverse/tod_volumes_clues_paths.md; Fianshoo's ReLunacy Resurgence found the
/// layouts, checked there on every level). Pointers inside the records are absolute file offsets.
///
///   0x7700 / 0x7710  paths / points      0x7780  Clues: +0 volume record, +4 / +8 class data (offset, size), +0xC class
///   0x77C0           controllers          0x7940 / 0x7950  Areas: +0 path u16s, +4 volume u16s, +8 / +0xA counts
///   0x7900 / 0x7910  Pods                 0x7980 / 0x7990  Scents (0x10000 | Clue index, as Pods hold Mobys)
///
/// A Clue's class data is raw; a controller's and a Moby's are a mini-IGHW whose section 0x25200 is the class struct
/// (the class number first). The fields of either that name another instance are the ones
/// the game's .fields file types as references (<see cref="ClassFields"/>). An instance's index in its table is what its handle holds and what
/// instances.lua numbers, so every list here is by table index.
/// </summary>
public sealed class OldGameplayReader
{
    private const uint PathId = 0x7700, PointId = 0x7710, VolumeId = 0x7740, ClueId = 0x7780, ControllerId = 0x77C0;
    private const uint PodId = 0x7900, PodMemberId = 0x7910, AreaId = 0x7940, ScentId = 0x7980, MobyId = 0x7340;
    private const uint ClassSection = 0x25200;
    private const int VolumeStride = 0x90;

    private readonly IGFile _file;
    private readonly DebugReader _names;
    private readonly Games.GameId? _game;
    private readonly long _length;

    public OldGameplayReader(IGFile gameplay, DebugReader names, Games.GameId? game)
    {
        _file = gameplay ?? throw new ArgumentNullException(nameof(gameplay));
        _names = names ?? throw new ArgumentNullException(nameof(names));
        _game = game;
        _length = gameplay.sh.BaseStream.Length;
    }

    // The name of an instance of a family by its table index; a placeholder when nothing names it.
    private string NameOf(string family, int index) =>
        _names.GetOldInstanceName(family, index) ?? $"{char.ToUpperInvariant(family[0])}{family[1..]}_{index}";

    private static readonly (string Kind, string Family, uint Section)[] Kinds =
    [
        ("Moby", "moby", MobyId), ("Path", "path", PathId), ("Volume", "volume", VolumeId), ("Clue", "clue", ClueId),
        ("Controller", "controller", ControllerId), ("Scent", "scent", ScentId), ("Area", "area", AreaId), ("Pod", "pod", PodId),
    ];

    private bool Fits(long offset, long size) => offset >= 0 && size >= 0 && offset + size <= _length;

    private byte[]? Slice(uint offset, uint size)
    {
        if (size == 0 || !Fits(offset, size) || size > 1 << 24) return null;
        _file.sh.Seek(offset);
        return _file.sh.ReadBytes((int)size);
    }

    /// <summary>The class struct of a controller's or Moby's mini-IGHW (section 0x25200): class number first. Null when
    /// the blob is not one.</summary>
    internal static byte[]? ReadClassStruct(StreamHelper stream, uint offset, uint size)
    {
        if (offset == 0 || size < 0x20 || size > 1 << 24 || offset + (long)size > stream.BaseStream.Length) return null;
        stream.Seek(offset);
        byte[] blob = stream.ReadBytes((int)size);
        if (BinaryPrimitives.ReadUInt32BigEndian(blob) != 0x49474857) return null;
        // Major 0 or 1: the section table follows a 0x10 or 0x20 byte header (the dump's second word is 0x00000002 here, major 0).
        int count = (int)BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(8));
        if (count < 1 || count > 16) return null;
        int table = BinaryPrimitives.ReadUInt16BigEndian(blob.AsSpan(4)) >= 1 ? 0x20 : 0x10;
        for (int i = 0; i < count; i++)
        {
            int at = table + i * 0x10;
            if (at + 0x10 > blob.Length || BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(at)) != ClassSection) continue;
            uint start = BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(at + 4)), length = BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(at + 8));
            return start + (long)length <= blob.Length ? blob.AsSpan((int)start, (int)length).ToArray() : null;
        }
        return null;
    }

    /// <summary>The Moby's class struct and the instances it names (see <see cref="OldClassReferences"/>); empty
    /// without class data. <paramref name="record"/> is its 0x48-byte record's file offset.</summary>
    public (byte[] Settings, List<ClueReference> References) ReadMobySettings(long record, OldGameplayData? sink = null)
    {
        _file.sh.Seek(record);
        uint pointer = _file.sh.ReadUInt32();
        _file.sh.Seek(record + 0x38);
        uint size = _file.sh.ReadUInt32();
        if (ReadClassStruct(_file.sh, pointer, size) is not { Length: >= 4 } settings) return ([], []);
        return (settings, References(settings, BinaryPrimitives.ReadUInt32BigEndian(settings), sink));
    }

    private int Count(uint section) => _file.QuerySection(section) is { id: not 0 } s ? (int)s.count : 0;

    private List<ClueReference> References(byte[] settings, uint cls, OldGameplayData? sink)
    {
        var references = new List<ClueReference>();
        foreach (var field in ClassFields.Of(_game, cls))
        {
            if (field.Kind != ClassFieldKind.Reference || field.TypeName is not { } kind || field.Offset + 4 > settings.Length) continue;
            int offset = field.Offset;
            uint word = BinaryPrimitives.ReadUInt32BigEndian(settings.AsSpan(offset));
            int index = OldClassReferences.Decode(kind, word);
            // 0 (Moby) or 0xFFFFFFFF is "none"; anything else outside the table names nothing.
            if (kind == "Moby" ? word == 0 : word == 0xFFFFFFFF) continue;
            int family = Array.FindIndex(Kinds, k => k.Kind == kind);
            if (index < 0 || family < 0 || index >= Count(Kinds[family].Section))
            {
                if (sink != null) sink.UnresolvedReferences++;
                continue;
            }
            references.Add(new ClueReference(offset, kind, NameOf(Kinds[family].Family, index), index));
        }
        return references;
    }

    /// <summary>Reads the families and annotates <paramref name="volumes"/> (indexed by table position) with their Clues,
    /// Areas and Scents.</summary>
    public OldGameplayData Read(IReadOnlyList<Volume> volumes)
    {
        var data = new OldGameplayData
        {
            Paths = ReadPaths(),
            Controllers = [],
            Pods = [],
            References = new GameplayReferences(false, [.. Kinds.Select(k => (k.Kind,
                (IReadOnlyList<(string Name, ushort Layer)>)Enumerable.Range(0, Count(k.Section)).Select(i => (NameOf(k.Family, i), (ushort)0)).ToList()))]),
        };
        try { ReadControllers(data); }
        catch (Exception e) when (e is IOException or EndOfStreamException or ArgumentException) { Console.WriteLine($"Controllers: could not be read: {e.Message}"); }
        try { ReadClues(data, volumes); }
        catch (Exception e) when (e is IOException or EndOfStreamException or ArgumentException) { Console.WriteLine($"Clues: could not be read: {e.Message}"); }
        try { ReadAreas(data, volumes); }
        catch (Exception e) when (e is IOException or EndOfStreamException or ArgumentException) { Console.WriteLine($"Areas: could not be read: {e.Message}"); }
        try { ReadPods(data); }
        catch (Exception e) when (e is IOException or EndOfStreamException or ArgumentException) { Console.WriteLine($"Pods: could not be read: {e.Message}"); }
        try { ReadScents(data, volumes); }
        catch (Exception e) when (e is IOException or EndOfStreamException or ArgumentException) { Console.WriteLine($"Scents: could not be read: {e.Message}"); }
        data.Areas = Count(AreaId);
        data.Scents = Count(ScentId);
        PathRoles.Assign(data.Paths, volumes, data.Controllers);
        ClueClasses.AssignVolumeRoles(volumes, _game, data.Controllers);
        Console.WriteLine($"Old gameplay: {data.Paths.Count} paths, {data.Controllers.Count} controllers, {data.Clues} Clues, " +
                          $"{data.Areas} Areas, {data.Pods.Count} Pods, {data.Scents} Scents.");
        return data;
    }

    private List<GameplayPath> ReadPaths()
    {
        var paths = new List<GameplayPath>();
        try
        {
            var section = _file.QuerySection(PathId);
            var points = _file.QuerySection(PointId);
            if (section.id != PathId || section.length < 0x10) return paths;
            for (int i = 0; i < section.count; i++)
            {
                _file.sh.Seek(section.offset + (long)i * section.length);
                uint first = _file.sh.ReadUInt32(), count = _file.sh.ReadUInt32();
                float length = _file.sh.ReadSingle();
                var list = new List<GameplayPathPoint>((int)Math.Min(count, 4096));
                // The points lie in 0x7710, 0x10 bytes each: x, y, z and the distance from the first.
                if (count <= 1 << 16 && first >= points.offset && Fits(first, count * 0x10L))
                {
                    _file.sh.Seek(first);
                    for (int k = 0; k < count; k++)
                        list.Add(new GameplayPathPoint(new Vector3(_file.sh.ReadSingle(), _file.sh.ReadSingle(), _file.sh.ReadSingle()), _file.sh.ReadSingle()));
                }
                paths.Add(new GameplayPath { Tuid = (ulong)i, Name = NameOf("path", i), Index = i, Length = length, Points = list });
            }
        }
        catch (Exception e) when (e is IOException or EndOfStreamException or ArgumentException)
        {
            Console.WriteLine($"Paths: could not be read: {e.Message}");
        }
        return paths;
    }

    private void ReadControllers(OldGameplayData data)
    {
        var section = _file.QuerySection(ControllerId);
        if (section.id != ControllerId || section.length < 0x30) return;
        for (int i = 0; i < section.count; i++)
        {
            long record = section.offset + (long)i * section.length;
            _file.sh.Seek(record);
            uint pointer = _file.sh.ReadUInt32();
            var position = new Vector3(_file.sh.ReadSingle(), _file.sh.ReadSingle(), _file.sh.ReadSingle());
            var rotation = new Vector3(_file.sh.ReadSingle(), _file.sh.ReadSingle(), _file.sh.ReadSingle());
            var scale = new Vector3(_file.sh.ReadSingle(), _file.sh.ReadSingle(), _file.sh.ReadSingle());
            uint size = _file.sh.ReadUInt32();
            byte[] settings = ReadClassStruct(_file.sh, pointer, size) is { Length: >= 4 } s ? s : [];
            uint cls = settings.Length >= 4 ? BinaryPrimitives.ReadUInt32BigEndian(settings) : 0;
            string? className = GameClassNames.Name(_game, cls);
            data.Controllers.Add(new GameplayController
            {
                Tuid = (ulong)i,
                Name = NameOf("controller", i),
                Index = i,
                Class = cls,
                ClassName = className,
                Settings = settings,
                References = settings.Length >= 4 ? References(settings, cls, data) : [],
                Position = position,
                Rotation = rotation,
                Scale = scale,
                IsCamera = className?.StartsWith("CamArg", StringComparison.Ordinal) == true,
            });
        }
    }

    private void ReadClues(OldGameplayData data, IReadOnlyList<Volume> volumes)
    {
        var section = _file.QuerySection(ClueId);
        var volumeSection = _file.QuerySection(VolumeId);
        if (section.id != ClueId || section.length < 0x10) return;
        var records = new List<(int Index, int Volume, uint Class, byte[] Settings)>();
        for (int i = 0; i < section.count; i++)
        {
            _file.sh.Seek(section.offset + (long)i * section.length);
            uint volume = _file.sh.ReadUInt32(), pointer = _file.sh.ReadUInt32(), size = _file.sh.ReadUInt32(), cls = _file.sh.ReadUInt32();
            int at = volume >= volumeSection.offset && (volume - volumeSection.offset) % VolumeStride == 0
                ? (int)((volume - volumeSection.offset) / VolumeStride) : -1;
            if (at < 0 || at >= volumes.Count) { data.ClueVolumeMisses++; continue; }
            records.Add((i, at, cls, Slice(pointer, size) ?? []));
        }
        data.Clues = (int)section.count;
        var classNames = records.GroupBy(r => r.Class).ToDictionary(g => g.Key,
            g => ClueClasses.Name(g.Key, g.Select(r => NameOf("clue", r.Index)), _game));
        foreach (var (index, volume, cls, settings) in records)
            volumes[volume].Clue = new VolumeClue(cls, NameOf("clue", index), classNames[cls])
            {
                Tuid = (ulong)index,
                Index = index,
                Settings = settings,
                References = References(settings, cls, data),
            };
    }

    private List<ushort> U16List(uint pointer, int count)
    {
        var list = new List<ushort>(count);
        if (count == 0 || !Fits(pointer, count * 2L)) return list;
        _file.sh.Seek(pointer);
        for (int k = 0; k < count; k++) list.Add(_file.sh.ReadUInt16());
        return list;
    }

    private List<uint> U32List(uint pointer, uint count)
    {
        var list = new List<uint>((int)Math.Min(count, 4096));
        if (count == 0 || count > 1 << 16 || !Fits(pointer, count * 4L)) return list;
        _file.sh.Seek(pointer);
        for (int k = 0; k < count; k++) list.Add(_file.sh.ReadUInt32());
        return list;
    }

    private void ReadAreas(OldGameplayData data, IReadOnlyList<Volume> volumes)
    {
        var section = _file.QuerySection(AreaId);
        if (section.id != AreaId || section.length < 0x0C) return;
        int pathCount = data.Paths.Count;
        for (int i = 0; i < section.count; i++)
        {
            _file.sh.Seek(section.offset + (long)i * section.length);
            uint pathList = _file.sh.ReadUInt32(), volumeList = _file.sh.ReadUInt32();
            ushort paths = _file.sh.ReadUInt16(), volumeEntries = _file.sh.ReadUInt16();
            string name = NameOf("area", i);
            foreach (ushort p in U16List(pathList, paths))
                if (p < pathCount) { if (!data.Paths[p].AreaIndexes.Contains(i)) data.Paths[p].AreaIndexes.Add(i); }
                else data.UnresolvedReferences++;
            foreach (ushort v in U16List(volumeList, volumeEntries))
                if (v < volumes.Count)
                {
                    if (!volumes[v].Areas.Contains(name)) volumes[v].Areas.Add(name);
                    if (!volumes[v].AreaIndexes.Contains(i)) volumes[v].AreaIndexes.Add(i);
                }
                else data.UnresolvedReferences++;
        }
    }

    private void ReadPods(OldGameplayData data)
    {
        var section = _file.QuerySection(PodId);
        if (section.id != PodId || section.length < 0x08) return;
        int mobys = Count(MobyId);
        for (int i = 0; i < section.count; i++)
        {
            _file.sh.Seek(section.offset + (long)i * section.length);
            uint pointer = _file.sh.ReadUInt32(), count = _file.sh.ReadUInt32();
            var names = new List<string>();
            var tuids = new List<ulong>();
            foreach (uint word in U32List(pointer, count))
            {
                int moby = OldClassReferences.Decode("Moby", word);
                if (moby < 0 || moby >= mobys) { data.UnresolvedReferences++; continue; }
                names.Add(NameOf("moby", moby));
                tuids.Add((ulong)moby);
            }
            data.Pods.Add(new GameplayGroup { Tuid = (ulong)i, Name = NameOf("pod", i), MemberNames = names, MemberTuids = tuids });
        }
    }

    private void ReadScents(OldGameplayData data, IReadOnlyList<Volume> volumes)
    {
        var section = _file.QuerySection(ScentId);
        if (section.id != ScentId || section.length < 0x08) return;
        var clueVolume = new Dictionary<int, Volume>();
        foreach (var volume in volumes)
            if (volume.Clue is { } clue) clueVolume[clue.Index] = volume;
        int clues = Count(ClueId);
        for (int i = 0; i < section.count; i++)
        {
            _file.sh.Seek(section.offset + (long)i * section.length);
            uint pointer = _file.sh.ReadUInt32(), count = _file.sh.ReadUInt32();
            string name = NameOf("scent", i);
            foreach (uint word in U32List(pointer, count))
            {
                int clue = OldClassReferences.DecodeScentMember(word);
                if (clue < 0 || clue >= clues) { data.UnresolvedReferences++; continue; }
                if (!clueVolume.TryGetValue(clue, out var volume)) continue;
                if (!volume.Scents.Contains(name)) volume.Scents.Add(name);
                if (!volume.ScentIndexes.Contains(i)) volume.ScentIndexes.Add(i);
            }
        }
    }
}
