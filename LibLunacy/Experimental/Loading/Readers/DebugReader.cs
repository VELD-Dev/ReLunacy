using LibLunacy.Legacy;

namespace LibLunacy.Experimental.Loading.Readers;

/// <summary>
/// Reads debug information from debug.dat file
/// Contains human-readable names for assets and instances
/// </summary>
public sealed class DebugReader
{
    private readonly IGFile? _debugFile;
    private readonly Dictionary<ulong, string> _mobyPrototypeNames = [];
    private readonly Dictionary<ulong, string> _tiePrototypeNames = [];
    private readonly Dictionary<ulong, string> _shaderNames = [];
    private readonly Dictionary<(ulong, ulong), string> _mobyInstanceNames = [];
    private readonly Dictionary<(ulong, ulong), string> _tieInstanceNames = [];
    private readonly List<string> _volumeNames = [];
    private readonly bool _isAvailable;

    public DebugReader(FileManager fileManager)
    {
        if (fileManager == null)
            throw new ArgumentNullException(nameof(fileManager));

        // Try to load debug.dat if it exists
        if (fileManager.igfiles.TryGetValue("debug.dat", out _debugFile) && _debugFile != null)
        {
            _isAvailable = true;
            LoadDebugData();
        }
        else
        {
            _isAvailable = false;
        }
    }

    /// <summary>
    /// Whether debug data is available
    /// </summary>
    public bool IsAvailable => _isAvailable;

