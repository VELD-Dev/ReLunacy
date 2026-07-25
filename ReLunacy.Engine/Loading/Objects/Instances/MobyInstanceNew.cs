using System.Numerics;
using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Objects.Instances;

[FileStructure(0x50)]
public record struct MobyInstanceNew : ILunaSerializable, IMobyInstance
{
    public const uint ID = 0x25048;
    public const uint Size = 0x50;

    [FileOffset(0x00)] public ushort mobyIndex;
    [FileOffset(0x02)] public ushort groupIndex;
    [FileOffset(0x04)] public float displayDist;
    [FileOffset(0x08)] public float updateDist;
    [FileOffset(0x0C), Reference(0x08)] public byte[] Unk1;
    [FileOffset(0x14)] public Vector3 position;
    [FileOffset(0x20)] public Vector3 rotation;
    [FileOffset(0x2C)] public float scale;
    [FileOffset(0x30), Reference(0x20)] public byte[] Unk2;

    public Vector3 Position { readonly get => position; set => position = value; }
    public Vector3 Rotation { readonly get => rotation; set => rotation = value; }
    public float Scale { readonly get => scale; set => scale = value; }
    public ushort MobyIndex { readonly get => mobyIndex; set => mobyIndex = value; }
    public float DisplayDistance { readonly get => displayDist; set => displayDist = value; }
    public float UpdateDistance { readonly get => updateDist; set => updateDist = value; }

    public static MobyInstanceNew Read(StreamHelper sh) => FileUtils.ReadStructure<MobyInstanceNew>(sh);

    public byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
