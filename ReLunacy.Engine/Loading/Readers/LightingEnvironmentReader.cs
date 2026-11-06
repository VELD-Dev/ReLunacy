using System.Numerics;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>Reads the old-engine lighting environment rooted at main.dat section 0x8B00.
///
/// 0x8B00 is one 0x80-byte root record. +0x00 is format/version (2 on the confirmed fixtures),
/// +0x04 points to 0x8C00 and +0x08 is its volume count. The root also carries global ambient,
/// two directional colours and two directions.
///
/// 0x8C00 is count*0x80. Each record can point into 0x8D00; exponents at +0x14..+0x16 give exact
/// grid dimensions. The retail consumer and position sampler are mirrored by OldEngineLightingSampler.
/// This reader preserves both raw packed colour words and their native byte/128 float conversion.</summary>
public sealed class LightingEnvironmentReader
{
    public const uint ID = 0x8B00;
    public const uint SpatialVolumesID = 0x8C00;
    public const uint SpatialVoxelDataID = 0x8D00;

    private const uint RootStride = 0x80;
    private const uint VolumeStride = 0x80;
    private const int MaxLights = 2;

    private readonly FileManager _fileManager;

    public LightingEnvironmentReader(FileManager fileManager)
    {
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
    }

    /// <summary>Editor ambient for the new-engine presentation fallback. Not a file value.</summary>
    public static readonly Vector3 NewEnginePresentationAmbient = new(0.42f, 0.43f, 0.46f);

    /// <summary>Editor sun colour for the new-engine presentation fallback. Not a file value.</summary>
    public static readonly Vector3 NewEnginePresentationSun = new(0.78f, 0.76f, 0.70f);

    /// <summary>
    /// New-engine editor PRESENTATION lighting, not a decoded retail source.
    ///
    /// Why it exists: a new-engine level has no old-engine 0x8B00 root in main.dat, so the renderer
    /// had no environment at all and lit everything without a lightmap - every moby, every UFrag -
    /// with a flat 0.15 ambient, which is why new-engine levels looked far too dark.
    ///
    /// What is sourced: region.dat has its own 0x8B00 (0xC0 bytes, different layout). Measured on
    /// acid_refinery_pre, plasma_harvester_pre and lobby: +0x00..+0x2F are zero, +0x30 and +0x40 hold
    /// the same unit vector and +0x50 its exact negation, e.g. (-0.773, -0.499, 0.392) /
    /// (0.773, 0.499, -0.392) - a sun direction and the direction TOWARDS it. +0x50 is used as the
    /// light direction (it points up on all three levels). The zero colour slots are not taken at face
    /// value: nothing lit by them would be visible, and where the game keeps its sun colour is OPEN.
    ///
    /// What is not: <see cref="NewEnginePresentationAmbient"/> and <see cref="NewEnginePresentationSun"/>
    /// are editor values, editable in the Level Data frame, and flagged by IsPresentationFallback.
    /// </summary>
    public static Assets.Lighting.LightingEnvironment? ReadNewEngineSunPresentation(FileManager fileManager, string? regionName)
    {
        if (fileManager.isOld || string.IsNullOrEmpty(regionName)) return null;
        string key = $"{regionName}/region.dat";
        if (!fileManager.igfiles.TryGetValue(key, out IGFile? region) || region is null) return null;

        if (ReadRegionSun(region, key) is { } decoded)
            return decoded;

        var section = region.QuerySection(ID);
        if (section.id != ID || section.count == 0 || section.length < 0x60) return null;

        // A Crack in Time's 0xA0 record is the old-engine 0x8B00 root shifted by 0x20: ambient
        // +0x40, light colours +0x50/+0x60, unit directions +0x70/+0x80 (old engine: +0x20,
        // +0x30/+0x40, +0x50/+0x60). Measured on six ACiT levels: both directions are unit vectors on
        // every one, the colours vary per level (axiom_city sun (1.8, 1.63, 0.87), gimlick_valley
        // (5.56, 2.9, 1.41)) and levels without their own lighting carry identical defaults (ambient
        // 0.18, sun 1.33, fill 0.67). That is a decoded source, not the presentation fallback.
        if (section.length == 0xA0)
            return ReadNewEngineShiftedRoot(region, section.offset, key);

        Vector3 towardSun = ReadVec3(region, section.offset + 0x50);
        if (!float.IsFinite(towardSun.X) || towardSun.LengthSquared() < 0.5f || towardSun.LengthSquared() > 1.5f)
            return null;

        var env = new Assets.Lighting.LightingEnvironment
        {
            Version = 0,
            Ambient = NewEnginePresentationAmbient,
            IsPresentationFallback = true,
            Bloom = ReadBloom(region),
        };
        env.Lights.Add(new Assets.Lighting.DirectionalLight
        {
            Colour = NewEnginePresentationSun,
            Direction = Vector3.Normalize(towardSun),
        });
        Console.WriteLine($"New-engine lighting: no decoded environment; presentation sun from {key} 0x8B00 +0x50 = {towardSun}.");
        return env;
    }

