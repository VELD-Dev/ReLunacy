using System.Buffers.Binary;
using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>
/// What gp_prius.dat says about its volumes besides their shapes: the editor layer each was placed in,
/// the Clue a volume is the shape of (with the Clue's settings), and the Areas and Scents that list it.
/// The instance families are named by the handle types of the region's own instances.lua (moby_handle,
/// path_handle, volume_handle, clue_handle, controller_handle, scent_handle, area_handle, pod_handle),
/// whose counts match the sections one for one (krell_canyon: 1614 / 416 / 960 + 313 / 313 / 868 / 61 /
/// 216 / 189). Each has its records and a 0x10-byte metadata section (TUID, name, layer):
///
///   0x25048 / 0x2504C  Mobys          0x2506C / 0x25070  controllers
///   0x25050 / 0x25054  paths          0x2508C / 0x25090  Scents: +0x00 Clue list, +0x04 its count
///   0x2505C / 0x25060  volumes        0x25080 / 0x25084  Areas: +0x00 paths, +0x04 volumes, +0x08 / +0x0A counts
///   0x25064 / 0x25068  Clues          0x25074 / 0x25078  Pods (Mobys)
///   0x25008            layers: a name at +0x00, indexed by the metadata's +0x0C ("gameplay", "cameras"...)
///
/// A Clue: +0x00 reference to its volume, +0x04 / +0x08 its settings in 0x25020 (absolute offset, size),
/// +0x0C its class. Its settings are the class's struct, starting with the class number; on All 4 One and
/// Into the Nexus wrapped in an IGHW of their own, the struct in 0x2501C and the references it makes in a
/// 0x25022 pool of its own, a reference field holding that entry's offset in the block.
///
/// A reference is four bytes, in one of two forms:
///  - A Crack in Time: two u16, (type &lt;&lt; 8 | layer, 0xF000 | index), the index counting that type's
///    instances within that layer in section order - the same (layer, index) instances.lua writes as
///    handle:new(layer, index).
///  - All 4 One, Into the Nexus: a file offset into the reference pool 0x25022, whose 0x10-byte entries are
///    (TUID, u32 type, 0).
/// The types are 0 Moby, 1 path, 2 volume, 3 Clue, 4 controller, 5 Scent, 6 Area (x4 in A Crack in Time's
/// form). PROVEN: every Clue lands on the volume named after it or "generatedVolumeForClue_" + it
/// (krell_canyon 313, zolar_forest 56, creature_collector 319, haunted_city 161); every list entry of those
/// levels' Areas, Scents and Pods, and every reference in their Clues' settings, resolves to an instance of
/// a sensible kind (a ClueGrindRail's rail path, a ClueCameraField's camera controller, a ClueRespawn's Area).
/// </summary>
public static class GameplayGroupReader
{
    private const uint LayerSection = 0x25008, PropertySection = 0x25020, ReferencePoolSection = 0x25022;
    private const uint ClueSection = 0x25064, AreaSection = 0x25080, ScentSection = 0x2508C;
    private const uint InnerStructSection = 0x2501C, InnerPoolSection = 0x25022;

    /// <summary>The instance kinds in reference-type order, with their metadata sections.</summary>
    private static readonly (string Kind, uint Metadata)[] Kinds =
    [
        ("Moby", 0x2504C), ("Path", 0x25054), ("Volume", 0x25060), ("Clue", 0x25068),
        ("Controller", 0x25070), ("Scent", 0x25090), ("Area", 0x25084), ("Pod", 0x25078),
    ];
    private const int MobyKind = 0, VolumeKind = 2, ClueKind = 3, ScentKind = 5, AreaKind = 6;

    private sealed class Index
    {
        public readonly List<(ulong Tuid, string Name, ushort Layer)>[] Instances = new List<(ulong, string, ushort)>[Kinds.Length];
        public readonly Dictionary<ushort, List<int>>[] ByLayer = new Dictionary<ushort, List<int>>[Kinds.Length];
        public readonly Dictionary<ulong, int>[] ByTuid = new Dictionary<ulong, int>[Kinds.Length];
        public bool Pooled;

