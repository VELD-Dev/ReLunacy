using ReLunacy.Engine.Loading.Textures;
using System.Runtime.CompilerServices;
using ReLunacy.Engine.Rendering.Resources;
using NeoVeldrid;
using AssetTexture = ReLunacy.Engine.Assets.Textures.Texture;
using OldTextureDescriptorState = ReLunacy.Engine.Assets.Textures.OldTextureDescriptorState;
using NeoTexture = NeoVeldrid.Texture;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>Turns a built material into the raw-Vulkan renderer's <see cref="VkMaterialDesc"/>.
/// Texture handles and their old-engine descriptor provenance are captured together so future/native
/// sampler and gamma application can use serialized RSX state instead of guessing from texture roles.
/// Proven baked sampling is already specialized; ordinary albedo/normal/properties descriptor
/// consumption is still incomplete, so provenance here means "available and preserved", not "fully
/// reproduced by Vulkan".</summary>
public static class VkMaterialBuilder
{
    private sealed class DescriptorLookup
    {
        public readonly Dictionary<GpuTexture, OldTextureDescriptorState?> ByGpu =
            new(ReferenceEqualityComparer.Instance);
        public readonly HashSet<GpuTexture> NewEngine = new(ReferenceEqualityComparer.Instance);

        public DescriptorLookup(AssetManager assets)
        {
            foreach (var (id, gpu) in assets.BuiltTextures)
            {
                OldTextureDescriptorState? descriptor = null;
                if (assets.SourceTextures.TryGetValue(id, out var source) && source is AssetTexture oldTexture)
                {
                    descriptor = oldTexture.SamplingDescriptor;
                    if (oldTexture.OldDescriptor == null && oldTexture.NewEngineDescriptor != null)
                        NewEngine.Add(gpu);
                }

                // A shared fallback GpuTexture can stand in for several failed source textures.
                // Once a GPU object is ambiguous, keep it permanently descriptor-less rather than
                // accidentally assigning one failed source's RSX state to all users of the fallback.
                if (!ByGpu.TryAdd(gpu, descriptor))
                    ByGpu[gpu] = null;
            }
        }
    }

    private static readonly ConditionalWeakTable<AssetManager, DescriptorLookup> DescriptorLookups = new();

