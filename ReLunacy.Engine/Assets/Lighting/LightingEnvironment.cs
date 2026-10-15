using System.Numerics;

namespace ReLunacy.Engine.Assets.Lighting;

/// <summary>One directional light of a LightingEnvironment. Colour carries its native magnitude;
/// Direction is the unit vector consumed by the old-engine N.L path.</summary>
public sealed class DirectionalLight
{
    public Vector3 Colour { get; set; }
    public Vector3 Direction { get; set; }
}

/// <summary>
/// One old-engine 0x8C00 spatial-lighting record plus its exact 0x8D00 U8 voxel payload.
///
/// The retail position sampler is now closed far enough to use for dynamic Mobys:
///  - containing OBBs are tested and the deepest containing volume wins;
///  - the point is transformed by the three axes/extents;
///  - grid coordinates use size*(local*0.5+0.5)-0.5;
///  - eight U8 cells are trilinearly filtered;
///  - weight = 1 - min(Field1C,1) * (1 - voxel/255);
///  - packed RGB is converted with VCFUX(...,7), i.e. byte/128;
///  - final rig = colour0 + colour1*max(N.dir0,0) + colour2*max(N.dir1,0).
///
/// Fields whose independent semantic labels remain unknown keep neutral names even though their role
/// in this path is known. Raw packed words are retained so the decoded float view never destroys
/// source information needed for cross-level validation.
/// </summary>
public sealed class SpatialLightingVolume
{
    public int Index { get; init; }
    public uint Field00Bits { get; init; }

    /// <summary>Exact big-endian 32-bit source words at +0x04/+0x08/+0x0C. The high three bytes are
    /// the RGB values consumed by the retail lighting conversion; the fourth byte remains preserved.</summary>
    public uint Colour0Bits { get; init; }
    public uint Colour1Bits { get; init; }
    public uint Colour2Bits { get; init; }

    public Vector3 Colour0 { get; init; }
    public Vector3 Colour1 { get; init; }
    public Vector3 Colour2 { get; init; }

    public uint Field10Bits { get; init; }
    public float Field10AsFloat => BitConverter.UInt32BitsToSingle(Field10Bits);

    public byte GridExponentX { get; init; }
    public byte GridExponentY { get; init; }
    public byte GridExponentZ { get; init; }
    public byte GridFlags { get; init; }

    public uint VoxelPointer { get; init; }

    /// <summary>Native blend/attenuation input at +0x1C. The Moby sampler clamps the upper bound to
    /// 1 before evaluating the voxel weight. The original float is kept exactly.</summary>
    public float Field1C { get; init; }

    public Vector3 Direction0 { get; init; }
    public Vector3 Direction1 { get; init; }

    public Vector3 AxisX { get; init; }
    public Vector3 AxisY { get; init; }
    public Vector3 AxisZ { get; init; }
    public Vector3 AxisScale { get; init; }
    public Vector3 Center { get; init; }
    public float CenterW { get; init; }

    public byte[] Voxels { get; init; } = [];

    public int SizeX => VoxelPointer == 0 ? 0 : 1 << GridExponentX;
    public int SizeY => VoxelPointer == 0 ? 0 : 1 << GridExponentY;
    public int SizeZ => VoxelPointer == 0 ? 0 : 1 << GridExponentZ;
    public int VoxelCount => Voxels.Length;
    public bool HasVoxelData => Voxels.Length != 0;
}

/// <summary>
/// Old-engine lighting environment rooted at main.dat 0x8B00. The root carries ambient and two
/// directional lights and points at 0x8C00 spatial volumes backed by 0x8D00 voxel grids.
///
/// Retail evidence currently closes this as the static environment-lighting source sampled for
/// Mobys/moveable objects. Static environment geometry has a separate prelighting path (directional
/// lightmaps and, where authored, vertex lighting), so TIE/UFrag draws must not be silently routed
/// through this sampler merely because the source exists. Exact coverage outside the proven movable
/// path remains an explicit reverse-engineering question rather than a renderer fallback.
/// </summary>
public sealed class LightingEnvironment
{
    public uint Version { get; init; }
    public uint SpatialVolumesPointer { get; init; }

    public Vector3 Ambient { get; set; }
    public List<DirectionalLight> Lights { get; } = [];

    /// <summary>
    /// True when this is NOT a decoded lighting source but the editor's new-engine presentation
    /// fallback (see LightingEnvironmentReader.ReadNewEngineSunPresentation): the sun direction comes
    /// from region.dat, the colours are editor values. UI must say so.
    /// </summary>
    public bool IsPresentationFallback { get; init; }
    public List<SpatialLightingVolume> SpatialVolumes { get; } = [];

