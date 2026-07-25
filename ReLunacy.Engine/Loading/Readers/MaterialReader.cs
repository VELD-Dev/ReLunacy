using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Assets.Materials;
using ReLunacy.Engine.Assets.Textures;
using ReLunacy.Engine.Loading.Shaders;

namespace ReLunacy.Engine.Loading.Readers;

// Resolves mesh shader indices to real materials/textures via a TextureShaderLoader, instead
// of the flat solid-color placeholder the reader pipeline previously fell back on. Old engine
// shader indices are direct keys into the shader table; new engine indices are local to the
// owning asset and must first be resolved through that asset's ShaderTUIDs table.
public sealed class MaterialReader
{
    private readonly TextureShaderLoader _loader;
    private readonly Dictionary<ulong, Material> _materialCache = [];
    private readonly Dictionary<ulong, Texture> _textureCache = [];
    private Material? _defaultMaterial;

    public MaterialReader(TextureShaderLoader loader)
    {
        _loader = loader;
    }

    /// <summary>Old engine: shaderIndex is a direct key into the shader table.</summary>
    public IMaterial GetMaterialByIndex(uint shaderIndex) => GetMaterialByTuid(shaderIndex);

    /// <summary>New engine: shaderIndex is local to the owning Moby/Tie/Zone and must be resolved through its ShaderTUIDs table first.</summary>
    public IMaterial GetMaterialForLocalIndex(ulong[]? shaderTuids, uint shaderIndex)
    {
        if (shaderTuids != null && shaderIndex < shaderTuids.Length)
            return GetMaterialByTuid(shaderTuids[shaderIndex]);
        return GetDefaultMaterial();
    }

    /// <summary>
    /// Wraps every texture the loader read from textures.dat/highmips.dat, not just the ones
    /// referenced by a shader actually used by the currently loaded level's geometry — cut/unused
    /// textures (interesting for datamining, e.g. Hidden Palace-style prototype content) never get
    /// touched by <see cref="GetMaterialByTuid"/>, since that only walks shaders reachable from
    /// loaded meshes. Reuses the same wrap cache, so nothing gets double-wrapped.
    /// </summary>
    public IReadOnlyDictionary<ulong, ITexture> GetAllTextures()
    {
        foreach (var legacy in _loader.Textures.Values)
            WrapTexture(legacy);

        return _textureCache.ToDictionary(kv => kv.Key, kv => (ITexture)kv.Value);
    }

    public IMaterial GetMaterialByTuid(ulong tuid)
    {
        if (_materialCache.TryGetValue(tuid, out var cached))
            return cached;

        if (!_loader.Shaders.TryGetValue(tuid, out var shader))
            return GetDefaultMaterial();

        Texture? albedo = shader.Albedo != null ? WrapTexture(shader.Albedo) : null;

        var material = Material.Create(
            id: tuid,
            albedo: albedo,
            normal: shader.Normal != null ? WrapTexture(shader.Normal) : null,
            properties: shader.Expensive != null ? WrapTexture(shader.Expensive) : null,
            renderMode: ToRenderMode(shader.RenderingMode),
            alphaClipThreshold: GetAlphaClip(shader),
            usesVertexAlphaCandidate: UsesVertexAlphaCandidate(shader.RenderingMode, albedo));
        material.Name = shader.name;

        _materialCache[tuid] = material;
        return material;
    }

    // See TextureMetadataOld.AlphaKillCandidate — logged once per distinct texture so a real
    // level load can show whether this bit actually correlates with textures that should be
    // transparent but currently render solid.
    private static readonly HashSet<ulong> _loggedAlphaKillTextures = [];

    private Texture WrapTexture(Textures.Texture legacy)
    {
        if (_textureCache.TryGetValue(legacy.id, out var cached))
            return cached;

        if (legacy.isOld && legacy.textureMetadata is Textures.TextureMetadataOld oldMeta && oldMeta.AlphaKillCandidate && _loggedAlphaKillTextures.Add(legacy.id))
            Console.WriteLine($"Diagnostic: texture {legacy.id:X} ('{legacy.name}') has the candidate old-engine alphaKill bit set (unverified — see TextureMetadataOld.AlphaKillCandidate).");

        var texture = Texture.FromData(legacy.id, legacy.Width, legacy.Height, ToTextureFormat(legacy.TexFormat), legacy.data, (int)legacy.MipmapCounts);
        texture.Name = legacy.name;
        _textureCache[legacy.id] = texture;
        return texture;
    }

    private Material GetDefaultMaterial()
    {
        if (_defaultMaterial != null)
            return _defaultMaterial;

        var texture = Texture.FromData(0, 1, 1, TextureFormat.A8R8G8B8, [255, 255, 255, 255]);
        texture.Name = "DefaultTexture";

        _defaultMaterial = Material.Create(id: 0, albedo: texture, renderMode: RenderMode.Opaque);
        _defaultMaterial.Name = "DefaultMaterial";
        return _defaultMaterial;
    }

