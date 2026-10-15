using System.Numerics;
using ReLunacy.Engine.Assets.Lighting;

namespace ReLunacy.Engine.Rendering;

/// <summary>
/// Retail-derived CPU sampler for the old-engine dynamic lighting environment rooted at 0x8B00.
///
/// EBOOT proof used by this implementation:
/// - 0x5510AC selects the containing 0x8C00 OBB with the smallest (most negative)
///   max(abs(dot(delta, axis)) - extent) value.
/// - 0x550580 maps the selected OBB into the 0x8D00 grid, applies the native half-texel
///   addressing rule, trilinearly filters eight U8 voxels, then evaluates
///   1 - min(Field1C, 1) * (1 - voxel/255).
/// - 0x550A60 expands the three packed local RGB triplets with VCFUX(..., 7), i.e. /128,
///   multiplies the three colours by that weight, and preserves the two direction vectors.
/// - 0x551390 evaluates the resulting rig as
///   colour0 + colour1*max(dot(N,dir0),0) + colour2*max(dot(N,dir1),0).
///
/// This class intentionally performs only the position-dependent part. The final N.L terms belong
/// in the fragment shader so normal maps affect Mobys exactly where the retail formula expects them.
/// </summary>
public static class OldEngineLightingSampler
{
    public readonly record struct Sample(
        bool HasLighting,
        Vector3 Ambient,
        Vector3 Light0Colour,
        Vector3 Light1Colour,
        Vector3 Direction0,
        Vector3 Direction1,
        int VolumeIndex,
        float VoxelWeight);

    public static Sample Global(LightingEnvironment? environment)
    {
        if (environment == null)
            return default;

        DirectionalLight? l0 = environment.Lights.Count > 0 ? environment.Lights[0] : null;
        DirectionalLight? l1 = environment.Lights.Count > 1 ? environment.Lights[1] : null;
        return new Sample(
            true,
            environment.Ambient,
            l0?.Colour ?? Vector3.Zero,
            l1?.Colour ?? Vector3.Zero,
            l0?.Direction ?? Vector3.UnitY,
            l1?.Direction ?? Vector3.UnitY,
            -1,
            1f);
    }

    /// <summary>Samples the retail spatial-lighting path at one world-space point. If the Render A/B
    /// gate is disabled, returns the global 0x8B00 rig instead; this compares spatial vs global light
    /// without incorrectly turning the Moby black or disabling old-engine lighting altogether.</summary>
    public static Sample SampleAt(LightingEnvironment? environment, Vector3 worldPosition)
    {
        if (environment == null)
            return default;

        if (!OldEngineRenderOptions.MobySpatialLightingEnabled)
            return Global(environment);

        SpatialLightingVolume? best = null;
        float bestSignedDistance = 0f;

        foreach (SpatialLightingVolume volume in environment.SpatialVolumes)
        {
            float sx = volume.AxisScale.X;
            float sy = volume.AxisScale.Y;
            float sz = volume.AxisScale.Z;
            if (!(sx > 1e-8f) || !(sy > 1e-8f) || !(sz > 1e-8f))
                continue;

            Vector3 delta = worldPosition - volume.Center;
            float lx = Vector3.Dot(delta, volume.AxisX);
            float ly = Vector3.Dot(delta, volume.AxisY);
            float lz = Vector3.Dot(delta, volume.AxisZ);

            // 0x5512CC..0x551304: max of the three signed distances to the OBB slabs.
            float signedDistance = MathF.Max(
                MathF.Abs(lx) - sx,
                MathF.Max(MathF.Abs(ly) - sy, MathF.Abs(lz) - sz));

            // 0x5510AC initializes the best score at zero. Therefore only points inside an OBB
            // (negative signed distance) qualify, and overlaps select the most deeply-contained one.
            if (signedDistance < bestSignedDistance)
            {
                bestSignedDistance = signedDistance;
                best = volume;
            }
        }

        if (best == null)
            return Global(environment);

        float weight = SampleVoxelWeight(best, worldPosition);
        return new Sample(
            true,
            best.Colour0 * weight,
            best.Colour1 * weight,
            best.Colour2 * weight,
            best.Direction0,
            best.Direction1,
            best.Index,
            weight);
    }

    /// <summary>Native 0x550580 voxel path. The grid coordinate is not simply UV*(size-1):
    /// the game uses texel-centre addressing, grid = size*(local*0.5+0.5)-0.5, then clamps and
    /// trilinearly filters the surrounding eight U8 samples.</summary>
    private static float SampleVoxelWeight(SpatialLightingVolume volume, Vector3 worldPosition)
    {
        if (!volume.HasVoxelData)
            return 1f;

        int sizeX = volume.SizeX;
        int sizeY = volume.SizeY;
        int sizeZ = volume.SizeZ;
        if (sizeX <= 0 || sizeY <= 0 || sizeZ <= 0)
            return 1f;

        Vector3 delta = worldPosition - volume.Center;
        float localX = Vector3.Dot(delta, volume.AxisX) / volume.AxisScale.X;
        float localY = Vector3.Dot(delta, volume.AxisY) / volume.AxisScale.Y;
        float localZ = Vector3.Dot(delta, volume.AxisZ) / volume.AxisScale.Z;

        float gx = Math.Clamp(sizeX * (localX * 0.5f + 0.5f) - 0.5f, 0f, sizeX - 1f);
        float gy = Math.Clamp(sizeY * (localY * 0.5f + 0.5f) - 0.5f, 0f, sizeY - 1f);
        float gz = Math.Clamp(sizeZ * (localZ * 0.5f + 0.5f) - 0.5f, 0f, sizeZ - 1f);

        int x0 = (int)MathF.Floor(gx); int x1 = Math.Min(x0 + 1, sizeX - 1); float tx = gx - x0;
        int y0 = (int)MathF.Floor(gy); int y1 = Math.Min(y0 + 1, sizeY - 1); float ty = gy - y0;
        int z0 = (int)MathF.Floor(gz); int z1 = Math.Min(z0 + 1, sizeZ - 1); float tz = gz - z0;

        float V(int x, int y, int z) => volume.Voxels[x + y * sizeX + z * sizeX * sizeY];
        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        float c000 = V(x0, y0, z0), c100 = V(x1, y0, z0);
        float c010 = V(x0, y1, z0), c110 = V(x1, y1, z0);
        float c001 = V(x0, y0, z1), c101 = V(x1, y0, z1);
        float c011 = V(x0, y1, z1), c111 = V(x1, y1, z1);

        float c00 = Lerp(c000, c100, tx);
        float c10 = Lerp(c010, c110, tx);
        float c01 = Lerp(c001, c101, tx);
        float c11 = Lerp(c011, c111, tx);
        float c0 = Lerp(c00, c10, ty);
        float c1 = Lerp(c01, c11, ty);
        float voxel01 = Lerp(c0, c1, tz) / 255f;

        // Wrapper path used by the retail position sampler caps Field1C at 1 before this blend.
        float field = MathF.Min(volume.Field1C, 1f);
        return 1f - field * (1f - voxel01);
    }
}
