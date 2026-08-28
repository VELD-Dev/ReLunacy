using ReLunacy.Engine.Rendering.Resources;
using NeoVeldrid;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>Turns a built material into the raw-Vulkan renderer's <see cref="VkMaterialDesc"/>.
///
/// Shared by every view that feeds the renderer a scene (the level view and the asset preview), so the
/// two cannot drift into shading the same material differently - which is exactly the sort of thing
/// that makes a preview a bad reference for the real thing.</summary>
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
            // The game's own 0-6 mode + vertex-alpha flags come from AssetManager's side table rather
            // than a map slot: none of them is a texture, and the renderer wants all three in one lookup.
            GameRenderMode = gameRenderMode,
            UsesVertexAlpha = usesVertexAlpha ? 1f : 0f,
            AlbedoHasAlphaChannel = albedoHasAlphaChannel ? 1f : 0f,
            // Foliage sprite cards are billboarded in the vertex shader from data packed into the
            // geometry, so they need the billboard pipeline rather than the lit one.
            IsBillboard = assetManager != null && assetManager.IsBillboardMaterial(material) ? 1f : 0f,
        };
    }

    /// <summary>A material map's texture as a NeoVeldrid texture. Every material has
    /// albedo/normal/properties/fLightColour/fLightDir maps (AssetManager provides defaults), so these
    /// are normally non-null; null is handled by the renderer.</summary>
    private static Texture? TextureOf(RenderMaterial material, MaterialMapKey key) =>
        material.GetMaterialMap(key)?.Texture?.DeviceTexture;

    private static float ValueOf(RenderMaterial material, MaterialMapKey key) =>
        material.GetMaterialMap(key)?.Value ?? 0f;
}
