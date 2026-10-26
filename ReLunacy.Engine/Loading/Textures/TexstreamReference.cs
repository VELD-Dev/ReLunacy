using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Textures;

// Old-engine streaming-texture overlay table (main.dat 0x9800). A record targets one physical
// 0x5200 texture index and points at the extra top mip stored in texstream.dat. On the confirmed
// Tools of Destruction fixture, for every consecutive record:
//
//     next.offset - offset == allocationBlocks128 * 0x80
//
// and the final record's offset + allocation matches texstream.dat's exact file size. The streamed
// image is the next higher mip (2x base width and 2x base height); tiny BC textures still receive a
// minimum 0x80-byte allocation.
[FileStructure(0x10)]
public record struct TexstreamReference : ILunaSerializable
{
    public const uint ID = 0x9800;
    public const uint Size = 0x10;
    public const uint AllocationUnit = 0x80;

    [FileOffset(0x00)] public uint offset;
    [FileOffset(0x04)] public ushort allocationBlocks128;
    [FileOffset(0x06)] public ushort index;
    [FileOffset(0x08)] public ulong Unk2;

    public readonly uint AllocationBytes => (uint)allocationBlocks128 * AllocationUnit;

    // Compatibility alias while callers/debug tooling migrate from the old unknown-field name.
    public readonly ushort Unk1 => allocationBlocks128;

    public static TexstreamReference Read(StreamHelper sh) => FileUtils.ReadStructure<TexstreamReference>(sh);

    public byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
