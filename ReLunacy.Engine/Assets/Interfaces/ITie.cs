using System.Numerics;

namespace ReLunacy.Engine.Assets.Interfaces;

/// <summary>Static entity (buildings, landscape elements, props) instanced throughout the level.</summary>
public interface ITie : IAsset
{
    IReadOnlyList<IMesh> Meshes { get; }
    float Scale { get; }
    (Vector3 center, float radius) GetBoundingSphere();
}
