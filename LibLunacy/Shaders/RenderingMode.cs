using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Shaders
{
    public enum RenderingMode : byte
    {
        Opaque = 0x00,
        AlphaClip = 0x04,
        AlphaBlend = 0x06
    }
}
