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

    public Matrix4x4 GetTransformMatrix()
    {
        return new Transform3D(Position, Rotation, Scale).ToMatrix();
    }
}