    // Full 9-value mapping per RenderingMode.cs. Only Opaque/Cutout/Overlay/Additive have a
    // confirmed-appropriate treatment in this engine's simplified 4-case RenderMode; Scunge,
    // Soft-Edge, Blended, Baked Only, and Lit Only don't have independently confirmed blend
    // behavior yet, so they're conservatively mapped to the closest reasonable guess below rather
    // than left to silently fall through — flagged per-case so it's easy to find and correct once
    // more is known about each. AssetManager already renders every material with
    // RasterizerStateDescription.CULL_NONE regardless of backface culling differences between
    // modes, so that distinction (if any of these have one) wouldn't currently change anything
    // downstream either way.
    //
    // The enum is believed exhaustive (all 9 values 0x00-0x08 accounted for), but the default
    // case below stays defensive — logged once per distinct byte — in case something outside that
    // range shows up in a file this hasn't been checked against yet.
    private static readonly HashSet<byte> _loggedUnknownRenderingModes = [];

    private static RenderMode ToRenderMode(RenderingMode mode)
    {
        switch (mode)
        {
            case RenderingMode.Opaque: return RenderMode.Opaque;
            case RenderingMode.Cutout: return RenderMode.AlphaClip;
            case RenderingMode.Overlay: return RenderMode.AlphaBlend;
            case RenderingMode.Additive: return RenderMode.Additive;
            // Guess: "blended" implies alpha blend like Overlay above — stronger guess now that
            // this is its own mode, not conflated with "baked only" anymore.
            case RenderingMode.Blended: return RenderMode.AlphaBlend;
            // Guess: "soft-edge" strongly suggests a depth-based edge fade (soft particles), which
            // isn't implemented by anything downstream yet — treated as plain alpha blend for now,
            // which is at least not wrong about needing to blend, just incomplete about how.
            case RenderingMode.SoftEdge: return RenderMode.AlphaBlend;
            // Guess: unclear semantics for all three — "baked"/"lit" read more like lighting
            // qualifiers than transparency, so defaulting to Opaque is the conservative choice
            // (risks looking solid when it should be transparent, not invisible/wrong-blended).
            case RenderingMode.Scunge: return RenderMode.Opaque;
            case RenderingMode.BakedOnly: return RenderMode.Opaque;
            case RenderingMode.LitOnly: return RenderMode.Opaque;
            default:
                byte raw = (byte)mode;
                if (_loggedUnknownRenderingModes.Add(raw))
                    Console.WriteLine($"Warning: Unrecognized shader renderingMode byte 0x{raw:X2} (outside the believed-exhaustive 0x00-0x08 range — falling back to Opaque).");
                return RenderMode.Opaque;
        }
    }

    private static float GetAlphaClip(Shader shader) =>
        shader.isOld ? shader.metadataOld!.Value.alphaClip : shader.metadataNew!.Value.alphaClip;

    // Per-material opacity (ShaderMetadata's decalOffsetCandidate/opacityCandidate at 0x48/0x4C)
    // was retracted — it explained flat dimming but not the spatial fade actually seen in-game.
    // The current best lead is per-vertex alpha (VertexFormat0.boneIndex, see PackedNormal-style
    // decode on that field) — but the user suspects there's a shader-level enum somewhere that
    // says whether a given mesh's ambiguous vertex field means bone index, vertex alpha, or vertex
    // color (not yet found). Until that's identified, this is the one condition confirmed to
    // correlate: a blending render mode with no albedo alpha to source transparency from.
    private static bool UsesVertexAlphaCandidate(RenderingMode mode, ITexture? albedo) =>
        mode is RenderingMode.Overlay or RenderingMode.SoftEdge or RenderingMode.Blended && !HasAlphaChannel(albedo);

    private static bool HasAlphaChannel(ITexture? texture) =>
        texture?.Format is TextureFormat.A8R8G8B8 or TextureFormat.DXT3 or TextureFormat.DXT5;

    private static TextureFormat ToTextureFormat(Textures.TextureFormat format) => format switch
    {
        Textures.TextureFormat.R5G6B5 => TextureFormat.R5G6B5,
        Textures.TextureFormat.A8R8G8B8 => TextureFormat.A8R8G8B8,
        Textures.TextureFormat.DXT1 => TextureFormat.DXT1,
        Textures.TextureFormat.DXT3 => TextureFormat.DXT3,
        Textures.TextureFormat.DXT5 => TextureFormat.DXT5,
        _ => TextureFormat.Unknown,
    };
}
