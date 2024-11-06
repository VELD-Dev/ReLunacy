using LibLunacy.Interfaces;
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
        public ShaderMetadata metadata;
        public ShaderReference? reference;
        public LunaStream shaderStream;

        public Texture Albedo;
        public Texture Normal;
        public Texture Expensive;
        public RenderingMode RenderingMode => (RenderingMode)metadata.renderingMode;

        public Shader(LunaStream stream, bool isOld = false, uint index = 0)
        {
            metadata = new ShaderMetadata(stream, isOld);
            if (isOld) TUID = index;

            if (!isOld)
            {
                var ig = new IGFile(stream);
                var refSec = ig.QuerySection(ShaderReference.ID);
                stream.Seek(refSec.offset);
                reference = new ShaderReference(stream);
                TUID = reference.Value.TUID;
            }
        }
    }
}