        /// <summary>A Crack in Time's form: the runtime handle (kind &lt;&lt; 26 | layer &lt;&lt; 16 | 0xF000 | index),
        /// as the QForce loader builds it at EBOOT 0x007724F0 (Fianshoo's ReLunacy Resurgence).</summary>
        public (int Kind, int Index) Inline(ushort a, ushort b)
        {
            int kind = a >> 10;
            if ((b & 0xF000) != 0xF000 || kind >= Kinds.Length) return (-1, -1);
            return ByLayer[kind].TryGetValue((ushort)(a & 0x3FF), out var list) && (b & 0x0FFF) < list.Count
                ? (kind, list[b & 0x0FFF]) : (-1, -1);
        }

        public (int Kind, int Index) Pooled16(ReadOnlySpan<byte> entry)
        {
            ulong tuid = BinaryPrimitives.ReadUInt64BigEndian(entry);
            uint kind = BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
            return kind < Kinds.Length && ByTuid[kind].TryGetValue(tuid, out int index) ? ((int)kind, index) : (-1, -1);
        }

        public string NameOf((int Kind, int Index) r) => Instances[r.Kind][r.Index].Name;
    }

    public static void Annotate(IGFile prius, IReadOnlyList<Volume> volumes, IReadOnlyList<ushort> volumeGroups, Games.GameId? game = null)
    {
        try
        {
            var layers = ReadLayers(prius);
            var index = BuildIndex(prius);
            for (int i = 0; i < volumes.Count && i < volumeGroups.Count; i++)
                volumes[i].Layer = volumeGroups[i] < layers.Count ? layers[volumeGroups[i]] : null;

            // The instance the reference at a file offset names.
            (int Kind, int Index) Resolve(uint offset) => ResolveAt(prius, index, offset);
            // Volumes are read in section order, which is the index's order.
            Volume? VolumeAt(uint offset) => Resolve(offset) is (VolumeKind, var i) && i < volumes.Count ? volumes[i] : null;

            // Clues: each is a volume with a class and its settings.
            var clueMeta = index.Instances[ClueKind];
            var clueVolumes = new Volume?[clueMeta.Count];
            var clueClasses = new uint[clueMeta.Count];
            var clueSettings = new (byte[] Struct, List<ClueReference> References)[clueMeta.Count];
            var clues = prius.QuerySection(ClueSection);
            if (clues.id == ClueSection && clues.length >= 0x10)
                for (int i = 0; i < clueMeta.Count && i < clues.count; i++)
                {
                    uint record = clues.offset + (uint)i * clues.length;
                    prius.sh.Seek(record + 0x04);
                    uint settings = prius.sh.ReadUInt32(), size = prius.sh.ReadUInt32();
                    clueClasses[i] = prius.sh.ReadUInt32();
                    clueVolumes[i] = VolumeAt(record);
                    clueSettings[i] = ReadSettings(prius, index, settings, size);
                }
            var classNames = clueClasses.Distinct().ToDictionary(c => c,
                c => ClueClasses.Name(c, clueMeta.Where((_, i) => clueClasses[i] == c).Select(m => m.Name), game));
            for (int i = 0; i < clueMeta.Count; i++)
                if (clueVolumes[i] is { } volume)
                    volume.Clue = new VolumeClue(clueClasses[i], clueMeta[i].Name, classNames[clueClasses[i]])
                    {
                        Settings = clueSettings[i].Struct ?? [],
                        References = clueSettings[i].References ?? [],
                    };

            // Areas: their volumes list.
            var areaMeta = index.Instances[AreaKind];
            var areas = prius.QuerySection(AreaSection);
            if (areas.id == AreaSection && areas.length >= 0x0C)
                for (int i = 0; i < areaMeta.Count && i < areas.count; i++)
                {
                    prius.sh.Seek(areas.offset + (uint)i * areas.length + 0x04);
                    uint list = prius.sh.ReadUInt32();
                    prius.sh.Seek(areas.offset + (uint)i * areas.length + 0x0A);
                    ushort count = prius.sh.ReadUInt16();
                    foreach (uint reference in References(prius, list, count))
                        if (VolumeAt(reference) is { } volume && !volume.Areas.Contains(areaMeta[i].Name))
                            volume.Areas.Add(areaMeta[i].Name);
                }

            // Scents: their Clues, and through them the Clues' volumes.
            var scentMeta = index.Instances[ScentKind];
            var scents = prius.QuerySection(ScentSection);
            if (scents.id == ScentSection && scents.length >= 0x08)
                for (int i = 0; i < scentMeta.Count && i < scents.count; i++)
                {
                    prius.sh.Seek(scents.offset + (uint)i * scents.length);
                    uint list = prius.sh.ReadUInt32(), count = prius.sh.ReadUInt32();
                    foreach (uint reference in References(prius, list, count))
                        if (Resolve(reference) is (ClueKind, var clue) && clueVolumes[clue] is { } volume
                            && !volume.Scents.Contains(scentMeta[i].Name))
                            volume.Scents.Add(scentMeta[i].Name);
                }

            int clued = volumes.Count(v => v.Clue != null);
            Console.WriteLine($"Volumes: {volumes.Count} ({clued} Clues in {classNames.Count} classes), {layers.Count} layers, " +
                              $"{areaMeta.Count} areas, {scentMeta.Count} scents.");
        }
        catch (Exception ex)
        {
            // Annotations only: the volumes themselves are already read.
            Console.WriteLine($"Volumes: could not read their Clues, Areas and Scents: {ex.Message}");
        }
    }

