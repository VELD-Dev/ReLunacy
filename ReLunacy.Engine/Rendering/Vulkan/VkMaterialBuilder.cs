using ReLunacy.Engine.Rendering.Resources;
using NeoVeldrid;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>Turns a built material into the raw-Vulkan renderer's <see cref="VkMaterialDesc"/>.
/// Shared by every view that feeds the renderer a scene (the level view and the asset preview), so
/// they shade materials identically.</summary>
public static class VkMaterialBuilder
{
    public static VkMaterialDesc Build(RenderMaterial material, AssetManager? assetManager)
    {
        byte gameRenderMode = 0;
        bool usesVertexAlpha = false;
        bool albedoHasAlphaChannel = false;
        assetManager?.TryGetVkMaterialInfo(material, out gameRenderMode, out usesVertexAlpha, out albedoHasAlphaChannel);

        return new VkMaterialDesc
        {
            Albedo = TextureOf(material, MaterialMapType.Albedo),
            Normal = TextureOf(material, MaterialMapType.Normal),
            Props = TextureOf(material, "fProperties"),
            LightColour = TextureOf(material, "fLightColour"),
            LightDir = TextureOf(material, "fLightDir"),
            // fLightColour's value slot is the "this material has a real bake" flag.
            HasBaked = ValueOf(material, "fLightColour"),
            ParallaxScale = ValueOf(material, "fParallaxScale"),
            ParallaxBias = ValueOf(material, "fParallaxBias"),
            AlphaThreshold = ValueOf(material, MaterialMapType.Albedo),
            // The game's 0-6 render mode and vertex-alpha flags come from AssetManager's side table,
            // not a map slot.
            GameRenderMode = gameRenderMode,
            UsesVertexAlpha = usesVertexAlpha ? 1f : 0f,
            AlbedoHasAlphaChannel = albedoHasAlphaChannel ? 1f : 0f,
            // Foliage sprite cards are billboarded in the vertex shader, so they need the billboard
            // pipeline rather than the lit one.
            IsBillboard = assetManager != null && assetManager.IsBillboardMaterial(material) ? 1f : 0f,
        };
    }

    /// <summary>A material map's texture as a NeoVeldrid texture. Normally non-null (AssetManager
    /// provides defaults); null is handled by the renderer.</summary>
    private static Texture? TextureOf(RenderMaterial material, MaterialMapKey key) =>
        material.GetMaterialMap(key)?.Texture?.DeviceTexture;

    private static float ValueOf(RenderMaterial material, MaterialMapKey key) =>
        material.GetMaterialMap(key)?.Value ?? 0f;
}
