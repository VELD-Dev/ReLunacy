using LibLunacy.Shaders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Interfaces
{
    public interface IMesh
    {
        public float[] vpos { get; }
        public uint[] indices { get; }
        public uint[] boneWeight { get; }
        public uint[] vertToBonemap { get; }
    }
}