    private static Index BuildIndex(IGFile prius)
    {
        var index = new Index { Pooled = prius.QuerySection(ReferencePoolSection).id == ReferencePoolSection };
        for (int k = 0; k < Kinds.Length; k++)
        {
            var list = ReadMetadata(prius, Kinds[k].Metadata);
            index.Instances[k] = list;
            index.ByLayer[k] = [];
            index.ByTuid[k] = [];
            for (int i = 0; i < list.Count; i++)
            {
                if (!index.ByLayer[k].TryGetValue(list[i].Layer, out var layer)) index.ByLayer[k][list[i].Layer] = layer = [];
                layer.Add(i);
                index.ByTuid[k].TryAdd(list[i].Tuid, i);
            }
        }
        return index;
    }

    /// <summary>A Clue's settings: the class struct (unwrapped from its own IGHW where it has one), and the
    /// fields of it that reference other instances.</summary>
    private static (byte[] Struct, List<ClueReference> References) ReadSettings(IGFile prius, Index index, uint offset, uint size)
    {
        var references = new List<ClueReference>();
        if (offset == 0 || size < 4 || offset + (long)size > prius.sh.BaseStream.Length) return ([], references);
        byte[] block = prius.sh.ReadFromOffset((int)size, offset);
        var span = block.AsSpan();

        if (block.Length >= 0x10 && BinaryPrimitives.ReadUInt32BigEndian(span) == 0x49474857) // "IGHW"
        {
            uint sections = BinaryPrimitives.ReadUInt32BigEndian(span[0x08..]);
            (uint Offset, uint Length) structSection = default, pool = default;
            for (uint s = 0; s < sections && 0x20 + s * 0x10 <= block.Length; s++)
            {
                var header = span[(int)(0x10 + s * 0x10)..];
                uint id = BinaryPrimitives.ReadUInt32BigEndian(header);
                var range = (BinaryPrimitives.ReadUInt32BigEndian(header[4..]), BinaryPrimitives.ReadUInt32BigEndian(header[8..]));
                if (id == InnerStructSection) structSection = range;
                else if (id == InnerPoolSection) pool = range;
            }
            if (structSection.Length == 0 || structSection.Offset + structSection.Length > block.Length) return ([], references);
            byte[] body = span.Slice((int)structSection.Offset, (int)structSection.Length).ToArray();
            for (int w = 4; w + 4 <= body.Length; w += 4)
            {
                uint value = BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(w));
                if (pool.Length == 0 || value < pool.Offset || value >= pool.Offset + pool.Length
                    || (value - pool.Offset) % 0x10 != 0 || value + 0x0C > block.Length) continue;
                var target = index.Pooled16(span[(int)value..]);
                if (target.Kind >= 0) references.Add(new ClueReference(w, Kinds[target.Kind].Kind, index.NameOf(target)));
            }
            return (body, references);
        }

