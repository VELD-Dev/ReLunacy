using System.Numerics;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Objects;

/// <summary>
/// Old-engine ToD section 0x3F00 record. There is exactly one record per 0x3300 TIE Primitive
/// and the 0x3400 definition points to the first record in parallel with its 0x3300 pointer.
/// Decoding the actual 0x9000 TIE vertices proves this is a primitive-local world-unit AABB.
/// </summary>
[FileStructure(Size)]
public record struct TiePrimitiveBounds
{
    public const uint Size = 0x20;

    [FileOffset(0x00)] public Vector3 HalfExtents;
    [FileOffset(0x0C)] public float Reserved0C;
    [FileOffset(0x10)] public Vector3 Center;
    [FileOffset(0x1C)] public float Reserved1C;

    public readonly Vector3 Min => Center - HalfExtents;
    public readonly Vector3 Max => Center + HalfExtents;
}
