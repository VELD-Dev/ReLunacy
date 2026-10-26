using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Objects;

/// <summary>On-disk bone record (8 bytes).</summary>
[FileStructure(0x08)]
public record struct MobyBone : ILunaSerializable
{
    public const uint Size = 0x08;

    [FileOffset(0x00)] public ushort flags;
    /// <summary>Index into the owning skeleton's bone array; negative (-1) marks the root bone.</summary>
    [FileOffset(0x02)] public short parentIndex;
    [FileOffset(0x04)] public ushort child;
    [FileOffset(0x06)] public ushort sibling;

    public static MobyBone Read(StreamHelper sh) => FileUtils.ReadStructure<MobyBone>(sh);

    public readonly byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
