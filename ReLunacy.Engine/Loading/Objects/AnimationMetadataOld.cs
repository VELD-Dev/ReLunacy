using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Interfaces;

namespace ReLunacy.Engine.Loading.Objects;

/// <summary>
/// Tools of Destruction old-engine animation clip header (main.dat section 0xF000).
/// Record size is 0x40 bytes. Fields without a proven semantic name remain identified by offset.
/// </summary>
[FileStructure(0x40)]
public record struct AnimationMetadataOld : ILunaSerializable
{
    public const uint ID = 0xF000;
    public const uint Size = 0x40;

    /// <summary>Disk-local animation slot. Runtime code rewrites this while building a Moby animation set;
    /// use the clip's position in the Moby +0x24 list as the stable playback slot.</summary>
    [FileOffset(0x00)] public ushort localAnimationIndex;
    [FileOffset(0x02)] public ushort flags;
    /// <summary>Declared bone count. On additive clips it matches the number of active blend-mask bones,
    /// not the skeleton count used to lay out the control block.</summary>
    [FileOffset(0x04)] public ushort numBones;
    [FileOffset(0x06)] public ushort numFrames;
    [FileOffset(0x08)] public uint namePtr;
    [FileOffset(0x0C)] public uint Unknown0C;
    [FileOffset(0x10)] public float Unknown10;
    [FileOffset(0x14)] public float linearSpeed;
    [FileOffset(0x18)] public float frameRate;
    [FileOffset(0x1C)] public uint rootTransformPtr;
    [FileOffset(0x20)] public uint controlPtr;
    [FileOffset(0x24)] public uint framesPtr;
    [FileOffset(0x28)] public ulong Unknown28;
    /// <summary>Unaligned byte size of the control block. framesPtr-controlPtr is align128(this).</summary>
    [FileOffset(0x30)] public ushort controlByteSize;
    [FileOffset(0x32)] public ushort frameStride;
    [FileOffset(0x34)] public ushort numReferenceValues;
    [FileOffset(0x36)] public ushort num16BitTracks;
    [FileOffset(0x38)] public ushort num8BitTracks;
    [FileOffset(0x3A)] public uint Unknown3A;
    [FileOffset(0x3E)] public ushort Unknown3E;

    public readonly bool IsLooping => (flags & 0x01) != 0;
    public readonly bool IsAdditive => (flags & 0x02) != 0;
    public readonly bool IsPacked => (flags & 0x04) != 0;

    public string Name;

    public static AnimationMetadataOld Read(StreamHelper sh, uint recordBase)
    {
        sh.Seek(recordBase);
        var h = FileUtils.ReadStructure<AnimationMetadataOld>(sh);
        h.Name = h.namePtr != 0 ? sh.ReadString(h.namePtr) : string.Empty;
        return h;
    }

    public readonly byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
