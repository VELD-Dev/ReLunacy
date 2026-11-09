using ReLunacy.Engine.Assets.Collision;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>
/// Reads a level's static collision (see <see cref="LevelCollision"/>).
///
/// Tools of Destruction and Quest for Booty: the level's raw collision.dat, loaded by the EBOOT from
/// "/app_home/%s/collision.dat". The new engine: section 0x2900 of each region's collision.dat, beside
/// its region.dat. That file also carries 0x8200 and 0x8300 on 126 of 152 regions, which are not
/// geometry (see docs/reverse/collision.md) and are not read.
///
/// A level or region without the file simply has no collision to show.
/// </summary>
public sealed class CollisionReader(FileManager fileManager)
{
    public const string FileName = "collision.dat";

    /// <summary>The new engine's collision section in a region's collision.dat.</summary>
    public const uint NewEngineSection = 0x2900;

    private readonly FileManager _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));

    /// <param name="regionName">The new-engine region whose collision.dat to read; ignored on the old
    /// engine, which has one per level.</param>
    public LevelCollision? Read(string? regionName)
    {
        string label = _fileManager.isOld ? FileName : $"{regionName}/{FileName}";
        try
        {
            LevelCollision? collision = _fileManager.isOld ? ReadOld() : regionName is null ? null : ReadNew(label);
            if (collision != null)
            {
                int tie = collision.Instances.Count(i => i.IsTie);
                int primitiveSets = collision.Shapes.Count(s => s is CollisionPrimitiveSet);
                Console.WriteLine(
                    $"Collision ({label}): {collision.Instances.Count} instance(s) ({tie} TIE, {collision.Instances.Count - tie} world), " +
                    $"{collision.Shapes.Count} shape(s) of which {primitiveSets} primitive set(s), " +
                    $"{collision.Shapes.OfType<CollisionMesh>().Sum(m => m.TriangleCount)} mesh triangle(s).");
            }
            return collision;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Collision: {label} could not be read ({e.Message}); skipped.");
            return null;
        }
    }

    private LevelCollision? ReadOld()
    {
        if (!_fileManager.rawfiles.TryGetValue(FileName, out Stream? stream))
            stream = _fileManager.LoadFile(FileName, true) as Stream;
        if (stream is null) return null;

        stream.Position = 0;
        var data = new byte[stream.Length];
        stream.ReadExactly(data);
        return LevelCollision.ReadOldEngine(data);
    }

    private LevelCollision? ReadNew(string key)
    {
        IGFile? file = _fileManager.GetOrLoadIGFile(key);
        if (file is null) return null;

        IGFile.SectionHeader section = file.QuerySection(NewEngineSection);
        if (section.id != NewEngineSection || section.count == 0) return null;

        file.sh.Seek(section.offset);
        return LevelCollision.ReadNewEngine(file.sh.ReadBytes(checked(section.count * section.length)));
    }
}
