using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Shaders;

/// <summary>New engine only.</summary>
[FileStructure(0x40)]
public record struct ShaderReference : ILunaObject, ILunaSerializable
{
    public const uint ID = 0x5D00;
    public const uint Size = 0x40;

    public ulong TUID { get => _tuid; init => _tuid = value; }
    [FileOffset(0x00)] private ulong _tuid;
    [FileOffset(0x08)] public uint namePointer;
    [FileOffset(0x0C)] public uint Unk1;
    // MaterialResourceNameLookupV2. Measured on 24 179 retail shader resources over A Crack in Time,
    // QForce and Into the Nexus:
    //
    //   +0x10..+0x1C  the texture ids of SLOTS 0..3, and each one is 0 exactly when the matching
    //                 0x5000 slot index is -1. Every non-zero value equals the high 32 bits of
    //                 0x5A00[slotIndex] - it is a duplicate of that, not an independent source.
    //   +0x20/+0x24   the ids of slots 4 and 5, read only when the 0x5000 slot is not -1. PROVEN in
    //                 the QForce EBOOT: the loader (0x006ACE70..0x006ACF04) builds slot i's key from
    //                 id +0x10+4i and name +0x28+4i for i = 0..5, skipping slots whose index is -1 -
    //                 which is why they hold leftovers on the 22 676 / 13 528 records whose slot is
    //                 empty.
    //   +0x28..+0x3C  six name pointers, all six inside section 0x11300 on 24 145/24 145 resources
    //                 that have one, including the pointers for unused slots.
    //
    // The AUTHORITATIVE id for any slot, auxiliaries included, is the high 32 bits of
    // 0x5A00[0x5000 slot index]; 0x5A00 holds one {u32 textureId, u32 allocationBytes} entry per
    // USED slot (count equal to the 0x5200 descriptor count on 24 145/24 145), and that texture id
    // is the low 32 bits of the texture's assetlookup 0x1D180 TUID.
    // PROVEN (QForce EBOOT): the runtime key of slot i is InsomniacCrc32(name i) << 32 | id i, and the
    // texture is found by the id first, then by the name CRC against the TUIDs' high 32 bits
    // (0x00726FA4) - 16 512 + 744 = all 17 256 QForce slots. The shader loader (0x006AD2DC) reads only
    // 0x5000, 0x5200, 0x5D00 and 0x33001-3; 0x5A00 and 0x11300 are not read there.
    [FileOffset(0x10)] public uint albedoID;
    [FileOffset(0x14)] public uint normalID;
    [FileOffset(0x18)] public uint expensiveID;
    [FileOffset(0x1C)] public uint detailID;
    [FileOffset(0x20)] public uint texture4ID;
    [FileOffset(0x24)] public uint texture5ID;
    [FileOffset(0x28)] public uint albedoNamePointer;
    [FileOffset(0x2C)] public uint normalNamePointer;
    [FileOffset(0x30)] public uint expensiveNamePointer;
    [FileOffset(0x34)] public uint detailNamePointer;
    [FileOffset(0x38)] public uint texture4NamePointer;
    [FileOffset(0x3C)] public uint texture5NamePointer;

    public readonly uint TextureCount => 6;

    public static ShaderReference Read(StreamHelper sh) => FileUtils.ReadStructure<ShaderReference>(sh);

    public byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