    public static VkMaterialDesc Build(RenderMaterial material, AssetManager? assetManager)
    {
        byte gameRenderMode = 0;
        bool usesVertexAlpha = false;
        bool albedoHasAlphaChannel = false;
        assetManager?.TryGetVkMaterialInfo(material, out gameRenderMode, out usesVertexAlpha, out albedoHasAlphaChannel);

        OldTextureDescriptorState? lightDirDescriptor = DescriptorOf(material, "fLightDir", assetManager);
        OldTextureDescriptorState? albedoDescriptor = DescriptorOf(material, MaterialMapType.Albedo, assetManager);
        OldTextureDescriptorState? lightColourDescriptor = DescriptorOf(material, "fLightColour", assetManager);
        float hasBaked = ValueOf(material, "fLightColour");
        var shaderInfo = assetManager?.GetVkNewEngineShaderInfo(material)
            ?? (IsNewEngine: false, ShaderClass: (byte)0, ShaderFlags: (byte)0);

        return new VkMaterialDesc
        {
            Albedo = TextureOf(material, MaterialMapType.Albedo),
            Normal = TextureOf(material, MaterialMapType.Normal),
            Props = TextureOf(material, "fProperties"),
            LightColour = TextureOf(material, "fLightColour"),
            LightDir = TextureOf(material, "fLightDir"),
            Detail = TextureOf(material, "fDetail"),
            AuxiliaryA = TextureOf(material, "fAuxiliaryA"),
            AuxiliaryB = TextureOf(material, "fAuxiliaryB"),

            AlbedoOldDescriptor = DescriptorOf(material, MaterialMapType.Albedo, assetManager),
            NormalOldDescriptor = DescriptorOf(material, MaterialMapType.Normal, assetManager),
            PropsOldDescriptor = DescriptorOf(material, "fProperties", assetManager),
            LightColourOldDescriptor = DescriptorOf(material, "fLightColour", assetManager),
            LightDirOldDescriptor = lightDirDescriptor,

            HasBaked = ValueOf(material, "fLightColour"),
            ParallaxScale = ValueOf(material, "fParallaxScale"),
            ParallaxBias = ValueOf(material, "fParallaxBias"),
            AlphaThreshold = ValueOf(material, MaterialMapType.Albedo),
            GameRenderMode = gameRenderMode,
            UsesVertexAlpha = usesVertexAlpha ? 1f : 0f,
            AlbedoHasAlphaChannel = albedoHasAlphaChannel ? 1f : 0f,
            IsBillboard = assetManager != null && assetManager.IsBillboardMaterial(material) ? 1f : 0f,
            IsNewEngine = shaderInfo.IsNewEngine,
            ShaderClass = shaderInfo.ShaderClass,
            ShaderFlags = shaderInfo.ShaderFlags,
            OldEngineCullBack = assetManager?.IsVkOldEngineCulled(material) ?? false,

            // Descriptor-driven, never assumed. When a material carries no old-engine LightDir
            // descriptor there is nothing to expand and the shader must leave the sample alone.
            ReflectionTint = assetManager?.GetVkReflectionTint(material) ?? System.Numerics.Vector4.Zero,
            ReflectionParameters = assetManager?.GetVkReflectionParameters(material) ?? System.Numerics.Vector4.Zero,
            EnvironmentCube = assetManager?.GetVkEnvironmentCube(material) ?? 0,
            MaterialStrengths = assetManager?.GetVkMaterialStrengths(material) ?? System.Numerics.Vector2.One,
            MaterialPermutation = assetManager?.GetVkMaterialPermutation(material) ?? System.Numerics.Vector4.Zero,
            MobyTint = assetManager?.GetVkMobyTint(material) ?? System.Numerics.Vector4.Zero,
            EmbeddedProgram = (float)(assetManager?.GetVkEmbeddedProgram(material) ?? EmbeddedProgram.Generic),
            ProgramBundle = assetManager?.GetVkProgramBundle(material),
            Albedo2 = TextureOf(material, "fAlbedo2"),
            Normal2 = TextureOf(material, "fNormal2"),
            Props2 = TextureOf(material, "fProperties2"),
            Detail2 = TextureOf(material, "fDetail2"),
            BlendMask = TextureOf(material, "fBlendMask"),
            SecondTint = assetManager?.GetVkSecondLayer(material)?.Tint ?? System.Numerics.Vector4.Zero,
            SecondParallax = assetManager?.GetVkSecondLayer(material)?.Parallax ?? System.Numerics.Vector4.Zero,
            LightDirBiasedExpand =
                lightDirDescriptor is { } lightDir &&
                RsxTextureFormatConversion.Decode(lightDir).ColourChannelsFullyBiased ? 1f : 0f,
            AlbedoGamma = albedoDescriptor is { } albedoState && RsxTextureFormatConversion.Decode(albedoState).HasGamma ? 1f : 0f,
            LightColourGamma = lightColourDescriptor is { } lightState && RsxTextureFormatConversion.Decode(lightState).HasGamma ? 1f : 0f,
            NewEngineRnm = hasBaked > 0f && IsNewEngine(material, "fLightDir", assetManager) ? 1f : 0f,
        };
    }

    private static bool IsNewEngine(RenderMaterial material, MaterialMapKey key, AssetManager? assets)
    {
        if (assets == null) return false;
        var gpu = material.GetMaterialMap(key)?.Texture;
        if (gpu == null) return false;
        var lookup = DescriptorLookups.GetValue(assets, static a => new DescriptorLookup(a));
        return lookup.NewEngine.Contains(gpu);
    }

    private static NeoTexture? TextureOf(RenderMaterial material, MaterialMapKey key) =>
        material.GetMaterialMap(key)?.Texture?.DeviceTexture;

    private static float ValueOf(RenderMaterial material, MaterialMapKey key) =>
        material.GetMaterialMap(key)?.Value ?? 0f;

    private static OldTextureDescriptorState? DescriptorOf(
        RenderMaterial material,
        MaterialMapKey key,
        AssetManager? assets)
    {
        if (assets == null) return null;
        var gpu = material.GetMaterialMap(key)?.Texture;
        if (gpu == null) return null;
        var lookup = DescriptorLookups.GetValue(assets, static a => new DescriptorLookup(a));
        return lookup.ByGpu.TryGetValue(gpu, out var descriptor) ? descriptor : null;
    }
}