    /// <summary>
    /// New engine: the reflection occlusion (floor b, full-strength luminance a) the programs apply as
    /// reflection *= saturate(b + (1 - b) * lum(diffuse light) / a) - region.dat 0x8B00 +0x90 / +0x94,
    /// (0.01, 0.6) on all 15 QForce levels. Null when the level carries none.
    /// </summary>
    public Vector2? ReflectionOcclusion { get; init; }

    /// <summary>
    /// New engine: the game camera's near and far clip planes, region.dat 0x8100 +0x08 / +0x04 (QForce
    /// 0.06 / 1800 on 12 of 17 regions). The renderer converts the game's D24 polygon offset into world
    /// terms with them. Null when the level carries none.
    /// </summary>
    public Vector2? CameraClip { get; set; }

    /// <summary>
    /// New engine: the atmosphere table, region.dat 0x34004 - 512 ARGB u32 (A = fog amount). Every draw
    /// of the fog programs reads it at its distance from the camera over the far plane and mixes
    /// mix(colour, K.rgb, K.w) with K = pow(lerp(table[t x 512]), 2.2) (QForce EBOOT 0x00718030 /
    /// 0x00717DC0; Docs/reverse/QFORCE_FILE_STRUCTURE_2026-10-08.md §3). Null when the level has none.
    /// </summary>
    public uint[]? FogTable { get; set; }

    /// <summary>
    /// New engine: region.dat 0x34003, the second 512-entry atmosphere table (linear ARGB). The atmosphere
    /// pass brightens the scene by it at the pixel's distance: c + c L (1 - c) (FP 0x8E). Null when absent
    /// or all zero.
    /// </summary>
    public uint[]? InscatterTable { get; set; }

    /// <summary>New engine: the scattering parameters of region.dat 0x34000 (<see cref="NewEngineAtmosphere"/>).</summary>
    public NewEngineAtmosphere? Atmosphere { get; set; }

    /// <summary>The level's bloom: new engine region.dat 0x34000 +0x80..+0x90, old engine main.dat 0x7100
    /// +0x70..+0x8C (set by LevelReader from <see cref="LevelFog.Bloom"/>); null when the level has none.</summary>
    public BloomParameters? Bloom { get; set; }
}

/// <summary>
/// The new engine's atmosphere scattering, region.dat 0x34000 (version 5) +0x10..+0x30, as the per-view
/// block reads it (QForce EBOOT 0x00716E24: view +0x78 = +0x10, +0x84..+0x98 = +0x1C..+0x30) and
/// 0x007181B8 turns it into the atmosphere pass's constants (Docs/reverse/QFORCE_FILE_STRUCTURE_2026-10-08.md
/// §3.2). <see cref="Direction"/> is zero when the level leaves it to the sun.
/// </summary>
public readonly record struct NewEngineAtmosphere(
    Vector3 Direction, Vector3 Colour, float Intensity, float Turbidity, float G);

/// <summary>
/// The new engine's bloom parameters, region.dat 0x34000 (0xF4 bytes, version 5) +0x80..+0x90: copied to
/// the global environment block 0x0109D480 +0xB0 (QForce EBOOT 0x00715B80) and, with no environment
/// volume active, to the per-view block read by the post function 0x006BD8C0 (PROVEN). acid_refinery
/// (0.55, 1.9, 0.65, 0, 0.8), jungle_ruins_pvp (0.555, 3, 0.4, 0, 0.8), pvp_dlc_1 (0.7, 2.5, 0.4, 0.55, 0.8).
///
/// Tools of Destruction runs the same chain (EBOOT 0x004F2E14, the ancestor of QForce's 0x006BD8C0) from
/// main.dat 0x7100 (<see cref="LevelFog.Bloom"/>). <see cref="ColourExtract"/> false is its alpha-only
/// extract (FP 95 of the post table 0x100C3660: the glow mask alone, white), picked by 0x7100 +0x8C = 0.
/// <see cref="DestinationAlpha"/>: the glow mask is the old engine's destination alpha, which the Sky
/// alone writes (Dome, Color shells, Clouds, Bloom shells; the clear and the opaque world leave 0) -
/// rather than the new engine's per-program glow output.
/// </summary>
public readonly record struct BloomParameters(
    float ExtractScale, float ExtractExponent, float MipWeight, float HistoryBlend, float Intensity,
    bool ColourExtract = true, bool DestinationAlpha = false);
