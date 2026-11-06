using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Assets.Geometry;

/// <summary>
/// A new-engine Tie primitive as the retail renderer addresses it: +0x04 first vertex, +0x06 secondary
/// index, +0x08 vertex count, +0x0E bit 0 (drawn with the placement's bake) and the +0x30/+0x32/+0x34
/// fade distances (normal map, reflection, detail), and the +0x20 mask the loader ANDs with the
/// material's feature bits.
/// </summary>
public readonly record struct NewEngineTiePrimitive(int FirstVertex, int SecondaryIndex, int VertexCount, bool Lightmapped,
    System.Numerics.Vector3 FadeDistances = default, byte FeatureMask = 0x1F)
{
    /// <summary>
    /// Index of this primitive's vertex 0 in its placement's prelight words (QForce EBOOT 0x0073F5D8:
    /// attribute 4 = placement +0x54 + (+0x04) * 4 on an unbaked placement, + (+0x04 - +0x06) * 4 on a
    /// baked one), or -1 when a baked placement draws this primitive with its bake instead.
    /// </summary>
    public int PrelightFirstWord(bool placementBaked) =>
        placementBaked ? (Lightmapped ? -1 : FirstVertex - SecondaryIndex) : FirstVertex;
}

public sealed class Mesh : IMesh
{
    public IGeometry Geometry { get; init; }
    public IMaterial Material { get; init; }
    public string? Name { get; set; }
    public string? VertexFormatName { get; init; }
    public Func<int, string?>? VertexDumper { get; init; }

    /// <summary>
    /// Old-engine Tie per-primitive shader-use state reconstructed from proven retail loader rules.
    /// Null for every geometry family where that state does not exist or has not been proven.
    /// </summary>
    public OldTieShaderUse? OldTieShaderUse { get; init; }

    /// <summary>
    /// Old-engine Tie only: where this primitive's vertices start inside its prototype's shared vertex
    /// block (0x3300 +0x04). Vertex i of this mesh is prototype vertex SourceVertexBase + i, which is
    /// what lets an editor map a picked render vertex back to the file. -1 when not applicable.
    /// </summary>
    public int SourceVertexBase { get; init; } = -1;

    /// <summary>Animated UVs (section 0x5C00), or null.</summary>
    public UvAnimation? UvAnimation { get; init; }

    /// <summary>New-engine Tie only: the primitive's vertex window and bake gate, null elsewhere.</summary>
    public NewEngineTiePrimitive? NewEngineTiePrimitive { get; init; }

    /// <summary>New engine: this primitive's normal-map / reflection / detail fade distances (Tie
    /// primitive or Moby frag record +0x30), or null.</summary>
    public System.Numerics.Vector3? NewEngineFadeDistances { get; init; }

    /// <summary>
    /// One material per shader/material variant set of the owning Moby, in variant order, or empty
    /// when the asset has only one. Element 0 is always <see cref="Material"/>.
    ///
    /// A new-engine Moby stores its variants as a second dimension of 0xD700: the same frags listed
    /// again with different material slots (Docs/reverse/NEW_ENGINE_MOBY_CLASS_2026-09-19.md). The
    /// geometry is shared, so only the material differs and this list is all an editor needs to show
    /// another variant.
    /// </summary>
    public IReadOnlyList<IMaterial> MaterialVariants { get; init; } = [];

    /// <summary>New engine, two-layer Tie primitive: the material blended in by <see cref="BlendMask"/>
    /// (Docs/reverse/NEW_ENGINE_TWO_LAYER_AND_GAMEPLAY_CONSTANTS_2026-10-07.md), or null. The vertex alpha
    /// candidates of such a mesh carry the per-vertex weight, low 7 bits of |position.w| / 128.</summary>
    public IMaterial? SecondMaterial { get; init; }

    /// <summary>New engine, two-layer Tie primitive: the blend mask (unit 12), or null.</summary>
    public ITexture? BlendMask { get; init; }

    public Mesh(IGeometry geometry, IMaterial material, string? name = null, string? vertexFormatName = null, Func<int, string?>? vertexDumper = null, OldTieShaderUse? oldTieShaderUse = null, IReadOnlyList<IMaterial>? materialVariants = null)
    {
        MaterialVariants = materialVariants ?? [];
        Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        Material = material ?? throw new ArgumentNullException(nameof(material));
        Name = name;
        VertexFormatName = vertexFormatName;
        VertexDumper = vertexDumper;
        OldTieShaderUse = oldTieShaderUse;
    }
}
