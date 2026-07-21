using System.Numerics;
using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Objects;

[FileStructure(0xC0)]
public record struct OldMoby : IMoby
{
    public const uint ID = 0xD100;
    public const uint Size = 0xC0;

    [FileOffset(0x00)] public Vector4 boundingSphere;
    [FileOffset(0x10)] public ushort Unk1;
    [FileOffset(0x12)] public ushort Unk2;
    [FileOffset(0x14)] public ushort bonesCount;
    [FileOffset(0x16)] public ushort Unk3;
    [FileOffset(0x18)] public ushort bangleCount;
    [FileOffset(0x1A)] public ushort mobyId;
    [FileOffset(0x1C)] public ushort Null1;
    [FileOffset(0x1E)] public byte UnkBool;
    [FileOffset(0x1F)] public byte Null2;
    [FileOffset(0x20)] public uint skeletonPointer;
    [FileOffset(0x24)] public uint UnkPointer1;
    [FileOffset(0x28), Reference(nameof(BangleCount))] public MobyBangle[] mobyBangles;
    [FileOffset(0x2C)] public uint UnkPointer2;
    [FileOffset(0x30)] public uint Null3;
    [FileOffset(0x34)] public uint indicesOffset;
    [FileOffset(0x38)] public uint verticesOffset;
    [FileOffset(0x3C)] public float scale;

    public readonly uint BangleCount => bangleCount;

    private ulong _tuid;
    public ulong TUID { readonly get => _tuid; init => _tuid = value; }

    public MobyBangle[] bangles { readonly get => mobyBangles; set => mobyBangles = value; }

    public static OldMoby Read(StreamHelper sh, int index)
    {
        var moby = FileUtils.ReadStructure<OldMoby>(sh);
        moby._tuid = (ulong)index;
        return moby;
    }

    public byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
