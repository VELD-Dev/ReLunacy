using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Textures;

namespace ReLunacy.Engine.Loading.Shaders;

public class Shader
{
    public const uint NewInternalTUIDSecID = 0x5600;
    public const uint PointerID = 0x1D100;

    public ulong TUID { get; private set; }
    public string name = string.Empty;
    public ShaderMetadataOld? metadataOld;
    public ShaderMetadataNew? metadataNew;
    public ShaderReference? reference;
    public StreamHelper shaderStream;
    public bool isOld;

    public Texture? Albedo;
    public Texture? Normal;
    // Insomniac's "expensive" map - four masks packed into one texture:
    //   R = gloss/specular mask   multiplies the specular term
    //   G = parallax height       fed to fma(h, parallaxScale, parallaxBias)
    //   B = additive emissive     added to the baked light colour
    //   A = detail-map mask       modulates detail map intensity
    // NOTE: GltfExporter.ApplyExpensiveChannels still uses an older R=spec/G=metallic/B=emissive
    // split (R and B match; G does not).
    public Texture? Expensive;
    // Old engine only (ShaderMetadataOld.detailMap, offset 0x0C) - new engine's equivalent isn't
    // identified yet. DXT5 or ARGB8888, four channels:
    //   R = normal delta dx   added to base normal's .x/.y, scaled by detailNormalStrength
    //   G = normal delta dy
    //   B = colour offset     added to albedo, scaled by detailAlbedoStrength
    //   A = gloss offset      added to tex2 gloss term, scaled by detailSpecStrength
    // Both offsets are signed, -1..+1. Sampled at a UV tiled by ShaderMetadataOld.detailTiling
    // (0x58), falling back to AssetManager.DefaultDetailTiling when that field is zero.
    public Texture? DetailMap;
    public RenderingMode RenderingMode => (RenderingMode)(isOld ? metadataOld!.Value.renderingMode : metadataNew!.Value.renderingMode);

    public Shader(StreamHelper sh, bool isOld = false, uint index = 0, ulong? tuidOverride = null)
    {
        this.isOld = isOld;
        shaderStream = sh;

        if (isOld)
        {
            metadataOld = ShaderMetadataOld.Read(sh);
            TUID = index;
        }
        else
        {
            metadataNew = ShaderMetadataNew.Read(sh);
            var ig = new IGFile(sh.BaseStream);
            var refSec = ig.QuerySection(ShaderReference.ID);
            sh.Seek(refSec.offset);
            reference = ShaderReference.Read(sh);
            // reference.Value.TUID reads as a genuine 0 for every shader in this format (see
            // TextureShaderLoader.LoadShadersNew, which already works around this for its own
            // Shaders dictionary key) - tuidOverride carries the real identity instead: the
            // assetlookup pointer-table TUID, the same value Tie.ShaderTUIDs/MaterialReader resolve
            // materials against. Without this, every new-engine Shader object's own TUID property
            // read back as 0, so anything comparing against it directly (ShaderBrowser.SelectShader,
            // its used-shader filter, its Find Usages button) silently failed for every new-engine
            // shader even though the dictionary lookup and material resolution were already correct.
            TUID = tuidOverride ?? reference.Value.TUID;
        }
    }
}
