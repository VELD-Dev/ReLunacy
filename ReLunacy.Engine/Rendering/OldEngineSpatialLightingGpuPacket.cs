using System.Numerics;
using ReLunacy.Engine.Assets.Lighting;

namespace ReLunacy.Engine.Rendering;

/// <summary>
/// Immutable GPU-friendly representation of the old-engine 0x8C00/0x8D00 spatial-lighting data.
///
/// This packet deliberately preserves the already-proven CPU sampler contract instead of inventing a
/// second lighting model. Each 0x8C00 volume occupies ten RGBA32F texels; every 0x8D00 U8 voxel is
/// concatenated into one R8 stream. A shader can therefore reproduce 0x5510AC + 0x550580 at the
/// fragment's real world position without reducing an entire Moby to one lighting sample.
/// </summary>
public sealed class OldEngineSpatialLightingGpuPacket
{
    public const int DescriptorTexelsPerVolume = 10;
    public const int DescriptorFloatsPerVolume = DescriptorTexelsPerVolume * 4;
    public const int VoxelTextureWidth = 4096;

    public float[] DescriptorRgba32F { get; }
    public byte[] VoxelsR8 { get; }
    public int VolumeCount { get; }
    public int VoxelTextureHeight { get; }

    private OldEngineSpatialLightingGpuPacket(float[] descriptors, byte[] voxels, int volumeCount)
    {
        DescriptorRgba32F = descriptors;
        VoxelsR8 = voxels;
        VolumeCount = volumeCount;
        VoxelTextureHeight = Math.Max(1, (voxels.Length + VoxelTextureWidth - 1) / VoxelTextureWidth);
    }

    public static OldEngineSpatialLightingGpuPacket Build(LightingEnvironment? environment)
    {
        if (environment == null || environment.SpatialVolumes.Count == 0)
            return new OldEngineSpatialLightingGpuPacket([], [], 0);

        int volumeCount = environment.SpatialVolumes.Count;
        var descriptors = new float[checked(volumeCount * DescriptorFloatsPerVolume)];
        int voxelCount = environment.SpatialVolumes.Sum(static volume => volume.HasVoxelData ? volume.Voxels.Length : 0);
        var voxels = new byte[voxelCount];
        int voxelBase = 0;

        for (int i = 0; i < volumeCount; i++)
        {
            SpatialLightingVolume volume = environment.SpatialVolumes[i];
            int o = i * DescriptorFloatsPerVolume;

            Write(descriptors, o + 0,  volume.Center, volume.Field1C);
            Write(descriptors, o + 4,  volume.AxisX, volume.AxisScale.X);
            Write(descriptors, o + 8,  volume.AxisY, volume.AxisScale.Y);
            Write(descriptors, o + 12, volume.AxisZ, volume.AxisScale.Z);
            Write(descriptors, o + 16, volume.Colour0, volume.SizeX);
            Write(descriptors, o + 20, volume.Colour1, volume.SizeY);
            Write(descriptors, o + 24, volume.Colour2, volume.SizeZ);
            Write(descriptors, o + 28, volume.Direction0, voxelBase);
            Write(descriptors, o + 32, volume.Direction1, volume.HasVoxelData ? volume.Voxels.Length : 0);
            Write(descriptors, o + 36, new Vector3(volume.Index, 0f, 0f), volume.HasVoxelData ? 1f : 0f);

            if (!volume.HasVoxelData)
                continue;

            volume.Voxels.CopyTo(voxels, voxelBase);
            voxelBase += volume.Voxels.Length;
        }

        return new OldEngineSpatialLightingGpuPacket(descriptors, voxels, volumeCount);
    }

    private static void Write(float[] destination, int offset, Vector3 xyz, float w)
    {
        destination[offset + 0] = xyz.X;
        destination[offset + 1] = xyz.Y;
        destination[offset + 2] = xyz.Z;
        destination[offset + 3] = w;
    }
}