    /// <summary>
    /// Loads all debug data from the file
    /// </summary>
    private void LoadDebugData()
    {
        if (_debugFile == null)
            return;

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

    /// <summary>
    /// Loads moby instance names (section 0x73C0)
    /// </summary>
    private void LoadMobyInstanceNames()
    {
        var section = _debugFile!.QuerySection(0x73C0);
        if (section.count == 0)
            return;

        _debugFile.sh.Seek(section.offset);
        var names = FileUtils.ReadStructureArray<DebugInstanceName>(_debugFile.sh, section.count);

        foreach (var item in names)
        {
            if (!string.IsNullOrEmpty(item.name))
            {
                _mobyInstanceNames[(item.tuid1, item.tuid2)] = item.name;
            }
        }
    }

    /// <summary>
    /// Loads tie instance names (section 0x72C0)
    /// </summary>
    private void LoadTieInstanceNames()
    {
        var section = _debugFile!.QuerySection(0x72C0);
        if (section.count == 0)
            return;

        _debugFile.sh.Seek(section.offset);
        var names = FileUtils.ReadStructureArray<DebugInstanceName>(_debugFile.sh, section.count);

        foreach (var item in names)
        {
            if (!string.IsNullOrEmpty(item.name))
            {
                _tieInstanceNames[(item.tuid1, item.tuid2)] = item.name;
            }
        }
    }

    /// <summary>
    /// Loads moby prototype names (section 0x9480)
    /// </summary>
    private void LoadMobyPrototypeNames()
    {
        var section = _debugFile!.QuerySection(0x9480);
        if (section.count == 0)
            return;

        for (uint i = 0; i < section.count; i++)
        {
            _debugFile.sh.Seek(section.offset + i * 0x10);
            var assetName = FileUtils.ReadStructure<DebugAssetName>(_debugFile.sh);

            if (!string.IsNullOrEmpty(assetName.name))
            {
                _mobyPrototypeNames[assetName.tuid] = assetName.name;
            }
        }
    }

    /// <summary>
    /// Loads tie prototype names (section 0x9280)
    /// </summary>
    private void LoadTiePrototypeNames()
    {
        var section = _debugFile!.QuerySection(0x9280);
        if (section.count == 0)
            return;

        for (uint i = 0; i < section.count; i++)
        {
            _debugFile.sh.Seek(section.offset + i * 0x10);
            var assetName = FileUtils.ReadStructure<DebugAssetName>(_debugFile.sh);

            if (!string.IsNullOrEmpty(assetName.name))
            {
                _tiePrototypeNames[assetName.tuid] = assetName.name;
            }
        }
    }

    /// <summary>
    /// Loads shader names (section 0x5D00)
    /// </summary>
    private void LoadShaderNames()
    {
        var section = _debugFile!.QuerySection(0x5D00);
        if (section.count == 0)
            return;

        for (uint i = 0; i < section.count; i++)
        {
            _debugFile.sh.Seek(section.offset + i * 0x30);
            var shaderName = FileUtils.ReadStructure<DebugShaderName>(_debugFile.sh);

            if (!string.IsNullOrEmpty(shaderName.shaderName))
            {
                _shaderNames[shaderName.shaderTuid] = shaderName.shaderName;
            }
        }
    }

    private void LoadVolumeNames()
    {
        var section = _debugFile!.QuerySection(0x7760);
        if (section.count == 0)
            return;

        for(int i = 0; i < section.count; i++)
        {
            var volumeName = _debugFile.sh.ReadString();
            if (!string.IsNullOrEmpty(volumeName))
            {
                _volumeNames[i] = volumeName;
            }
        }
    }

    /// <summary>
    /// Gets the name for a moby prototype
    /// </summary>
    public string? GetMobyPrototypeName(ulong tuid)
    {
        return _mobyPrototypeNames.TryGetValue(tuid, out var name) ? name : null;
    }

    /// <summary>
    /// Gets the name for a tie prototype
    /// </summary>
    public string? GetTiePrototypeName(ulong tuid)
    {
        return _tiePrototypeNames.TryGetValue(tuid, out var name) ? name : null;
    }

    /// <summary>
    /// Gets the name for a shader
    /// </summary>
    public string? GetShaderName(ulong tuid)
    {
        return _shaderNames.TryGetValue(tuid, out var name) ? name : null;
    }

    /// <summary>
    /// Gets the name for a moby instance
    /// </summary>
    public string? GetMobyInstanceName(ulong tuid1, ulong tuid2)
    {
        return _mobyInstanceNames.TryGetValue((tuid1, tuid2), out var name) ? name : null;
    }

    /// <summary>
    /// Gets the name for a tie instance
    /// </summary>
    public string? GetTieInstanceName(ulong tuid1, ulong tuid2)
    {
        return _tieInstanceNames.TryGetValue((tuid1, tuid2), out var name) ? name : null;
    }

    /// <summary>
    /// Gets summary of loaded debug data
    /// </summary>
    public string GetSummary()
    {
        if (!_isAvailable)
            return "Debug data not available";

        return $"Debug Data Summary:\n" +
               $"  Moby Prototypes: {_mobyPrototypeNames.Count}\n" +
               $"  Tie Prototypes: {_tiePrototypeNames.Count}\n" +
               $"  Shaders: {_shaderNames.Count}\n" +
               $"  Moby Instances: {_mobyInstanceNames.Count}\n" +
               $"  Tie Instances: {_tieInstanceNames.Count}";
    }

    #region Debug Structures

    [FileStructure(0x18)]
    public struct DebugInstanceName
    {
        [FileOffset(0x00)] public ulong tuid1;
        [FileOffset(0x08)] public ulong tuid2;
        [FileOffset(0x10)] [Reference] public string name;
        [FileOffset(0x14)] public uint unk;
    }

    [FileStructure(0x10)]
    public struct DebugAssetName
    {
        [FileOffset(0x00)] public ulong tuid;
        [FileOffset(0x08)] [Reference] public string name;
    }

    [FileStructure(0x30)]
    public struct DebugShaderName
    {
        [FileOffset(0x00)] public ulong shaderTuid;
        [FileOffset(0x08)] [Reference] public string shaderName;
        [FileOffset(0x10)] public uint albedoTuid;
        [FileOffset(0x14)] public uint normalTuid;
        [FileOffset(0x18)] public uint expensiveTuid;
        [FileOffset(0x1C)] public uint wthTuid;
        [FileOffset(0x20)] [Reference] public string albedoName;
        [FileOffset(0x24)] [Reference] public string normalName;
        [FileOffset(0x28)] [Reference] public string expensiveName;
        [FileOffset(0x2C)] [Reference] public string wthName;
    }

    #endregion
}
