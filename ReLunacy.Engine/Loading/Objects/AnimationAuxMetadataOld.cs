using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Objects;

/// <summary>
/// Tools of Destruction old-engine animation auxiliary record (main.dat F400).
/// F400 is parallel to F000 in the audited fixtures: one 0x30-byte record per F000 clip.
/// D100 +0x80 points at the first F400 record for a Moby animation list.
/// </summary>
[FileStructure(0x30)]
public record struct AnimationAuxMetadataOld : ILunaSerializable
{
    public const uint ID = 0xF400;
    public const uint Size = 0x30;

    [FileOffset(0x00)] public uint Unknown00;
    [FileOffset(0x04)] public uint Unknown04;
    [FileOffset(0x08)] public uint Unknown08;

    /// <summary>Observed as F000.linearSpeed * F000.frameRate in audited clips.</summary>
    [FileOffset(0x0C)] public float linearSpeedTimesFrameRate;

    /// <summary>Observed as (F000.numFrames - 1) / F000.frameRate when frameRate is non-zero.</summary>
    [FileOffset(0x10)] public float lastFrameTimeSeconds;

    /// <summary>Observed as F000.numFrames / F000.frameRate when frameRate is non-zero.</summary>
    [FileOffset(0x14)] public float durationSeconds;

    /// <summary>Count consumed by the native F400/F500 initialization path.</summary>
    [FileOffset(0x18)] public uint frameAuxCount;

    /// <summary>Absolute main.dat pointer into F500 when frameAuxCount is non-zero.</summary>
    [FileOffset(0x1C)] public uint frameAuxPointer;

    [FileOffset(0x20)] public uint Unknown20;
    [FileOffset(0x24)] public uint Unknown24;
    [FileOffset(0x28)] public uint Unknown28;
    [FileOffset(0x2C)] public uint Unknown2C;

    public static AnimationAuxMetadataOld Read(StreamHelper sh, uint recordBase)
    {
        sh.Seek(recordBase);
        return FileUtils.ReadStructure<AnimationAuxMetadataOld>(sh);
    }

    public readonly byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
