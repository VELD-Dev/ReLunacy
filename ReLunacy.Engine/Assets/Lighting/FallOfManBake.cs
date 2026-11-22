namespace ReLunacy.Engine.Assets.Lighting;

/// <summary>Two unsigned chroma bytes per lightmapped vertex, and the draw's vertex constant c[5].z.</summary>
public sealed record FallOfManBake(byte[] Chroma, float Scale, float[]? VertexScales = null);
