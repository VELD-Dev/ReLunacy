using System.Numerics;
using LibLunacy.Experimental.Core.Interfaces;
using LibLunacy.Experimental.Core.Primitives;

namespace LibLunacy.Experimental.Assets.Geometry;

/// <summary>
/// Represents a placed instance of an asset in the world
/// </summary>
public sealed class PlacedInstance<TAsset> : IPlacedInstance<TAsset> where TAsset : IAsset
{
    public ulong ID { get; set; }
    public string Name { get; set; }
    public TAsset Asset { get; init; }
    public Vector3 Position { get; init; }
    public Vector3 Rotation { get; init; }
    public float Scale { get; init; }
    public ushort Group { get; init; }

    private readonly Matrix4x4? _rawMatrix;

    public PlacedInstance(TAsset asset, Transform3D transform, ulong tuid, ushort group = 0, string name = "")
    {
        Asset = asset ?? throw new ArgumentNullException(nameof(asset));
        Position = transform.Position;
        Rotation = transform.Rotation;
        Scale = transform.Scale;
        Group = group;
        Name = name;
        ID = tuid;
    }

    public PlacedInstance(TAsset asset, Vector3 position, Vector3 rotation, float scale, ulong tuid, ushort group = 0, string name = "")
    {
        Asset = asset ?? throw new ArgumentNullException(nameof(asset));
        Position = position;
        Rotation = rotation;
        Scale = scale;
        Group = group;
        Name = name;
        ID = tuid;
    }

    public PlacedInstance(TAsset asset, Matrix4x4 rawMatrix, ulong tuid, ushort group = 0, string name = "")
    {
        Asset = asset ?? throw new ArgumentNullException(nameof(asset));
        _rawMatrix = rawMatrix;
        // Decompose for display properties
        Matrix4x4.Decompose(rawMatrix, out var scale, out var rotation, out var translation);
        Position = translation;
        Scale = (scale.X + scale.Y + scale.Z) / 3.0f;
        // Convert quaternion to Euler angles in degrees
        var euler = QuaternionToEuler(rotation);
        Rotation = euler;
        Group = group;
        Name = name;
        ID = tuid;
    }

    public Matrix4x4 GetTransformMatrix()
    {
        if (_rawMatrix.HasValue)
            return _rawMatrix.Value;
        return new Transform3D(Position, Rotation, Scale).ToMatrix();
    }

    private static Vector3 QuaternionToEuler(Quaternion q)
    {
        const float Rad2Deg = 180f / MathF.PI;
        // YXZ Euler extraction
        float sinP = 2f * (q.W * q.X - q.Y * q.Z);
        float pitch = MathF.Abs(sinP) >= 1f ? MathF.CopySign(MathF.PI / 2f, sinP) : MathF.Asin(sinP);
        float yaw = MathF.Atan2(2f * (q.W * q.Y + q.X * q.Z), 1f - 2f * (q.X * q.X + q.Y * q.Y));
        float roll = MathF.Atan2(2f * (q.W * q.Z + q.X * q.Y), 1f - 2f * (q.X * q.X + q.Z * q.Z));
        return new Vector3(pitch * Rad2Deg, yaw * Rad2Deg, roll * Rad2Deg);
    }
}
