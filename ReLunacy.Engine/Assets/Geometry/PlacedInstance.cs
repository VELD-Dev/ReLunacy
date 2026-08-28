using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Assets.Primitives;

namespace ReLunacy.Engine.Assets.Geometry;

public sealed class PlacedInstance<TAsset> : IPlacedInstance<TAsset> where TAsset : IAsset
{
    public ulong ID { get; set; }
    public string Name { get; set; }
    public TAsset Asset { get; init; }
    public Vector3 Position { get; init; }
    public Vector3 Rotation { get; init; }
    public float Scale { get; init; }
    public ushort Group { get; init; }
    public float DisplayDistance { get; init; } = -1f;
    public float UpdateDistance { get; init; } = -1f;
    public ushort LightmapIndex { get; init; } = 0xFFFF;

    private readonly Matrix4x4? _rawMatrix;

    public PlacedInstance(TAsset asset, Transform3D transform, ulong tuid, ushort group = 0, string name = "", float displayDistance = -1f, float updateDistance = -1f)
    {
        Asset = asset ?? throw new ArgumentNullException(nameof(asset));
        Position = transform.Position;
        Rotation = transform.Rotation;
        Scale = transform.Scale;
        Group = group;
        Name = name;
        ID = tuid;
        DisplayDistance = displayDistance;
        UpdateDistance = updateDistance;
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
        Matrix4x4.Decompose(rawMatrix, out var scale, out var rotation, out var translation);
        Position = translation;
        Scale = (scale.X + scale.Y + scale.Z) / 3.0f;
        Rotation = QuaternionToEuler(rotation);
        Group = group;
        Name = name;
        ID = tuid;
    }

    public Matrix4x4 GetTransformMatrix() => _rawMatrix ?? new Transform3D(Position, Rotation, Scale).ToMatrix();

    private static Vector3 QuaternionToEuler(Quaternion q)
    {
        const float Rad2Deg = 180f / MathF.PI;
        float sinP = 2f * (q.W * q.X - q.Y * q.Z);
        float pitch = MathF.Abs(sinP) >= 1f ? MathF.CopySign(MathF.PI / 2f, sinP) : MathF.Asin(sinP);
        float yaw = MathF.Atan2(2f * (q.W * q.Y + q.X * q.Z), 1f - 2f * (q.X * q.X + q.Y * q.Y));
        float roll = MathF.Atan2(2f * (q.W * q.Z + q.X * q.Y), 1f - 2f * (q.X * q.X + q.Z * q.Z));
        return new Vector3(pitch * Rad2Deg, yaw * Rad2Deg, roll * Rad2Deg);
    }
}