    /// <summary>
    /// All 4 One / QForce: region.dat 0x1DB03 (0x80 bytes) is the level's sun.
    ///   +0x10  unit direction towards the sun
    ///   +0x20  sun colour, bytes R,G,B (+0x23 unused), +0x24 intensity
    ///   +0x60  ambient colour, same encoding, +0x64 intensity
    /// A colour is (byte / 256)^2 * intensity. That is exact against the retail Tie fragment shaders,
    /// which compile the sun in as constants: acid_refinery_pre (250, 235, 146) x 1.0 gives
    /// (0.9537, 0.8427, 0.3253) and jungle_ruins_pvp (105, 162, 195) x 2.5 gives (0.4206, 1.0011,
    /// 1.4505) - the shaders' values to four digits - and in both the direction is the shaders'
    /// dot3 constant. Present on every QForce and All 4 One level sampled; absent on A Crack in Time,
    /// which carries its lights in the 0xA0 0x8B00 root instead. Levels without a sun of their own
    /// ship (0, 0, -1) with white x 1.
    /// </summary>
    private static Assets.Lighting.LightingEnvironment? ReadRegionSun(IGFile region, string key)
    {
        var section = region.QuerySection(0x1DB03);
        if (section.id != 0x1DB03 || section.count == 0) return null;
        uint b = section.offset;
        if ((long)b + 0x68 > region.sh.BaseStream.Length) return null;

        Vector3 direction = ReadVec3(region, b + 0x10);
        if (!float.IsFinite(direction.X) || direction.LengthSquared() < 0.5f || direction.LengthSquared() > 1.5f)
            return null;

        Vector3 Colour(uint offset)
        {
            region.sh.Seek(offset);
            byte r = region.sh.ReadByte(), g = region.sh.ReadByte(), bl = region.sh.ReadByte();
            region.sh.Seek(offset + 4);
            float intensity = region.sh.ReadSingle();
            static float C(byte v) => (v / 256f) * (v / 256f);
            return new Vector3(C(r), C(g), C(bl)) * intensity;
        }

        var env = new Assets.Lighting.LightingEnvironment
        {
            Version = 0,
            Ambient = Colour(b + 0x60),
            ReflectionOcclusion = ReadReflectionOcclusion(region),
            Bloom = ReadBloom(region),
        };
        env.Lights.Add(new Assets.Lighting.DirectionalLight
        {
            Colour = Colour(b + 0x20),
            Direction = Vector3.Normalize(direction),
        });
        Console.WriteLine($"New-engine lighting: {key} 0x1DB03 sun {env.Lights[0].Colour} towards {env.Lights[0].Direction}, ambient {env.Ambient}.");
        return env;
    }

