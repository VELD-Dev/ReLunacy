using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Objects;

/// <summary>
/// Tools of Destruction old-engine F500 animation child record, 0x14 bytes.
/// F400 +0x18/+0x1C owns contiguous lists of these records. The exact per-field semantics are still
/// under reverse, so fields remain identified by offset.
/// </summary>
[FileStructure(0x14)]
public record struct AnimationFrameAuxOld : ILunaSerializable
{
    public const uint ID = 0xF500;
    public const uint Size = 0x14;

    [FileOffset(0x00)] public uint Unknown00;
    [FileOffset(0x04)] public uint Unknown04;
    [FileOffset(0x08)] public float Unknown08;
    [FileOffset(0x0C)] public float Unknown0C;
    [FileOffset(0x10)] public uint Unknown10;

    public static AnimationFrameAuxOld Read(StreamHelper sh, uint recordBase)
    {
        sh.Seek(recordBase);
        return FileUtils.ReadStructure<AnimationFrameAuxOld>(sh);
    }

    public readonly byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
