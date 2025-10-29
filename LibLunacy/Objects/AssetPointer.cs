using LibLunacy.Interfaces;
using LibLunacy.Legacy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Objects;

public record struct AssetPointer : ILunaObject, ILunaSerializable
{
    public const uint Size = 0x10;

    public ulong TUID { get; init; }
    public uint offset;
    public uint length;

    public AssetPointer(StreamHelper sh)
    {
        TUID =      sh.ReadUInt64(0x00);
        offset =    sh.ReadUInt32(0x08);
        length =    sh.ReadUInt32(0x0C);
    }

    public byte[] ToBytes(bool isOld, params object[]? additionalParams)
    {
        throw new NotImplementedException();
    }
}
