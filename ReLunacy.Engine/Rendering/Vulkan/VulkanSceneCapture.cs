using System.Runtime.CompilerServices;
using ReLunacy.Engine.Rendering.Resources;
using NeoVeldrid;
using OldTextureDescriptorState = ReLunacy.Engine.Assets.Textures.OldTextureDescriptorState;
using NeoTexture = NeoVeldrid.Texture;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>One material's inputs for the raw-Vulkan lit renderer: the six authored material slots
/// (albedo, normal, properties, detail, auxiliary A, auxiliary B), the two per-instance baked-light
/// textures, plus the per-material scalars the shader needs.
/// Old-engine texture descriptors travel beside the GPU handles losslessly. That provenance is the
/// source of truth for native ADDRESS/FILTER/LOD/gamma work; it must not be confused with complete
/// consumption. The baked 0x5400/0x5410 path already has its proven dedicated sampler, while ordinary
/// albedo/normal/properties still use compatibility/global sampling for parts of the descriptor state
/// and exact per-channel RSX gamma remains OPEN.</summary>
public struct VkMaterialDesc
{
    public NeoTexture? Albedo;
    public NeoTexture? Normal;
    public NeoTexture? Props;
    public NeoTexture? LightColour;
    public NeoTexture? LightDir;
    public NeoTexture? Detail;
    public NeoTexture? AuxiliaryA;
    public NeoTexture? AuxiliaryB;
    // Two-layer TIE primitives (AssetManager.GetOrBuildMaterial with a second layer): the second
    // material's albedo / normal / properties / detail and the blend mask - RSX units 8..12.
    public NeoTexture? Albedo2;
    public NeoTexture? Normal2;
    public NeoTexture? Props2;
    public NeoTexture? Detail2;
    public NeoTexture? BlendMask;
    /// <summary>The second material's +0x50 (group 15) and (+0x70, +0x74, +0x78, 0) (group 17).</summary>
    public System.Numerics.Vector4 SecondTint;
    public System.Numerics.Vector4 SecondParallax;
    public readonly bool IsTwoLayer => BlendMask != null;

    // CPU-side provenance only. These are never copied into a GPU struct. Keeping the canonical
    // OldTextureDescriptorState avoids creating a second, drifting interpretation of RSX sampler words.
    public OldTextureDescriptorState? AlbedoOldDescriptor;
    public OldTextureDescriptorState? NormalOldDescriptor;
    public OldTextureDescriptorState? PropsOldDescriptor;
    public OldTextureDescriptorState? LightColourOldDescriptor;
    public OldTextureDescriptorState? LightDirOldDescriptor;

    public float HasBaked;
    public float ParallaxScale;
    public float ParallaxBias;
    public float AlphaThreshold;
    /// <summary>The GAME's rendering mode (0 Opaque, 1 Overlay, 2 Additive, 3 Scunge, 4 Cutout,
    /// 5 Soft-Edge, 6 Blended) - the renderer implements each one's real RSX blend/depth/alpha state.</summary>
    public float GameRenderMode;
    public float UsesVertexAlpha;
    public float AlbedoHasAlphaChannel;
    /// <summary>1 = foliage geometry requiring the billboard vertex path.</summary>
    public float IsBillboard;
    /// <summary>True for ShaderMetadataNew materials. Old-engine materials deliberately keep their
    /// previous renderer routing and culling behaviour.</summary>
    public bool IsNewEngine;
    /// <summary>ShaderMetadataNew +0x22/+0x23, preserved for class dispatch and raster state.</summary>
    public byte ShaderClass;
    public byte ShaderFlags;
    /// <summary>Tools of Destruction: the material's +0x13 bit 0x02 is clear, so the game culls back faces.</summary>
    public bool OldEngineCullBack;

    /// <summary>
    /// 1 when this material's LightDir (0x5410) descriptor selects the native ADDRESS
    /// UNSIGNED_REMAP=BIASED range expansion on its logical colour channels, decoded through
    /// <see cref="ReLunacy.Engine.Loading.Textures.RsxTextureFormatConversion"/>.
    ///
    /// This is a SEPARATE hardware stage from CONTROL1. CONTROL1 decides which stored component
    /// reaches which logical channel; ADDRESS.UNSIGNED_REMAP decides whether that logical value is
    /// expanded from [0,1] to [-1,1]. Resolving the first does not resolve the second, and the
    /// baked-sampler reverse shows 0x5410 setting BIASED on every audited descriptor while 0x5400
    /// does not. The expansion is deliberately not baked into the uploaded RGBA8 (that would destroy
    /// the signed range for the texture editor), so the shader has to perform it.
    /// </summary>
    public float LightDirBiasedExpand;

