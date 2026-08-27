using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Readers;

namespace ReLunacy.Engine.Games;

/// <summary>Where a discovered level's data actually lives.</summary>
public enum LevelSourceKind
{
    Folder,
    Psarc,
}

/// <summary>
/// A single level discovered on disk (or inside a PSARC), independent of whether it's currently
/// loaded. Owns its own load/unload lifecycle so the editor can juggle many discovered levels
/// while only holding one (or a few) fully loaded in memory at a time. <see cref="Data"/> is the
/// hook a future GLTF exporter would read from.
/// </summary>
public sealed class Level : IDisposable
{
    public string Name { get; }
    public GameDefinition? Game { get; }
    public LevelSourceKind SourceKind { get; }

    /// <summary>Folder path (SourceKind == Folder) or the .psarc file path (SourceKind == Psarc).</summary>
    public string SourcePath { get; }

    /// <summary>
    /// Old engine only: resolved location of this level's debug.dat, if one was found near it at
    /// scan time (see GameLibraryScanner.ResolveDebugDatPath) - it never ships inside main.dat/the
    /// level's own .psarc, so this has to be tracked separately.
    /// </summary>
    public string? DebugDatPath { get; }

    public FileManager? FileManager { get; private set; }
    public LevelData? Data { get; private set; }
    public bool IsLoaded => Data != null;

    public Level(string name, string sourcePath, LevelSourceKind sourceKind, GameDefinition? game = null, string? debugDatPath = null)
    {
        Name = name;
        SourcePath = sourcePath;
        SourceKind = sourceKind;
        Game = game;
        DebugDatPath = debugDatPath;
    }

    public LevelData Load(Action<string, float>? progressCallback = null)
    {
        if (Data != null)
            return Data;

        FileManager = new FileManager();
        if (SourceKind == LevelSourceKind.Psarc)
            FileManager.LoadFromPsarcFile(SourcePath);
        else
            FileManager.LoadFolder(SourcePath);

        if (FileManager.isOld && DebugDatPath != null)
            FileManager.LoadExternalDebugDat(DebugDatPath);

        var reader = new LevelReader(FileManager);
        Data = reader.LoadLevel(progressCallback);
        return Data;
    }

    public void Unload()
    {
        FileManager?.Dispose();
        Data = null;
        FileManager = null;
    }

    public void Dispose()
    {
        Unload();
        GC.SuppressFinalize(this);
    }
}
