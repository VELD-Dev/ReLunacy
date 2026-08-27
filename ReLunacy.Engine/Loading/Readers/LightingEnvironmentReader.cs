using System.Numerics;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>Reads the old-engine analytic lighting environment (main.dat section 0x8b00). One 0x80
/// record per level: three colour vectors at 0x20/0x30/0x40 and two unit light directions at
/// 0x50/0x60 (0x00 is a small int header, 0x10 and 0x70 are unused). See
/// <see cref="Assets.Lighting.LightingEnvironment"/> for the field roles and the capture that pins
/// them. New engine is not handled.</summary>
public sealed class LightingEnvironmentReader
{
    public const uint ID = 0x8b00;

    private readonly FileManager _fileManager;

    public LightingEnvironmentReader(FileManager fileManager)
    {
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
    }

    public Assets.Lighting.LightingEnvironment? Read()
    {
        if (!_fileManager.isOld) return null;
        if (!_fileManager.igfiles.TryGetValue("main.dat", out IGFile? main) || main is null) return null;

        var section = main.QuerySection(ID);
        if (section.id != ID || section.length < 0x80) return null;

        Vector3 ReadVec3(uint offset)
        {
            main.sh.Seek(offset);
            return new Vector3(main.sh.ReadSingle(), main.sh.ReadSingle(), main.sh.ReadSingle());
        }

        uint b = (uint)section.offset;
        uint headerCount = main.sh.ReadUInt32(b);   // first word = light count (2 on both levels seen)

        var env = new Assets.Lighting.LightingEnvironment { Ambient = ReadVec3(b + 0x20) };

        // Ambient is colour[0] at 0x20; each directional light is colour[i] at 0x30/0x40 paired with
        // direction[i] at 0x50/0x60. Build from whichever direction slots are actually populated
        // rather than assuming two, so a level with fewer is handled (MaxLights is the record's
        // physical capacity, not an assumption that every level fills it).
        for (int i = 0; i < MaxLights; i++)
        {
            Vector3 dir = ReadVec3(b + 0x50 + (uint)i * 0x10);
            if (dir.LengthSquared() <= 1e-8f) continue;
            env.Lights.Add(new Assets.Lighting.DirectionalLight
            {
                Colour = ReadVec3(b + 0x30 + (uint)i * 0x10),
                Direction = Vector3.Normalize(dir),
            });
        }

        Console.WriteLine($"Lighting environment (0x8b00): headerCount={headerCount}, {env.Lights.Count} " +
                          $"directional light(s), ambient={env.Ambient}.");
        return env;
    }

    /// <summary>Directional lights the 0x80 record can physically hold (two direction slots). Not an
    /// assumption that a level uses both - see the reader.</summary>
    private const int MaxLights = 2;
}
