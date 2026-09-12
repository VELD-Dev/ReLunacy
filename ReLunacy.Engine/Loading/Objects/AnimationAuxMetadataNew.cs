using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Objects;

/// <summary>New-engine animset F400 record. Only cross-file invariants are named.</summary>
[FileStructure(0x20)]
public record struct AnimationAuxMetadataNew : ILunaSerializable
{
    public const uint ID = 0xF400;
    public const uint Size = 0x20;

    [FileOffset(0x00)] public uint Unknown00;
    [FileOffset(0x04)] public uint Unknown04;
    [FileOffset(0x08)] public uint Unknown08;
    [FileOffset(0x0C)] public float linearSpeedTimesFrameRate;
    [FileOffset(0x10)] public float auxiliaryLastSampleTime;
    [FileOffset(0x14)] public float auxiliaryDuration;
    [FileOffset(0x18)] public uint Unknown18;
    [FileOffset(0x1C)] public uint Unknown1C;

    public static AnimationAuxMetadataNew Read(StreamHelper sh, uint recordBase)
    {
        sh.Seek(recordBase);
        return FileUtils.ReadStructure<AnimationAuxMetadataNew>(sh);
    }

    public readonly byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
