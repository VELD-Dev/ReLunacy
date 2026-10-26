using System.Numerics;

namespace ReLunacy.Engine.Rendering;

/// <summary>
/// Proof-bounded arithmetic recovered from the Tools of Destruction BakedShadows subsystem.
/// This class contains only closed math. Interaction discovery and the producer of the local shadow
/// basis remain separate because their terminal ownership is not fully closed yet.
/// </summary>
public static class OldEngineMobyShadowMath
{
    public const float ShadowFloor = 0.550000012f;
    public const float ShadowRange = 0.449999988f;
    public const float FourTapWeight = 0.112499997f;
    public const int MinTargetDimension = 32;
    public const int MaxTargetDimension = 1024;
    public const int TargetAlignment = 32;

    public static readonly Vector2[] ReceiverTapOffsets =
    [
        new(-0.5f,  0.0f),
        new( 0.7f, -0.4f),
        new( 0.4f,  0.5f),
        new(-0.7f,  0.6f),
    ];

    /// <summary>
    /// Reconstructs the exact local orthographic matrix emitted by the native helper recovered at
    /// Build-A 0x003DBDD0. X/Y map to [-1,+1] and Z to [0,1]. System.Numerics uses the same
    /// row-vector storage convention as the recovered executable matrix here.
    /// </summary>
    public static Matrix4x4 BuildLocalProjection(
        Vector3 localMin,
        Vector3 localMax,
        Vector3 basis0,
        Vector3 basis1,
        Vector3 basis2)
    {
        Vector3 extent = localMax - localMin;
        if (!(extent.X > 1e-8f) || !(extent.Y > 1e-8f) || !(extent.Z > 1e-8f))
            return Matrix4x4.Identity;

        float sx = 2f / extent.X;
        float sy = 2f / extent.Y;
        float sz = 1f / extent.Z;
        float tx = -(localMin.X + localMax.X) / extent.X;
        float ty = -(localMin.Y + localMax.Y) / extent.Y;
        float tz = -localMin.Z / extent.Z;

        return new Matrix4x4(
            basis0.X * sx, basis0.Y * sy, basis0.Z * sz, 0f,
            basis1.X * sx, basis1.Y * sy, basis1.Z * sz, 0f,
            basis2.X * sx, basis2.Y * sy, basis2.Z * sz, 0f,
            tx,            ty,            tz,            1f);
    }

    /// <summary>
    /// Exact native clip-to-shadow-texture transform derived from the first 4x4 block. TOD flips Y
    /// before the receiver samples the local depth target.
    /// </summary>
    public static Matrix4x4 BuildClipToTexture(Matrix4x4 m)
    {
        return new Matrix4x4(
             0.5f * m.M11, -0.5f * m.M12, m.M13, m.M14,
             0.5f * m.M21, -0.5f * m.M22, m.M23, m.M24,
             0.5f * m.M31, -0.5f * m.M32, m.M33, m.M34,
             0.5f * m.M41 + 0.5f,
            -0.5f * m.M42 + 0.5f,
                     m.M43,
                     m.M44);
    }

    /// <summary>
    /// Native adaptive BakedShadows target dimension. The context selects either 32 or 64 texels per
    /// world unit; dimensions are rounded, clamped to [32,1024] and aligned upward to 32 pixels.
    /// </summary>
    public static int AdaptiveTargetDimension(float extent, bool lowDensity)
    {
        float density = lowDensity ? 32f : 64f;
        int scaled = (int)MathF.Round(MathF.Max(extent, 0f) * density);
        scaled = Math.Clamp(scaled, MinTargetDimension, MaxTargetDimension);
        return Math.Min(MaxTargetDimension, AlignUp(scaled, TargetAlignment));
    }

    public static float FilterFourComparisons(float s0, float s1, float s2, float s3) =>
        Math.Clamp(FourTapWeight * (s0 + s1 + s2 + s3) + ShadowFloor, ShadowFloor, 1f);

    private static int AlignUp(int value, int alignment) =>
        checked((value + alignment - 1) / alignment * alignment);
}
