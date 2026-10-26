using System.Numerics;
using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Objects;

/// <summary>
/// New-engine animset F990 record. The first 0x40 bytes are a matrix and +0x40/+0x44 point to the
/// parallel F000/F400 records. Remaining fields stay offset-named until their native consumers are traced.
/// </summary>
[FileStructure(0x80)]
public record struct AnimationAuxBlockNew : ILunaSerializable
{
    public const uint ID = 0xF990;
    public const uint Size = 0x80;

    [FileOffset(0x00)] public Matrix4x4 matrix;
    [FileOffset(0x40)] public uint f000Pointer;
    [FileOffset(0x44)] public uint f400Pointer;
    [FileOffset(0x48)] public uint fc00Pointer;
    [FileOffset(0x4C)] public uint fd00Pointer;
    [FileOffset(0x50)] public uint Unknown50;
    [FileOffset(0x54)] public uint Unknown54;
    [FileOffset(0x58)] public uint Unknown58;
    [FileOffset(0x5C)] public uint Unknown5C;
    [FileOffset(0x60)] public uint Unknown60;
    [FileOffset(0x64)] public uint Unknown64;
    [FileOffset(0x68)] public uint Unknown68;
    [FileOffset(0x6C)] public uint Unknown6C;
    [FileOffset(0x70)] public uint Unknown70;
    [FileOffset(0x74)] public uint Unknown74;
    [FileOffset(0x78)] public uint Unknown78;
    [FileOffset(0x7C)] public uint Unknown7C;

    public static AnimationAuxBlockNew Read(StreamHelper sh, uint recordBase)
    {
        sh.Seek(recordBase);
        return FileUtils.ReadStructure<AnimationAuxBlockNew>(sh);
    }

    public readonly byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
