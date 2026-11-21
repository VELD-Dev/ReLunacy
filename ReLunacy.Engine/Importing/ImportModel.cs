using System.Numerics;

namespace ReLunacy.Engine.Importing;

/// <summary>One source mesh of an import, in class-local space (the node transform already applied), as a triangle
/// list. A glTF mesh primitive is one part; its <see cref="Slot"/> is the material the user picked for it. The arrays
/// are per vertex and have the same length; vertices may repeat (the splitter welds them).</summary>
public sealed class ImportPart
{
    public string Name = "";
    /// <summary>Index into <see cref="ImportModel.Slots"/>.</summary>
    public int Slot;
    public Vector3[] Positions = [];
    public Vector3[] Normals = [];
    /// <summary>xyz = tangent, w = +1 or -1, the sign of the bitangent (B = w * cross(N, T)) in the stored UV space.</summary>
    public Vector4[] Tangents = [];
    public Vector2[] Uv0 = [];
    /// <summary>Lightmap unwrap (glTF TEXCOORD_1): the class's Half2 lightmap array for the parts of a lightmapped slot, and the space the
    /// overlay mask is painted in.</summary>
    public Vector2[]? Uv1;
    /// <summary>RGBA; only alpha is written (vertex position w: the per-vertex alpha of transparent materials, and an overlay
    /// primitive's layer weight, |w| &amp; 0x7F / 127). Null = opaque.</summary>
    public Vector4[]? Colors;
    /// <summary>Triangle list into the vertex arrays; the winding is kept.</summary>
    public int[] Indices = [];
}

/// <summary>One entry of the class's shader list (0x5600): the material every part with this slot uses, with the
/// per-material fields a Tie primitive carries. The defaults are the retail majority; fill them from a template
/// primitive that uses the same shader (the feature mask and fades are a property of the shader).</summary>
public sealed class ImportMaterialSlot
{
    public string Name = "";
    public ulong ShaderTuid;
    /// <summary>The primitive's lightmap gate (+0x0E bit 0): its parts need <see cref="ImportPart.Uv1"/>, written to the class's lightmap
    /// array; each placement then needs a lightmap pair (TieAppender writes a neutral one).</summary>
    public bool Lightmapped;
    /// <summary>ACiT feature mask (primitive +0x24): 0x1D on 97% of retail primitives.</summary>
    public byte FeatureMask = 0x1D;
    /// <summary>Fades (primitive +0x30..+0x36: normal map, reflection, detail, 1): (35, 120, 8, 1) on 73% of retail primitives.</summary>
    public short[] Fades = [35, 120, 8, 1];
    /// <summary>Primitive +0x0A, the blend-mask index; 0xFFFF = single layer, 0 = the class's overlay mask (retail: every overlay
    /// primitive, 129 / 129 sampled). Part of the batch key.</summary>
    public ushort BlendMask = 0xFFFF;
    /// <summary>The overlay layer: the index in <see cref="ImportModel.Slots"/> of the second material (primitive +0x22 / +0x2B), -1 for
    /// none. Needs <see cref="Lightmapped"/>, <see cref="BlendMask"/> 0 and <see cref="ImportModel.OverlayMask"/>.</summary>
    public int OverlaySlot = -1;
    /// <summary>The overlay layer's UVs (the class's second Half2 run): UV0 x scale + offset.</summary>
    public Vector2 OverlayUvScale = Vector2.One, OverlayUvOffset = Vector2.Zero;
    /// <summary>0x3500 record: UV units per world unit on u and v. Null = measured from the parts that use the slot.</summary>
    public Vector2? UvDensity;
}

/// <summary>Everything the splitter and the class builder need, independent of where it came from (glTF later).</summary>
public sealed class ImportModel
{
    /// <summary>The class name (0x3410).</summary>
    public string Name = "";
    public List<ImportPart> Parts = [];
    public List<ImportMaterialSlot> Slots = [];
    /// <summary>The overlay mask (DXT1, one mip, square 128..1024), painted in lightmap UV space: the class's 0x5800 texels.</summary>
    public EncodedTexture? OverlayMask;
    /// <summary>The mask's id (0x5900 +0x00).</summary>
    public uint OverlayMaskId;
}
