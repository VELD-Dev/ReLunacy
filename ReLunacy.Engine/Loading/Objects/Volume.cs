using System.Numerics;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Objects.Instances;

namespace ReLunacy.Engine.Loading.Objects;

public class Volume
{
    public const uint TransformSectionID = 0x2505C;
    public ulong TUID => metadata.TUID;
    public string name;
    public InstanceMetadata metadata;
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 scale;

    public Volume(StreamHelper sh, Matrix4x4 transform)
    {
        metadata = new InstanceMetadata(sh);
        name = sh.ReadString(metadata.namePointer);
        Matrix4x4.Decompose(transform, out scale, out rotation, out position);
    }
}
