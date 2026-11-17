using System.Numerics;

namespace ReLunacy.Engine.Importing;

/// <summary>
/// A triangle soup to cook into collision chunks (see <see cref="CollisionCooker"/>), in the Tie class's own space:
/// the space the placement matrix is applied to. Winding is kept as given.
/// </summary>
public sealed class CollisionInput
{
    public required Vector3[] Positions { get; init; }

    /// <summary>Three indices into <see cref="Positions"/> per triangle.</summary>
    public required int[] Indices { get; init; }

    /// <summary>One 32-bit collision material word per triangle. The cooker builds each chunk's palette (at most 4
    /// words) from the words its triangles use.</summary>
    public required uint[] TriangleMaterialWord { get; init; }

    /// <summary>Optional triangle flags (5 bits; bit 0 = no wall jump). Null = all 0.</summary>
    public byte[]? TriangleFlags { get; init; }

    public int TriangleCount => Indices.Length / 3;
}
