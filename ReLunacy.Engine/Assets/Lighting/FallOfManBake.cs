namespace ReLunacy.Engine.Assets.Lighting;

/// <summary>Two unsigned chroma bytes per vertex and the draw's c[5].z. VertexDirections carries the
/// three bytes that replace the lightmap UV in vertex-lit UFrags.</summary>
public sealed record FallOfManBake(byte[] Chroma, float Scale, float[]? VertexScales = null, byte[]? VertexDirections = null);

/// <summary>Fall of Man Tie placement's four-byte prelight stream and the two c[5].z scales.</summary>
public sealed record FallOfManTiePrelight(byte[] Words, bool PlacementHasLightmap, float LightmappedScale, float PrelightScale);

/// <summary>The two vertex indices and lightmap bit in a Fall of Man Tie primitive.</summary>
public readonly record struct FallOfManTiePrimitive(int FirstVertex, int FirstSecondaryVertex, bool Lightmapped)
{
    public int PrelightFirstWord(bool placementHasLightmap) =>
        placementHasLightmap ? (Lightmapped ? -1 : FirstVertex - FirstSecondaryVertex) : FirstVertex;
}