    /// <summary>1 when the albedo's RSX descriptor sets ADDRESS gamma: the sample is sRGB and is
    /// linearised in the shader (and the lit result re-encoded). New-engine colour maps all do;
    /// old-engine ones never do, so this is a no-op there.</summary>
    public float AlbedoGamma;

    /// <summary>Same for the baked colour map (0x5400). New-engine: 0x730303 on every one.</summary>
    public float LightColourGamma;

    /// <summary>1 for a new-engine baked draw: 0x5410 holds three Radiosity-Normal-Mapping basis
    /// weights, not a direction (decoded from the retail QForce TIE fragment programs).</summary>
    public float NewEngineRnm;

    /// <summary>ShaderMetadataOld +0x40 reflection tint (rgb) and albedo mix factor (w).</summary>
    public System.Numerics.Vector4 ReflectionTint;
    /// <summary>New-engine 0x5000 +0x60: (unused, Fresnel exponent, cube-alpha bias, Fresnel gain).
    /// Zero on old-engine materials. See Assets/Materials/Material.ReflectionParametersSource.</summary>
    public System.Numerics.Vector4 ReflectionParameters;
    /// <summary>New-engine 0x5000 +0x40: environment cube slot 0..3 (set 0 bindings 3, 5, 6, 7).
    /// QForce EBOOT: cube descriptor = base + index*0x20, bound to retail RSX unit 7.</summary>
    public float EnvironmentCube;
    /// <summary>New-engine 0x5000 +0x30 (normal-map strength) and +0x34 (reflection strength), the
    /// c[21].xy the game's renderers upload; (1, 1) where they do not apply.</summary>
    public System.Numerics.Vector2 MaterialStrengths;
    /// <summary>New-engine (detail tiling, detail gain +0x3C, detail present, normal-map permutation);
    /// zero on old-engine materials. See AssetManager.GetVkMaterialPermutation.</summary>
    public System.Numerics.Vector4 MaterialPermutation;
    /// <summary>New-engine Moby tint (rgb, 1 when it applies). See AssetManager.GetVkMobyTint.</summary>
    public System.Numerics.Vector4 MobyTint;
    /// <summary>Class 0x13: the embedded program family the lit shader emulates, 0 for the generic path.</summary>
    public float EmbeddedProgram;
    /// <summary>Class 0x13: the material's program bundle, drawn through RsxFragmentTranslator when it translates.</summary>
    public ReLunacy.Engine.Loading.Shaders.NewEngineShaderProgramBundle? ProgramBundle;
}

/// <summary>Which kind of scene object an instance came from, so the Render menu's per-type toggles can
/// filter draws without rebuilding the scene.</summary>
[Flags]
public enum SceneEntityKind : byte
{
    None = 0,
    Moby = 1,
    Tie = 2,
    UFrag = 4,
    Foliage = 8,
    Other = 16,
    TieCollision = 32,
    WorldCollision = 64,
    Path = 128,
    All = Moby | Tie | UFrag | Foliage | Other | TieCollision | WorldCollision | Path,
}

/// <summary>The game's rendering modes (ShaderMetadataOld 0x11), with the RSX states the EBOOT reverse
/// established for each.</summary>
public enum GameRenderMode : byte
{
    Opaque = 0,
    Overlay = 1,
    Additive = 2,
    Scunge = 3,
    Cutout = 4,
    SoftEdge = 5,
    Blended = 6,
}

/// <summary>Geometry registry bridging the asset system to the from-scratch renderer.</summary>
public static class VulkanSceneCapture
{
    /// <summary>Floats per vertex in <see cref="VertexData"/>: position xyz, texcoord uv, normal xyz,
    /// tangent xyzw (w = bitangent handedness), lightmap texcoord uv2, colour rgba, joint indices
    /// (skeleton-global, stored as whole-number floats), joint weights.</summary>
    public const int FloatsPerVertex = 26;

