using System.Numerics;

namespace ReLunacy.Engine.Assets.Interfaces;

public interface IModel : IAsset
{
    IReadOnlyList<IMesh> Meshes { get; }
    float Scale { get; }
    (Vector3 center, float radius) GetBoundingSphere();
}
