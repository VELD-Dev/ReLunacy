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
    // Insomniac's "expensive" map - four unrelated masks packed into one texture. Channel roles
    // read off the captured shaders, which agree across all six programs dumped so far (one UFrag
    // program in dev/, five tie programs in dev/ties/):
    //   R = gloss / specular mask   multiplies the specular term
    //   G = PARALLAX HEIGHT         fed to fma(h, parallaxScale, parallaxBias)
    //   B = additive emissive       added to the baked light colour
    //   A = detail-map mask         "an optional channel in the standard shader template that
    //                               functions as a detail map mask - it modulates the intensity
    //                               with which the detail map is applied" (WWS post-mortem)
    // G is what pins ShaderMetadataOld.UsesParallax: in the three programs that do parallax, tex2
    // is sampled twice (once at the raw UV for the height, once at the offset UV) and .y is read;
    // in the three that don't, tex2 is sampled once and .y is never touched. 6/6, no exceptions.
    // NOTE: GltfExporter.ApplyExpensiveChannels still uses the older R=spec / G=metallic /
    // B=emissive split. R and B survive that revision; G does not.
    public Texture? Expensive;
    // Old engine only so far (ShaderMetadataOld.detailMap, offset 0x0C) - ShaderMetadataNew
    // hasn't had its equivalent identified yet.
    //
    // Channel layout, stated outright in Insomniac's own post-mortem for this game
    // (dev/Ratchet_and_Clank_WWS_Debrief_Feb_08.pdf, "Improved Detail Maps"): four channels, DXT5
    // or ARGB8888, holding a colour map offset, a gloss map offset, and the two partial derivatives
    // of a normal map delta. The captured UFrag fragment shader pins which channel is which, by
    // where each one lands:
    //   R = normal delta dx  ] added to the base normal map's .x/.y, scaled by fc[4].xy
    //   G = normal delta dy  ]   (ShaderMetadataOld.detailNormalStrength)
    //   B = COLOUR offset      added to albedo, scaled by fc[5].z (detailAlbedoStrength)
    //   A = GLOSS offset       added to the tex2 gloss term, scaled by fc[5].w (detailSpecStrength,
    //                          a misnomer kept only because the name is threaded through IMaterial)
    // Both offsets are SIGNED, -1..+1 - the game gets that via the RSX texture remap, so anything
    // decoding this map itself has to apply the same bias rather than read it as unorm.
    // This supersedes the earlier "B = roughness, R/G = a second normal map" reading: R/G were
    // right, but B is a colour offset and the gloss offset in A was missed entirely.
    //
    // Sampled at a tiled UV; the tiling scale is ShaderMetadataOld.detailTiling (0x58), with
    // AssetManager.DefaultDetailTiling standing in only when the field reads a literal zero.
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
