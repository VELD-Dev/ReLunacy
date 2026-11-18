using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Readers;

// Reads human-readable names for assets and instances from debug.dat, when present.
public sealed class DebugReader
{
    private readonly IGFile? _debugFile;
    private readonly Dictionary<ulong, string> _mobyPrototypeNames = [];
    private readonly Dictionary<ulong, string> _tiePrototypeNames = [];
    private readonly Dictionary<ulong, string> _shaderNames = [];
    // Old engine only, matched by array position (names[i] <-> instance[i]). New engine reads
    // instance names from gp_prius.dat (mobys) or the zone's own file (ties), not debug.dat.
    private readonly List<string?> _mobyInstanceNames = [];
    private readonly List<string?> _tieInstanceNames = [];
    // Index-aligned with the old-engine volume array (gameplay.dat section 0x7740, see
    // RegionReader.ReadVolumesOld). Must Add an entry (even null) per record - skipping one
    // desyncs every name after it from its volume index.
    private readonly List<string?> _volumeNames = [];
    // Old engine, the other gameplay families by position, from debug.dat (see OldFamilySections).
    private readonly Dictionary<string, List<string?>> _familyNames = [];
    private readonly bool _isOld;

    /// <summary>Old engine: the level's instances.lua names (Tools of Destruction's every level, where debug.dat is
    /// absent); empty on the new engine. debug.dat's names win where both exist: they are the same.</summary>
    public OldInstanceNames InstanceNames { get; } = new();

    // debug.dat's name sections of the old gameplay families: the gameplay section's id + 0x20.
    private static readonly (string Family, uint Section)[] OldFamilySections =
    [
        ("path", 0x7720), ("clue", 0x77A0), ("controller", 0x77D0), ("pod", 0x7920), ("area", 0x7960), ("scent", 0x79A0),
    ];

    public DebugReader(FileManager fileManager)
    {
        ArgumentNullException.ThrowIfNull(fileManager);
        _isOld = fileManager.isOld;
        if (_isOld) InstanceNames = OldInstanceNames.Read(fileManager.SourcePath);

        if (fileManager.igfiles.TryGetValue("debug.dat", out _debugFile) && _debugFile != null)
        {
            IsAvailable = true;
            LoadDebugData();
        }
    }

    public bool IsAvailable { get; }

    private void LoadDebugData()
    {
        if (_debugFile == null) return;

        try
        {
            LoadMobyInstanceNames();
            LoadTieInstanceNames();
            LoadMobyPrototypeNames();
            LoadTiePrototypeNames();
            LoadShaderNames();
            LoadVolumeNames();
            LoadOldFamilyNames();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to load some debug data: {ex.Message}");
        }
    }

    private void LoadMobyInstanceNames()
    {
        if (!_isOld) return; // new engine gets instance names from gp_prius.dat, not debug.dat

        var section = _debugFile!.QuerySection(0x73C0);
        if (section.count == 0) return;

        _debugFile.sh.Seek(section.offset);
        var names = FileUtils.ReadStructureArray<DebugInstanceName>(_debugFile.sh, section.count);

        foreach (var item in names)
            _mobyInstanceNames.Add(string.IsNullOrEmpty(item.name) ? null : item.name);
    }

    private void LoadTieInstanceNames()
    {
        if (!_isOld) return; // new engine gets instance names from the zone's own file, not debug.dat

        var section = _debugFile!.QuerySection(0x72C0);
        if (section.count == 0) return;

        _debugFile.sh.Seek(section.offset);
        var names = FileUtils.ReadStructureArray<DebugInstanceName>(_debugFile.sh, section.count);

        foreach (var item in names)
            _tieInstanceNames.Add(string.IsNullOrEmpty(item.name) ? null : item.name);
    }

    // Old engine assets have no real TUID, so they're keyed by flat index. New engine assets
    // are keyed by the embedded tuid field.
    private void LoadMobyPrototypeNames()
    {
        var section = _debugFile!.QuerySection(0x9480);
        if (section.count == 0) return;

        for (uint i = 0; i < section.count; i++)
        {
            _debugFile.sh.Seek(section.offset + i * 0x10);
            var assetName = FileUtils.ReadStructure<DebugAssetName>(_debugFile.sh);

            if (string.IsNullOrEmpty(assetName.name)) continue;
            _mobyPrototypeNames[_isOld ? i : assetName.tuid] = assetName.name;
        }
    }

    private void LoadTiePrototypeNames()
    {
        var section = _debugFile!.QuerySection(0x9280);
        if (section.count == 0) return;

        for (uint i = 0; i < section.count; i++)
        {
            _debugFile.sh.Seek(section.offset + i * 0x10);
            var assetName = FileUtils.ReadStructure<DebugAssetName>(_debugFile.sh);

            if (string.IsNullOrEmpty(assetName.name)) continue;
            _tiePrototypeNames[_isOld ? i : assetName.tuid] = assetName.name;
        }
    }

