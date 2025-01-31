using LibLunacy.Numerics;
using LibLunacy.Objects.Instances;

namespace LibLunacy.Objects
{
    public class Volume
    {
        public const uint TransformSectionID = 0x2505C;
        public ulong TUID => metadata.TUID;
        public string name;
        public InstanceMetadata metadata;
        public Vec3 position;
        public Quat rotation;
        public Vec3 scale;

        public Volume(LunaStream stream, Mat4 transform)
        {
            metadata = new InstanceMetadata(stream);
            name = stream.ReadString((int)metadata.namePointer, false);
            Mat4.Decompose(transform, out scale, out rotation, out position);
        }
    }
}
