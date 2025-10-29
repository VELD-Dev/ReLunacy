using LibLunacy.Interfaces;
using LibLunacy.Legacy;
using LibLunacy.Textures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Shaders
{
    public class Shader
    {
        public const uint NewInternalTUIDSecID = 0x5600;
        public const uint PointerID = 0x1D100;

        public ulong TUID { get; private set; }
        public string name;
        public ShaderMetadataOld? metadataOld;
        public ShaderMetadataNew? metadataNew;
        public ShaderReference? reference;
        public StreamHelper shaderStream;
        public bool isOld;

        public Texture Albedo;
        public Texture Normal;
        public Texture Expensive;
        public RenderingMode RenderingMode => (RenderingMode)(isOld ? metadataOld!.Value.renderingMode : metadataNew!.Value.renderingMode);

        public Shader(StreamHelper sh, bool isOld = false, uint index = 0)
        {
            this.isOld = isOld;

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
}