    public static readonly Dictionary<object, int> MeshToGeometry = new(ReferenceEqualityComparer.Instance);
    public static readonly List<float[]> VertexData = new();
    public static readonly List<uint[]> Indices = new();
    /// <summary>
    /// Source-authoritative old-engine TIE shader-use state parallel to <see cref="VertexData"/>.
    /// Keeping it per geometry is important: two primitives sharing one material may carry different
    /// authored use bytes, so material-level inference is not an acceptable substitute.
    /// </summary>
    public static readonly List<VkOldTieDrawState> OldTieDrawStates = new();
    /// <summary>Per captured geometry: its new-engine TIE primitive window, or null.</summary>
    public static readonly List<Assets.Geometry.NewEngineTiePrimitive?> NewEngineTiePrimitives = new();
    /// <summary>Per captured geometry: its new-engine fade distances (TIE primitive, Moby frag), or null.</summary>
    public static readonly List<System.Numerics.Vector3?> NewEngineFadeDistances = new();
    /// <summary>Per captured geometry: its animated UVs (section 0x5C00), or null.</summary>
    public static readonly List<Assets.Geometry.UvAnimation?> UvAnimations = new();

    /// <summary>Packs a Vertex3D[] into position xyz, UV, normal xyz, tangent xyzw, lightmap UV and RGBA.</summary>
    public static float[] Interleave(Vertex3D[] vertices)
    {
        var data = new float[vertices.Length * FloatsPerVertex];
        for (int v = 0; v < vertices.Length; v++)
        {
            int o = v * FloatsPerVertex;
            data[o + 0] = vertices[v].Position.X;
            data[o + 1] = vertices[v].Position.Y;
            data[o + 2] = vertices[v].Position.Z;
            data[o + 3] = vertices[v].TexCoords.X;
            data[o + 4] = vertices[v].TexCoords.Y;
            data[o + 5] = vertices[v].Normal.X;
            data[o + 6] = vertices[v].Normal.Y;
            data[o + 7] = vertices[v].Normal.Z;
            data[o + 8] = vertices[v].Tangent.X;
            data[o + 9] = vertices[v].Tangent.Y;
            data[o + 10] = vertices[v].Tangent.Z;
            data[o + 11] = vertices[v].Tangent.W;
            data[o + 12] = vertices[v].TexCoords2.X;
            data[o + 13] = vertices[v].TexCoords2.Y;
            data[o + 14] = vertices[v].Color.X;
            data[o + 15] = vertices[v].Color.Y;
            data[o + 16] = vertices[v].Color.Z;
            data[o + 17] = vertices[v].Color.W;
            data[o + 18] = vertices[v].Joints.X;
            data[o + 19] = vertices[v].Joints.Y;
            data[o + 20] = vertices[v].Joints.Z;
            data[o + 21] = vertices[v].Joints.W;
            data[o + 22] = vertices[v].Weights.X;
            data[o + 23] = vertices[v].Weights.Y;
            data[o + 24] = vertices[v].Weights.Z;
            data[o + 25] = vertices[v].Weights.W;
        }
        return data;
    }

    public static void Register(object mesh, float[] vertexData, uint[] indices)
    {
        if (MeshToGeometry.ContainsKey(mesh)) return;
        MeshToGeometry[mesh] = VertexData.Count;
        VertexData.Add(vertexData);
        Indices.Add(indices);
        OldTieDrawStates.Add(mesh is RenderMesh renderMesh
            ? VkOldTieDrawState.From(renderMesh.OldTieShaderUse)
            : default);
        NewEngineTiePrimitives.Add((mesh as RenderMesh)?.NewEngineTiePrimitive);
        NewEngineFadeDistances.Add((mesh as RenderMesh)?.NewEngineFadeDistances);
        UvAnimations.Add((mesh as RenderMesh)?.UvAnimation);
    }

    public static bool TryGet(object mesh, out int geometryIndex) => MeshToGeometry.TryGetValue(mesh, out geometryIndex);

    public static bool Update(object mesh, float[] vertexData)
    {
        if (!MeshToGeometry.TryGetValue(mesh, out int index)) return false;
        VertexData[index] = vertexData;
        return true;
    }

    public static void Clear()
    {
        MeshToGeometry.Clear();
        VertexData.Clear();
        Indices.Clear();
        OldTieDrawStates.Clear();
        NewEngineTiePrimitives.Clear();
        NewEngineFadeDistances.Clear();
        UvAnimations.Clear();
    }
}
