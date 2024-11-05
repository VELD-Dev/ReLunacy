using LibLunacy.Interfaces;
using LibLunacy.Objects.Instances;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Objects
{
    public class Volume
    {
        public const uint TransformSectionID = 0x2505C;
        public ulong TUID => metadata.TUID;
        public string name;
        public InstanceMetadata metadata;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;

        public Volume(LunaStream stream, Matrix4x4 transform)
        {
            metadata = new InstanceMetadata(stream);
            name = stream.ReadString((int)metadata.namePointer, false);
            Matrix4x4.Decompose(transform, out scale, out rotation, out position);
        }
    }
}