        for (int w = 4; w + 4 <= block.Length; w += 4)
        {
            var target = index.Inline(BinaryPrimitives.ReadUInt16BigEndian(span[w..]), BinaryPrimitives.ReadUInt16BigEndian(span[(w + 2)..]));
            if (target.Kind >= 0) references.Add(new ClueReference(w, Kinds[target.Kind].Kind, index.NameOf(target)));
        }
        return (block, references);
    }

    private const uint ControllerSection = 0x2506C;
    /// <summary>The camera controller classes found by the naming rule over krell_canyon, zolar_forest,
    /// creature_collector and haunted_city - the same numbers in all three games - so that a level with few
    /// of one (krell_canyon's four 0x402, two of them named cam...) still gets them.</summary>
    private static readonly HashSet<uint> KnownCameraClasses = [0xD2, 0x333, 0x360, 0x402, 0x631, 0xAB5];
    private const int ControllerKind = 4;

    /// <summary>
    /// The region's controllers (gp_prius 0x2506C; see <see cref="GameplayController"/>), each flagged a
    /// camera or not. Nothing names the controller classes next to their numbers, and the numbers differ
    /// between games, so a class counts as a camera's when most of the level's controllers of that class
    /// are named after one: "cam..." or "CamArg...", or it is one of <see cref="KnownCameraClasses"/> (creature_collector: 0x631 is 95 CamArgBlendable of 96,
    /// 0x360 the CamArgFixed / goldboltcam / wall_kick_cam ones, 0xD2 and 0x402 the cinematic cam_...;
    /// FX, sound and spawner controllers are never named so).
    /// </summary>
    public static List<GameplayController> ReadControllers(IGFile prius, Games.GameId? game = null)
    {
        var controllers = new List<GameplayController>();
        try
        {
            var layers = ReadLayers(prius);
            var index = BuildIndex(prius);
            var meta = index.Instances[ControllerKind];
            var section = prius.QuerySection(ControllerSection);
            if (section.id != ControllerSection || section.length < 0x2C) return controllers;
            var classes = new uint[meta.Count];
            var settingsOf = new (byte[] Struct, List<ClueReference> References)[meta.Count];
            var records = new (System.Numerics.Vector3 Position, System.Numerics.Vector3 Rotation, System.Numerics.Vector3 Scale)[meta.Count];
            for (int i = 0; i < meta.Count && i < section.count; i++)
            {
                uint record = section.offset + (uint)i * section.length;
                prius.sh.Seek(record);
                uint settings = prius.sh.ReadUInt32(), size = prius.sh.ReadUInt32();
                var position = new System.Numerics.Vector3(prius.sh.ReadSingle(), prius.sh.ReadSingle(), prius.sh.ReadSingle());
                var rotation = new System.Numerics.Vector3(prius.sh.ReadSingle(), prius.sh.ReadSingle(), prius.sh.ReadSingle());
                var scale = new System.Numerics.Vector3(prius.sh.ReadSingle(), prius.sh.ReadSingle(), prius.sh.ReadSingle());
                records[i] = (position, rotation, scale);
                classes[i] = ReadClass(prius, settings, size);
                settingsOf[i] = ReadSettings(prius, index, settings, size);
            }

            static bool CameraNamed(string name) =>
                name.StartsWith("cam", StringComparison.OrdinalIgnoreCase) || name.Contains("CamArg", StringComparison.OrdinalIgnoreCase);
            var cameraClasses = Enumerable.Range(0, meta.Count).GroupBy(i => classes[i])
                .Where(g => GameClassNames.Name(game, g.Key) is { } name
                    ? name.StartsWith("CamArg", StringComparison.Ordinal)
                    : KnownCameraClasses.Contains(g.Key) || g.Count(i => CameraNamed(meta[i].Name)) * 2 > g.Count())
                .Select(g => g.Key).ToHashSet();

            for (int i = 0; i < meta.Count && i < section.count; i++)
                controllers.Add(new GameplayController
                {
                    Tuid = meta[i].Tuid,
                    Name = meta[i].Name,
                    Layer = meta[i].Layer < layers.Count ? layers[meta[i].Layer] : null,
                    Class = classes[i],
                    ClassName = GameClassNames.Name(game, classes[i]),
                    Settings = settingsOf[i].Struct ?? [],
                    References = settingsOf[i].References ?? [],
                    Position = records[i].Position,
                    Rotation = records[i].Rotation,
                    Scale = records[i].Scale,
                    IsCamera = cameraClasses.Contains(classes[i]),
                });
            Console.WriteLine($"Controllers: {controllers.Count}, {controllers.Count(c => c.IsCamera)} cameras " +
                              $"(classes {string.Join(", ", cameraClasses.Select(c => $"0x{c:X}"))}).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Controllers: could not be read: {ex.Message}");
        }
        return controllers;
    }

    private const uint PathSection = 0x25050, PathPointSection = 0x25058;
    private const uint PodSection = 0x25074, SquadronSection = 0x250A0, SquadronMetadataSection = 0x250A4;
    private const int PathKind = 1, PodKind = 7;

    /// <summary>The region's paths with their points (see <see cref="GameplayPath"/>).</summary>
    public static List<GameplayPath> ReadPaths(IGFile prius)
    {
        var paths = new List<GameplayPath>();
        try
        {
            var layers = ReadLayers(prius);
            var meta = ReadMetadata(prius, Kinds[PathKind].Metadata);
            var section = prius.QuerySection(PathSection);
            if (section.id != PathSection || section.length < 0x10) return paths;
            long end = prius.sh.BaseStream.Length;
            for (int i = 0; i < meta.Count && i < section.count; i++)
            {
                prius.sh.Seek(section.offset + (uint)i * section.length);
                uint first = prius.sh.ReadUInt32();
                prius.sh.ReadUInt32();
                float length = prius.sh.ReadSingle();
                byte closed = prius.sh.ReadByte(), kind = prius.sh.ReadByte();
                ushort count = prius.sh.ReadUInt16();
                var points = new List<GameplayPathPoint>(count);
                if (first != 0 && first + count * 0x10L <= end)
                {
                    prius.sh.Seek(first);
                    for (int k = 0; k < count; k++)
                        points.Add(new GameplayPathPoint(
                            new System.Numerics.Vector3(prius.sh.ReadSingle(), prius.sh.ReadSingle(), prius.sh.ReadSingle()),
                            prius.sh.ReadSingle()));
                }
                paths.Add(new GameplayPath
                {
                    Tuid = meta[i].Tuid,
                    Name = meta[i].Name,
                    Layer = meta[i].Layer < layers.Count ? layers[meta[i].Layer] : null,
                    Length = length,
                    Closed = closed != 0,
                    Kind = kind,
                    Points = points,
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Paths: could not be read: {ex.Message}");
        }
        return paths;
    }

    /// <summary>The region's Pods, their Mobys resolved to names (see <see cref="GameplayGroup"/>).</summary>
    public static List<GameplayGroup> ReadPods(IGFile prius)
    {
        var pods = new List<GameplayGroup>();
        try
        {
            var layers = ReadLayers(prius);
            var index = BuildIndex(prius);
            var meta = index.Instances[PodKind];
            var section = prius.QuerySection(PodSection);
            if (section.id != PodSection || section.length < 0x08) return pods;
            for (int i = 0; i < meta.Count && i < section.count; i++)
            {
                prius.sh.Seek(section.offset + (uint)i * section.length);
                uint list = prius.sh.ReadUInt32(), count = prius.sh.ReadUInt32();
                var names = new List<string>();
                var tuids = new List<ulong>();
                foreach (uint reference in References(prius, list, count))
                {
                    var target = ResolveAt(prius, index, reference);
                    if (target.Kind < 0) continue;
                    names.Add(index.NameOf(target));
                    tuids.Add(index.Instances[target.Kind][target.Index].Tuid);
                }
                pods.Add(new GameplayGroup
                {
                    Tuid = meta[i].Tuid,
                    Name = meta[i].Name,
                    Layer = meta[i].Layer < layers.Count ? layers[meta[i].Layer] : null,
                    MemberNames = names,
                    MemberTuids = tuids,
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Pods: could not be read: {ex.Message}");
        }
        return pods;
    }

    /// <summary>The region's Squadrons and the Tie placements they list (see <see cref="GameplayGroup"/>).</summary>
    public static List<GameplayGroup> ReadSquadrons(IGFile prius)
    {
        var squadrons = new List<GameplayGroup>();
        try
        {
            var layers = ReadLayers(prius);
            var meta = ReadMetadata(prius, SquadronMetadataSection);
            var section = prius.QuerySection(SquadronSection);
            if (section.id != SquadronSection || section.length < 0x08) return squadrons;
            long end = prius.sh.BaseStream.Length;
            for (int i = 0; i < meta.Count && i < section.count; i++)
            {
                prius.sh.Seek(section.offset + (uint)i * section.length);
                uint list = prius.sh.ReadUInt32(), count = prius.sh.ReadUInt32();
                var tuids = new List<ulong>();
                if (list != 0 && list + count * 0x10L <= end)
                    for (uint k = 0; k < count; k++)
                    {
                        prius.sh.Seek(list + k * 0x10);
                        tuids.Add(prius.sh.ReadUInt64());
                    }
                squadrons.Add(new GameplayGroup
                {
                    Tuid = meta[i].Tuid,
                    Name = meta[i].Name,
                    Layer = meta[i].Layer < layers.Count ? layers[meta[i].Layer] : null,
                    MemberTuids = tuids,
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Squadrons: could not be read: {ex.Message}");
        }
        return squadrons;
    }

    /// <summary>The instance the reference at a file offset names, in either form.</summary>
    private static (int Kind, int Index) ResolveAt(IGFile prius, Index index, uint offset)
    {
        prius.sh.Seek(offset);
        if (!index.Pooled) return index.Inline(prius.sh.ReadUInt16(), prius.sh.ReadUInt16());
        uint entry = prius.sh.ReadUInt32();
        if (entry + 0x0CL > prius.sh.BaseStream.Length) return (-1, -1);
        return index.Pooled16(prius.sh.ReadFromOffset(0x0C, entry));
    }

    /// <summary>The class number a settings block starts with (inside its own IGHW's 0x2501C where it has one).</summary>
    private static uint ReadClass(IGFile prius, uint offset, uint size)
    {
        if (offset == 0 || size < 4 || offset + (long)size > prius.sh.BaseStream.Length) return 0;
        byte[] block = prius.sh.ReadFromOffset((int)size, offset);
        var span = block.AsSpan();
        if (block.Length < 0x10 || BinaryPrimitives.ReadUInt32BigEndian(span) != 0x49474857)
            return BinaryPrimitives.ReadUInt32BigEndian(span);
        uint sections = BinaryPrimitives.ReadUInt32BigEndian(span[0x08..]);
        for (uint s = 0; s < sections && 0x20 + s * 0x10 <= block.Length; s++)
        {
            var header = span[(int)(0x10 + s * 0x10)..];
            if (BinaryPrimitives.ReadUInt32BigEndian(header) != InnerStructSection) continue;
            uint at = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
            return at + 4 <= block.Length ? BinaryPrimitives.ReadUInt32BigEndian(span[(int)at..]) : 0;
        }
        return 0;
    }

    private static List<string> ReadLayers(IGFile prius)
    {
        var section = prius.QuerySection(LayerSection);
        var layers = new List<string>();
        if (section.id != LayerSection || section.length == 0) return layers;
        for (uint i = 0; i < section.count; i++)
            layers.Add(prius.sh.ReadString(section.offset + i * section.length));
        return layers;
    }

    private static List<(ulong Tuid, string Name, ushort Layer)> ReadMetadata(IGFile prius, uint id)
    {
        var section = prius.QuerySection(id);
        var list = new List<(ulong, string, ushort)>();
        if (section.id != id || section.length < 0x10) return list;
        for (uint i = 0; i < section.count; i++)
        {
            prius.sh.Seek(section.offset + i * section.length);
            ulong tuid = prius.sh.ReadUInt64();
            uint name = prius.sh.ReadUInt32();
            ushort layer = prius.sh.ReadUInt16();
            list.Add((tuid, name != 0 ? prius.sh.ReadString(name) : string.Empty, layer));
        }
        return list;
    }

    /// <summary>The offsets of a list's references (four bytes each); empty when it lies outside the file.</summary>
    private static IEnumerable<uint> References(IGFile prius, uint offset, uint count)
    {
        if (offset == 0 || count == 0 || offset + count * 4L > prius.sh.BaseStream.Length) yield break;
        for (uint i = 0; i < count; i++)
            yield return offset + i * 4;
    }
}
