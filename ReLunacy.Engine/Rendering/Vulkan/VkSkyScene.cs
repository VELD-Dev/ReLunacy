using System.Numerics;
using NeoVeldrid;
using OldTextureDescriptorState = ReLunacy.Engine.Assets.Textures.OldTextureDescriptorState;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>One native old-engine SkyShell draw after file decoding and GPU texture resolution.</summary>
public readonly record struct VkSkyDraw(
    uint FirstIndex,
    uint IndexCount,
    int TextureSlot,
    int RetailPassId,
    ushort DrawOrder,
    float Rotation10,
    float Rotation14,
    float SpinRate,
    float InitialPhase,
    bool Bloom);

/// <summary>
/// Expanded proof-bounded packet for retail Sky pass 14 (Dome). VertexData keeps ten floats per
/// generated retail vertex: position4, quantized UNORM8 colour4, projectedAttr2. The projected pair
/// is preserved because pass 17 Clouds consumes it directly.
/// </summary>
public sealed class VkSkyDome
{
    public const uint FloatsPerVertex = 10;
    public const uint VertexStride = FloatsPerVertex * sizeof(float);

    public float[] VertexData { get; }
    public uint[] Indices { get; }
    public Vector4 VertexConstant42 { get; }
    public Vector4 FragmentConstant0 { get; }

    /// <summary>
    /// True when the terminal retail C0 path is closed. For the native Dome this is proven:
    /// SkyDefs +0x44..+0x50 -> Sky runtime +0x90..+0x9C -> embedded FP constant slot in
    /// fragment shader 0x100C1930. This path uses the raw float4, not runtime packed colour +0x60.
    /// </summary>
    public bool FragmentConstant0JoinProven { get; }

    public bool HasGeometry => VertexData.Length != 0 && Indices.Length != 0;

    public VkSkyDome(
        float[] vertexData,
        uint[] indices,
        Vector4 vertexConstant42,
        Vector4 fragmentConstant0,
        bool fragmentConstant0JoinProven)
    {
        VertexData = vertexData;
        Indices = indices;
        VertexConstant42 = vertexConstant42;
        FragmentConstant0 = fragmentConstant0;
        FragmentConstant0JoinProven = fragmentConstant0JoinProven;
    }
}

/// <summary>One six-float parameter block consumed by retail helper 0x580024.</summary>
public readonly record struct VkSkyCloudLayer(
    float Period,
    float Angle,
    float PrimaryScroll,
    float Bias0,
    float SecondaryScroll,
    float Bias1);

/// <summary>
/// Proof-bounded packet for native Sky pass 17. It reuses the generated Dome position/projected
/// attributes and binds the three physical textures from SkyDefs +0xE8/+0xEC/+0xF0 as TEX4/5/6.
/// Artistic LF/HF labels are deliberately avoided because that naming is still open.
/// </summary>
public sealed class VkSkyClouds
{
    public int Texture4Slot { get; }
    public int Texture5Slot { get; }
    public int Texture6Slot { get; }
    public VkSkyCloudLayer Layer0 { get; }
    public VkSkyCloudLayer Layer1 { get; }
    public float Texture4PerturbScale { get; }
    public float Texture5PerturbScale { get; }
    public float LayerBlend { get; }
    public Vector4 ColourMultiplier { get; }
    public Vector4 VertexConstant42 { get; }

    public bool HasTextures => Texture4Slot >= 0 && Texture5Slot >= 0 && Texture6Slot >= 0;

    public VkSkyClouds(
        int texture4Slot,
        int texture5Slot,
        int texture6Slot,
        VkSkyCloudLayer layer0,
        VkSkyCloudLayer layer1,
        float texture4PerturbScale,
        float texture5PerturbScale,
        float layerBlend,
        Vector4 colourMultiplier,
        Vector4 vertexConstant42)
    {
        Texture4Slot = texture4Slot;
        Texture5Slot = texture5Slot;
        Texture6Slot = texture6Slot;
        Layer0 = layer0;
        Layer1 = layer1;
        Texture4PerturbScale = texture4PerturbScale;
        Texture5PerturbScale = texture5PerturbScale;
        LayerBlend = layerBlend;
        ColourMultiplier = colourMultiplier;
        VertexConstant42 = vertexConstant42;
    }
}

/// <summary>
/// CPU-side old-engine Sky packet prepared for the viewport renderer. Dome is generated from the
/// recovered retail 0x580574 producer; SkyShell geometry remains authored data from vertices.dat;
/// pass 17 Clouds reuses the native Dome stream and its proven three-texture shader contract.
/// Destination-alpha Bloom remains proof-gated.
/// </summary>
public sealed class VkSkyScene
{
    public const uint FloatsPerVertex = 10;
    public const uint VertexStride = FloatsPerVertex * sizeof(float);

    public VkSkyDome? Dome { get; }
    public VkSkyClouds? Clouds { get; }
    public float[] VertexData { get; }
    public uint[] Indices { get; }
    public Texture[] Textures { get; }
    public OldTextureDescriptorState?[] TextureDescriptors { get; }
    public VkSkyDraw[] Draws { get; }
    public float TimeScale { get; }
    /// <summary>SkyDefs +0xE0, the Bloom shells' alpha factor (<see cref="Assets.Sky.SkyDefs.BloomShellScale"/>).</summary>
    public float BloomShellScale { get; }

    public bool HasDome => Dome is { HasGeometry: true };
    public bool HasClouds => HasDome && Clouds is { HasTextures: true };
    public bool HasShellGeometry => VertexData.Length != 0 && Indices.Length != 0 && Draws.Length != 0;
    public bool HasGeometry => HasDome || HasShellGeometry;
    public int ColorDrawCount => Draws.Count(x => !x.Bloom);
    public int BloomDrawCount => Draws.Count(x => x.Bloom);

    public VkSkyScene(
        VkSkyDome? dome,
        VkSkyClouds? clouds,
        float[] vertexData,
        uint[] indices,
        Texture[] textures,
        OldTextureDescriptorState?[] textureDescriptors,
        VkSkyDraw[] draws,
        float timeScale,
        float bloomShellScale = 0f)
    {
        if (textures.Length != textureDescriptors.Length)
            throw new ArgumentException("Sky texture/descriptor arrays must stay parallel.", nameof(textureDescriptors));

        Dome = dome;
        Clouds = clouds;
        VertexData = vertexData;
        Indices = indices;
        Textures = textures;
        TextureDescriptors = textureDescriptors;
        Draws = draws;
        TimeScale = timeScale;
        BloomShellScale = float.IsFinite(bloomShellScale) ? bloomShellScale : 0f;

        SkyShellCoverageAudit.Log(this);
    }
}