    /// <summary>
    /// region.dat 0x8B00 (0xC0 bytes) +0x90 / +0x94: the floor b and the full-strength diffuse luminance
    /// a of the shaders' reflection occlusion, reflection *= saturate(b + (1 - b) * lum(light) / a).
    /// The formula is PROVEN: the QForce EBOOT builds fragment-constant group 1 = (0.2126 k, 0.7152 k,
    /// 0.0722 k, b) with k = (1 - b) / a (0x0079DE48) and patches it into every shader. The source is
    /// STRONG: the region loader copies +0x90/+0x94 into the lighting block 0x0109BF00 +0xB8/+0xBC
    /// (0x0070AC68), and (0.01, 0.6) - the value on all 15 QForce levels, not the engine default (0.9,
    /// 0.05) - gives (0.35079, 1.18008, 0.11913, 0.01), the vector the RPCS3 cache holds in 98 % of the
    /// reflecting pipelines, while the (0.04, 2.25) of 53 All 4 One and 18 Into the Nexus regions gives
    /// the corpus's other vector (x 0.4267, bias 0.04); the hop from that block to the per-view settings
    /// the producer reads is not traced. A Crack in Time (0xA0 record, no 0x1DB03) never reaches this.
    /// Null when the record is absent or implausible.
    /// </summary>
    /// <summary>region.dat 0x34000 +0x80..+0x90: bloom extract scale and exponent, level weight, history
    /// blend, intensity (<see cref="Assets.Lighting.BloomParameters"/>). Null when absent or implausible.</summary>
    private static Assets.Lighting.BloomParameters? ReadBloom(IGFile region)
    {
        var section = region.QuerySection(0x34000);
        if (section.id != 0x34000 || section.count == 0 || section.length < 0x94) return null;
        region.sh.Seek(section.offset + 0x80);
        float k = region.sh.ReadSingle(), e = region.sh.ReadSingle(), s = region.sh.ReadSingle();
        float h = region.sh.ReadSingle(), intensity = region.sh.ReadSingle();
        if (!float.IsFinite(k) || !float.IsFinite(e) || !float.IsFinite(s) || !float.IsFinite(h) || !float.IsFinite(intensity))
            return null;
        if (k < 0f || e <= 0f || h < 0f || h > 1f || intensity < 0f || intensity > 16f)
            return null;
        return new Assets.Lighting.BloomParameters(k, e, s, h, intensity);
    }

    private static Vector2? ReadReflectionOcclusion(IGFile region)
    {
        var section = region.QuerySection(ID);
        if (section.id != ID || section.count == 0 || section.length < 0x98) return null;
        region.sh.Seek(section.offset + 0x90);
        float floor = region.sh.ReadSingle();
        float fullLuminance = region.sh.ReadSingle();
        if (!float.IsFinite(floor) || !float.IsFinite(fullLuminance) || floor < 0f || floor >= 1f || fullLuminance <= 0f)
            return null;
        return new Vector2(floor, fullLuminance);
    }

    /// <summary>
    /// region.dat 0x8100 (0x30 bytes) +0x08 / +0x04: the game camera's near and far clip planes. STRONG: the
    /// camera setup (0x0062E82C, 0x0062E914, 0x0062EAC8) takes its near plane from the getter 0x00630050
    /// (the 0x8100 copy at 0x0109F0A0, +0x08) and its far plane as min(+0x04 via 0x00630060, a caller
    /// limit); and the depth-linearisation group 18 in the RPCS3 cache holds 1/near = 16.667, 10 and 5 -
    /// the 0.06, 0.1 and 0.2 these records carry (QForce 0.06 on 12 of 17 regions, A4O 0.1-0.2, ITN
    /// 0.06-0.4). A Crack in Time's 0x100-byte record has another layout and is not read.
    /// </summary>
    /// <summary>
    /// region.dat 0x34004 (0x800 bytes): the per-draw fog table, 512 big-endian ARGB words. PROVEN: the
    /// fog constant K of the QForce shaders is pow(lerp(entry, t x 512), 2.2) per channel, A = amount
    /// (0x00718030 -> 0x00717DC0, pow 2.2 at 0x00868614), t = distance(object, camera) / far plane; the
    /// RPCS3 cache holds acid_refinery's entries 96 and 128 exactly ((70/255)^2.2 = 0.05819, 0.06003).
    /// The table the game samples is the per-view copy (+0x170) of the active atmosphere: the region's,
    /// unless the camera stands in a zone volume (0x34001, not followed). Null when absent or all zero.
    /// </summary>
    public static uint[]? ReadNewEngineFogTable(FileManager fileManager, string? regionName) =>
        ReadRegionTable(fileManager, regionName, 0x34004);

