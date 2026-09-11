using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Objects;

/// <summary>New-engine animset F000 record. Unknown fields remain named by offset.</summary>
[FileStructure(0x60)]
public record struct AnimationMetadataNew : ILunaSerializable
{
    public const uint ID = 0xF000;
    public const uint Size = 0x60;

    [FileOffset(0x00)] public ushort localAnimationIndex;
    [FileOffset(0x02)] public ushort flags;
    [FileOffset(0x04)] public ushort numBones;
    [FileOffset(0x06)] public ushort numFrames;
    [FileOffset(0x08)] public uint namePtr;
    [FileOffset(0x0C)] public uint frameRemapPtr;
    [FileOffset(0x10)] public uint Unknown10;
    [FileOffset(0x14)] public float linearSpeed;
    [FileOffset(0x18)] public float frameRate;
    [FileOffset(0x1C)] public uint Unknown1C;
    [FileOffset(0x20)] public uint controlPtr;
    [FileOffset(0x24)] public uint framesPtr;
    [FileOffset(0x28)] public ulong Unknown28;
    [FileOffset(0x30)] public ushort controlByteSize;
    [FileOffset(0x32)] public ushort frameStride;
    [FileOffset(0x34)] public ushort numReferenceValues;
    [FileOffset(0x36)] public ushort num16BitTracks;
    [FileOffset(0x38)] public ushort num8BitTracks;
    [FileOffset(0x3A)] public ushort auxiliarySampleCount;
    [FileOffset(0x3C)] public uint Unknown3C;
    [FileOffset(0x40)] public uint Unknown40;
    [FileOffset(0x44)] public uint Unknown44;
    [FileOffset(0x48)] public uint Unknown48;
    [FileOffset(0x4C)] public float Unknown4C;
    [FileOffset(0x50)] public uint Unknown50;
    [FileOffset(0x54)] public uint Unknown54;
    [FileOffset(0x58)] public uint Unknown58;
    [FileOffset(0x5C)] public float Unknown5C;

    public string Name;

    public readonly bool IsLooping => (flags & 0x01) != 0;
    public readonly bool IsAdditive => (flags & 0x02) != 0;
    public readonly bool IsPacked => (flags & 0x04) != 0;

    public static AnimationMetadataNew Read(StreamHelper sh, uint recordBase)
    {
        sh.Seek(recordBase);
        var value = FileUtils.ReadStructure<AnimationMetadataNew>(sh);
        value.Name = value.namePtr != 0 ? sh.ReadString(value.namePtr) : string.Empty;
        return value;
    }

    public readonly byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
