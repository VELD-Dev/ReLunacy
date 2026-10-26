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
    private readonly bool _isOld;

    public DebugReader(FileManager fileManager)
    {
        ArgumentNullException.ThrowIfNull(fileManager);
        _isOld = fileManager.isOld;

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

    public string? GetMobyPrototypeName(ulong tuid) => _mobyPrototypeNames.TryGetValue(tuid, out var name) ? name : null;
    public string? GetTiePrototypeName(ulong tuid) => _tiePrototypeNames.TryGetValue(tuid, out var name) ? name : null;
    public string? GetShaderName(ulong tuid) => _shaderNames.TryGetValue(tuid, out var name) ? name : null;
    public string? GetMobyInstanceName(int index) => index >= 0 && index < _mobyInstanceNames.Count ? _mobyInstanceNames[index] : null;
    public string? GetTieInstanceName(int index) => index >= 0 && index < _tieInstanceNames.Count ? _tieInstanceNames[index] : null;
    public string? GetVolumeName(int index) => index >= 0 && index < _volumeNames.Count ? _volumeNames[index] : null;

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
