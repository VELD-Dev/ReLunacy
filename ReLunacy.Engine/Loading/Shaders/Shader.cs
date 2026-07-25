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
    public Texture? Expensive;
    // Old engine only so far (ShaderMetadataOld.detailMap, offset 0x0C) — ShaderMetadataNew
    // hasn't had its equivalent identified yet. Confirmed layout: B = roughness, R/G = a second,
    // higher-frequency tangent-space normal map, sampled at a tiled UV. The tiling scale itself
    // hasn't been located in ShaderMetadata's still-unidentified byte ranges — consumers
    // (GltfExporter) use a placeholder constant until it's found.
    public Texture? DetailMap;
    public RenderingMode RenderingMode => (RenderingMode)(isOld ? metadataOld!.Value.renderingMode : metadataNew!.Value.renderingMode);

    public Shader(StreamHelper sh, bool isOld = false, uint index = 0)
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
            TUID = reference.Value.TUID;
        }
    }
}
