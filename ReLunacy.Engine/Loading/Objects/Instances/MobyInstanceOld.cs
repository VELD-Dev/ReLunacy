using System.Numerics;
using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Objects.Instances;

[FileStructure(0x48)]
public record struct MobyInstanceOld : ILunaSerializable, IMobyInstance
{
    public const uint ID = 0x7340;
    public const uint Size = 0x48;

    [FileOffset(0x00), Reference(0x18)] public byte[] Unk1;
    [FileOffset(0x18)] public Vector3 position;
    [FileOffset(0x24)] public Vector3 rotation;
    [FileOffset(0x30)] public float scale;
    [FileOffset(0x34)] public ulong Unk2;
    [FileOffset(0x3C)] public ushort mobyIndex;
    [FileOffset(0x3E)] public ushort Unk3;
    [FileOffset(0x40)] public ulong Unk4;

    public Vector3 Position { get => position; set => position = value; }
    public Vector3 Rotation { get => rotation; set => rotation = value; }
    public float Scale { get => scale; set => scale = value; }
    public ushort MobyIndex { get => mobyIndex; set => mobyIndex = value; }

    public static MobyInstanceOld Read(StreamHelper sh) => FileUtils.ReadStructure<MobyInstanceOld>(sh);

    public byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