    /// <summary>region.dat 0x34003: the atmosphere pass's second table (see LightingEnvironment.InscatterTable).</summary>
    public static uint[]? ReadNewEngineInscatterTable(FileManager fileManager, string? regionName) =>
        ReadRegionTable(fileManager, regionName, 0x34003);

    /// <summary>
    /// region.dat 0x34000 +0x10..+0x30: scattering direction, colour, intensity, turbidity and phase g.
    /// PROVEN by value: with the lobby's record the formulas of 0x007181B8 give the in-scatter colour
    /// (0.002384, 0.017349, 0.028178) and extinction (0.000149, 0.000192, 0.000273) the RPCS3 cache holds in
    /// fragment shader 0x8E. Null when absent, of another version or implausible.
    /// </summary>
    public static Assets.Lighting.NewEngineAtmosphere? ReadNewEngineAtmosphere(FileManager fileManager, string? regionName)
    {
        if (fileManager.isOld || string.IsNullOrEmpty(regionName)) return null;
        if (!fileManager.igfiles.TryGetValue($"{regionName}/region.dat", out IGFile? region) || region is null) return null;
        var section = region.QuerySection(0x34000);
        if (section.id != 0x34000 || section.count == 0 || section.length < 0x34) return null;
        region.sh.Seek(section.offset);
        if (region.sh.ReadUInt32() != 5) return null;
        Vector3 direction = ReadVec3(region, section.offset + 0x10);
        Vector3 colour = ReadVec3(region, section.offset + 0x1C);
        region.sh.Seek(section.offset + 0x28);
        float intensity = region.sh.ReadSingle(), turbidity = region.sh.ReadSingle(), g = region.sh.ReadSingle();
        if (!float.IsFinite(direction.X) || !float.IsFinite(colour.X) || !float.IsFinite(intensity) ||
            !float.IsFinite(turbidity) || !float.IsFinite(g) || g >= 1.001f)
            return null;
        return new Assets.Lighting.NewEngineAtmosphere(direction, colour, intensity, turbidity, g);
    }