    private void LoadShaderNames()
    {
        var section = _debugFile!.QuerySection(0x5D00);
        if (section.count == 0) return;

        for (uint i = 0; i < section.count; i++)
        {
            _debugFile.sh.Seek(section.offset + i * 0x30);
            var shaderName = FileUtils.ReadStructure<DebugShaderName>(_debugFile.sh);

            if (!string.IsNullOrEmpty(shaderName.shaderName))
                _shaderNames[shaderName.shaderTuid] = shaderName.shaderName;
        }
    }

    private void LoadVolumeNames()
    {
        var section = _debugFile!.QuerySection(0x7760);
        if (section.count == 0) return;

        // Must seek to section.offset - the stream may be left elsewhere by a previous read.
        _debugFile.sh.Seek(section.offset);
        var names = FileUtils.ReadStructureArray<DebugInstanceName>(_debugFile.sh, section.count);

        foreach (var item in names)
            _volumeNames.Add(string.IsNullOrEmpty(item.name) ? null : item.name);
    }

    private void LoadOldFamilyNames()
    {
        if (!_isOld) return;

        foreach (var (family, id) in OldFamilySections)
        {
            var section = _debugFile!.QuerySection(id);
            if (section.count == 0) continue;
            _debugFile.sh.Seek(section.offset);
            var names = FileUtils.ReadStructureArray<DebugInstanceName>(_debugFile.sh, section.count);
            _familyNames[family] = [.. names.Select(item => string.IsNullOrEmpty(item.name) ? null : item.name)];
        }
    }

    public string? GetMobyPrototypeName(ulong tuid) => _mobyPrototypeNames.TryGetValue(tuid, out var name) ? name : null;
    public string? GetTiePrototypeName(ulong tuid) => _tiePrototypeNames.TryGetValue(tuid, out var name) ? name : null;
    public string? GetShaderName(ulong tuid) => _shaderNames.TryGetValue(tuid, out var name) ? name : null;
    public string? GetMobyInstanceName(int index) =>
        (index >= 0 && index < _mobyInstanceNames.Count ? _mobyInstanceNames[index] : null) ?? InstanceNames.Name("moby", index);
    public string? GetTieInstanceName(int index) => index >= 0 && index < _tieInstanceNames.Count ? _tieInstanceNames[index] : null;
    public string? GetVolumeName(int index) =>
        (index >= 0 && index < _volumeNames.Count ? _volumeNames[index] : null) ?? InstanceNames.Name("volume", index);

    /// <summary>Old engine: the name of an instance of a family (path, clue, controller, area, pod, scent; also moby and
    /// volume) by its place in its gameplay.dat table, from debug.dat else instances.lua; null when neither names it.</summary>
    public string? GetOldInstanceName(string family, int index) => (DebugName(family, index)) ?? InstanceNames.Name(family, index);

    /// <summary>Old engine: debug.dat's own name for the instance, without instances.lua behind it.</summary>
    public string? DebugName(string family, int index)
    {
        var list = family switch { "moby" => _mobyInstanceNames, "volume" => _volumeNames, _ => _familyNames.GetValueOrDefault(family) };
        return list != null && index >= 0 && index < list.Count ? list[index] : null;
    }

    /// <summary>Old engine: how many instances debug.dat names in the family.</summary>
    public int DebugCount(string family) => family switch
    {
        "moby" => _mobyInstanceNames.Count, "volume" => _volumeNames.Count, _ => _familyNames.GetValueOrDefault(family)?.Count ?? 0,
    };

    public string GetSummary()
    {
        if (!IsAvailable)
            return "Debug data not available";

        return "Debug Data Summary:\n" +
               $"  Moby Prototypes: {_mobyPrototypeNames.Count}\n" +
               $"  Tie Prototypes: {_tiePrototypeNames.Count}\n" +
               $"  Shaders: {_shaderNames.Count}\n" +
               $"  Moby Instances: {_mobyInstanceNames.Count}\n" +
               $"  Tie Instances: {_tieInstanceNames.Count}";
    }

    [FileStructure(0x18)]
    public struct DebugInstanceName
    {
        [FileOffset(0x00)] public ulong tuid1;
        [FileOffset(0x08)] public ulong tuid2;
        [FileOffset(0x10), Reference] public string name;
        [FileOffset(0x14)] public uint unk;
    }

    [FileStructure(0x10)]
    public struct DebugAssetName
    {
        [FileOffset(0x00)] public ulong tuid;
        [FileOffset(0x08), Reference] public string name;
    }

    [FileStructure(0x30)]
    public struct DebugShaderName
    {
        [FileOffset(0x00)] public ulong shaderTuid;
        [FileOffset(0x08), Reference] public string shaderName;
        [FileOffset(0x10)] public uint albedoTuid;
        [FileOffset(0x14)] public uint normalTuid;
        [FileOffset(0x18)] public uint expensiveTuid;
        [FileOffset(0x1C)] public uint wthTuid;
        [FileOffset(0x20), Reference] public string albedoName;
        [FileOffset(0x24), Reference] public string normalName;
        [FileOffset(0x28), Reference] public string expensiveName;
        [FileOffset(0x2C), Reference] public string wthName;
    }
}