    private static uint[]? ReadRegionTable(FileManager fileManager, string? regionName, uint id)
    {
        if (fileManager.isOld || string.IsNullOrEmpty(regionName)) return null;
        if (!fileManager.igfiles.TryGetValue($"{regionName}/region.dat", out IGFile? region) || region is null) return null;
        var section = region.QuerySection(id);
        if (section.id != id || section.count == 0) return null;
        uint length = section.count <= 1 ? section.length : section.count * section.length;
        if (length < 0x800 || section.offset + 0x800L > region.sh.BaseStream.Length) return null;
        region.sh.Seek(section.offset);
        byte[] raw = region.sh.ReadBytes(0x800);
        var table = new uint[512];
        bool any = false;
        for (int i = 0; i < table.Length; i++)
        {
            table[i] = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(i * 4, 4));
            any |= table[i] != 0;
        }
        return any ? table : null;
    }

    public static Vector2? ReadNewEngineCameraClip(FileManager fileManager, string? regionName)
    {
        if (fileManager.isOld || string.IsNullOrEmpty(regionName)) return null;
        if (!fileManager.igfiles.TryGetValue($"{regionName}/region.dat", out IGFile? region) || region is null) return null;
        var section = region.QuerySection(0x8100);
        if (section.id != 0x8100 || section.count == 0 || section.length < 0x30) return null;
        region.sh.Seek(section.offset + 0x04);
        float far = region.sh.ReadSingle();
        float near = region.sh.ReadSingle();
        if (!float.IsFinite(near) || !float.IsFinite(far) || near <= 0f || far <= near) return null;
        return new Vector2(near, far);
    }

    private static Assets.Lighting.LightingEnvironment? ReadNewEngineShiftedRoot(IGFile region, uint b, string key)
    {
        var env = new Assets.Lighting.LightingEnvironment
        {
            Version = 0,
            Ambient = ReadVec3(region, b + 0x40),
            Bloom = ReadBloom(region),
        };
        for (int i = 0; i < MaxLights; i++)
        {
            Vector3 dir = ReadVec3(region, b + 0x70 + (uint)i * 0x10);
            if (!float.IsFinite(dir.X) || dir.LengthSquared() <= 1e-8f) continue;
            env.Lights.Add(new Assets.Lighting.DirectionalLight
            {
                Colour = ReadVec3(region, b + 0x50 + (uint)i * 0x10),
                Direction = Vector3.Normalize(dir),
            });
        }
        Console.WriteLine($"New-engine lighting: {key} 0x8B00 (0xA0 layout): ambient {env.Ambient}, {env.Lights.Count} directional light(s).");
        return env;
    }

    public Assets.Lighting.LightingEnvironment? Read()
    {
        if (!_fileManager.isOld) return null;
        if (!_fileManager.igfiles.TryGetValue("main.dat", out IGFile? main) || main is null) return null;

        var rootSection = main.QuerySection(ID);
        if (rootSection.id != ID || rootSection.count == 0) return null;
        if (rootSection.length != RootStride)
        {
            Console.WriteLine($"Lighting environment 0x{ID:X4}: unsupported root stride 0x{rootSection.length:X}, expected 0x{RootStride:X}; skipped.");
            return null;
        }

        if (rootSection.count != 1)
            Console.WriteLine($"Lighting environment 0x{ID:X4}: expected one root record, found {rootSection.count}; reading the first.");

        uint b = rootSection.offset;
        uint version = main.sh.ReadUInt32(b + 0x00);
        uint volumesPointer = main.sh.ReadUInt32(b + 0x04);
        uint declaredVolumeCount = main.sh.ReadUInt32(b + 0x08);

        var env = new Assets.Lighting.LightingEnvironment
        {
            Version = version,
            SpatialVolumesPointer = volumesPointer,
            Ambient = ReadVec3(main, b + 0x20),
        };

        for (int i = 0; i < MaxLights; i++)
        {
            Vector3 dir = ReadVec3(main, b + 0x50 + (uint)i * 0x10);
            if (dir.LengthSquared() <= 1e-8f) continue;
            env.Lights.Add(new Assets.Lighting.DirectionalLight
            {
                Colour = ReadVec3(main, b + 0x30 + (uint)i * 0x10),
                Direction = Vector3.Normalize(dir),
            });
        }

        if (version != 2)
        {
            Console.WriteLine($"Lighting environment 0x{ID:X4}: version/header is {version}, not confirmed value 2. Global fields retained; spatial parsing disabled for this unknown revision.");
            return env;
        }

        ReadSpatialVolumes(main, env, declaredVolumeCount);

        int voxelVolumes = env.SpatialVolumes.Count(v => v.HasVoxelData);
        long voxelBytes = env.SpatialVolumes.Sum(v => (long)v.VoxelCount);
        Console.WriteLine($"Lighting environment (0x{ID:X4}): version={version}, {env.Lights.Count} global directional light(s), ambient={env.Ambient}, spatial={env.SpatialVolumes.Count} volumes ({voxelVolumes} voxel grids, 0x{voxelBytes:X} bytes).");
        return env;
    }

    private static void ReadSpatialVolumes(IGFile main, Assets.Lighting.LightingEnvironment env, uint declaredVolumeCount)
    {
        var volumeSection = main.QuerySection(SpatialVolumesID);
        if (volumeSection.id != SpatialVolumesID || volumeSection.count == 0)
        {
            if (env.SpatialVolumesPointer != 0 || declaredVolumeCount != 0)
                Console.WriteLine("Lighting environment: 0x8B00 declares spatial volumes but 0x8C00 is absent.");
            return;
        }

        if (volumeSection.length != VolumeStride)
        {
            Console.WriteLine($"Lighting environment: unsupported 0x{SpatialVolumesID:X4} stride 0x{volumeSection.length:X}, expected 0x{VolumeStride:X}; spatial data skipped.");
            return;
        }

        if (env.SpatialVolumesPointer != volumeSection.offset)
        {
            Console.WriteLine($"Lighting environment: 0x8B00 +0x04 points to 0x{env.SpatialVolumesPointer:X}, but 0x{SpatialVolumesID:X4} starts at 0x{volumeSection.offset:X}; spatial data skipped.");
            return;
        }

        if (declaredVolumeCount != volumeSection.count)
            Console.WriteLine($"Lighting environment: 0x8B00 declares {declaredVolumeCount} spatial volumes, but 0x{SpatialVolumesID:X4}.count is {volumeSection.count}; using the smaller validated count.");

        uint volumeCount = Math.Min(declaredVolumeCount, volumeSection.count);
        var voxelSection = main.QuerySection(SpatialVoxelDataID);
        bool hasVoxelSection = voxelSection.id == SpatialVoxelDataID && voxelSection.count == 1 && voxelSection.length != 0;
        if (voxelSection.id == SpatialVoxelDataID && voxelSection.count != 0 && voxelSection.count != 1)
            Console.WriteLine($"Lighting environment: 0x{SpatialVoxelDataID:X4} uses unsupported count={voxelSection.count}; voxel data will not be interpreted.");

        long voxelStart = hasVoxelSection ? voxelSection.offset : 0;
        long voxelEnd = hasVoxelSection ? checked((long)voxelSection.offset + voxelSection.length) : 0;
        var occupiedVoxelRanges = new List<(int Index, long Start, long End)>();

        for (uint i = 0; i < volumeCount; i++)
        {
            uint r = checked(volumeSection.offset + i * VolumeStride);
            uint field00 = main.sh.ReadUInt32(r + 0x00);

            uint colour0Bits = main.sh.ReadUInt32(r + 0x04);
            uint colour1Bits = main.sh.ReadUInt32(r + 0x08);
            uint colour2Bits = main.sh.ReadUInt32(r + 0x0C);
            Vector3 colour0 = DecodeNativeLightingRgb8(colour0Bits);
            Vector3 colour1 = DecodeNativeLightingRgb8(colour1Bits);
            Vector3 colour2 = DecodeNativeLightingRgb8(colour2Bits);

            uint field10 = main.sh.ReadUInt32(r + 0x10);

            main.sh.Seek(r + 0x14);
            byte expX = main.sh.ReadByte();
            byte expY = main.sh.ReadByte();
            byte expZ = main.sh.ReadByte();
            byte gridFlags = main.sh.ReadByte();

            uint voxelPointer = main.sh.ReadUInt32(r + 0x18);
            main.sh.Seek(r + 0x1C);
            float field1C = main.sh.ReadSingle();
            Vector3 direction0 = ReadVec3(main, r + 0x20);
            Vector3 direction1 = ReadVec3(main, r + 0x30);

            Vector4 axisXRow = ReadVec4(main, r + 0x40);
            Vector4 axisYRow = ReadVec4(main, r + 0x50);
            Vector4 axisZRow = ReadVec4(main, r + 0x60);
            Vector4 centreRow = ReadVec4(main, r + 0x70);

            byte[] voxels = [];
            if (voxelPointer != 0)
            {
                if (!hasVoxelSection)
                    throw new InvalidDataException($"Lighting volume {i} points to voxel data at 0x{voxelPointer:X}, but 0x{SpatialVoxelDataID:X4} is unavailable or unsupported.");

                int voxelCount = GridCellCount(i, expX, expY, expZ);
                long start = voxelPointer;
                long end = checked(start + voxelCount);
                if (start < voxelStart || end > voxelEnd)
                    throw new InvalidDataException($"Lighting volume {i}: voxel range [0x{start:X},0x{end:X}) escapes 0x{SpatialVoxelDataID:X4} [0x{voxelStart:X},0x{voxelEnd:X}).");

                voxels = main.sh.ReadFromOffset(voxelCount, checked((uint)start));
                if (voxels.Length != voxelCount)
                    throw new EndOfStreamException($"Lighting volume {i}: read {voxels.Length} voxel bytes, expected {voxelCount}.");
                occupiedVoxelRanges.Add(((int)i, start, end));
            }
            else if (expX != 0 || expY != 0 || expZ != 0)
            {
                Console.WriteLine($"Lighting volume {i}: zero voxel pointer with non-zero exponents ({expX},{expY},{expZ}); retaining as a non-voxel volume.");
            }

            env.SpatialVolumes.Add(new Assets.Lighting.SpatialLightingVolume
            {
                Index = (int)i,
                Field00Bits = field00,
                Colour0Bits = colour0Bits,
                Colour1Bits = colour1Bits,
                Colour2Bits = colour2Bits,
                Colour0 = colour0,
                Colour1 = colour1,
                Colour2 = colour2,
                Field10Bits = field10,
                GridExponentX = expX,
                GridExponentY = expY,
                GridExponentZ = expZ,
                GridFlags = gridFlags,
                VoxelPointer = voxelPointer,
                Field1C = field1C,
                Direction0 = direction0,
                Direction1 = direction1,
                AxisX = new Vector3(axisXRow.X, axisXRow.Y, axisXRow.Z),
                AxisY = new Vector3(axisYRow.X, axisYRow.Y, axisYRow.Z),
                AxisZ = new Vector3(axisZRow.X, axisZRow.Y, axisZRow.Z),
                AxisScale = new Vector3(axisXRow.W, axisYRow.W, axisZRow.W),
                Center = new Vector3(centreRow.X, centreRow.Y, centreRow.Z),
                CenterW = centreRow.W,
                Voxels = voxels,
            });
        }

        ValidateVoxelPacking(voxelStart, voxelEnd, occupiedVoxelRanges);
    }

    private static int GridCellCount(uint index, byte expX, byte expY, byte expZ)
    {
        if (expX >= 31 || expY >= 31 || expZ >= 31)
            throw new InvalidDataException($"Lighting volume {index}: invalid voxel exponents ({expX},{expY},{expZ}).");

        long x = 1L << expX;
        long y = 1L << expY;
        long z = 1L << expZ;
        long cells = checked(x * y * z);
        if (cells > int.MaxValue)
            throw new InvalidDataException($"Lighting volume {index}: voxel grid contains {cells} cells, too large to address.");
        return (int)cells;
    }

    private static void ValidateVoxelPacking(long voxelSectionStart, long voxelSectionEnd, List<(int Index, long Start, long End)> ranges)
    {
        if (ranges.Count == 0) return;
        ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
        for (int i = 1; i < ranges.Count; i++)
        {
            if (ranges[i].Start < ranges[i - 1].End)
                throw new InvalidDataException($"Lighting voxel ranges overlap: volume {ranges[i - 1].Index} ends at 0x{ranges[i - 1].End:X}, volume {ranges[i].Index} starts at 0x{ranges[i].Start:X}.");
        }

        int gaps = 0;
        for (int i = 1; i < ranges.Count; i++) if (ranges[i - 1].End != ranges[i].Start) gaps++;
        if (ranges[0].Start != voxelSectionStart || ranges[^1].End != voxelSectionEnd || gaps != 0)
            Console.WriteLine($"Lighting voxel packing: {ranges.Count} grids, first=0x{ranges[0].Start:X}, lastEnd=0x{ranges[^1].End:X}, section=[0x{voxelSectionStart:X},0x{voxelSectionEnd:X}), gaps={gaps}.");
    }

    private static Vector3 ReadVec3(IGFile main, uint offset)
    {
        main.sh.Seek(offset);
        return new Vector3(main.sh.ReadSingle(), main.sh.ReadSingle(), main.sh.ReadSingle());
    }

    private static Vector4 ReadVec4(IGFile main, uint offset)
    {
        main.sh.Seek(offset);
        return new Vector4(main.sh.ReadSingle(), main.sh.ReadSingle(), main.sh.ReadSingle(), main.sh.ReadSingle());
    }

    /// <summary>Retail VCFUX(...,7): use the first three bytes of the big-endian packed word and
    /// divide by 128, not 255. The untouched 32-bit word is stored separately on the asset.</summary>
    private static Vector3 DecodeNativeLightingRgb8(uint bits)
    {
        float r = (bits >> 24) & 0xFF;
        float g = (bits >> 16) & 0xFF;
        float b = (bits >> 8) & 0xFF;
        return new Vector3(r, g, b) / 128f;
    }
}
