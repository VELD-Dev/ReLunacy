using LibLunacy.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Shaders
{
    /// <summary>
    /// New engine only
    /// </summary>
    public record struct ShaderReference : ILunaObject, ILunaSerializable
    {
        public const uint ID = 0x5D00;
        public const uint Size = 0x40;

        public ulong TUID { get; init; }
        public uint namePointer;
        public uint Unk1;
        public uint albedoID;
        public uint normalID;
        public uint expensiveID;
        public uint Unk2;
        public uint Unk3;
        public uint Unk4;
        public uint albedoNamePointer;
        public uint normalNamePointer;
        public uint expensiveNamePointer;
        public uint Unk5;
        public uint Unk6;
        public uint Unk7;

        public readonly uint TextureCount => 3;

        public ShaderReference(LunaStream stream)
        {
            TUID = stream.ReadUInt64(0x00);
            namePointer = stream.ReadUInt32(0x08);
            Unk1 = stream.ReadUInt32(0x0C);
            albedoID = stream.ReadUInt32(0x10);
            normalID = stream.ReadUInt32(0x14);
            expensiveID = stream.ReadUInt32(0x18);
            Unk2 = stream.ReadUInt32(0x1C);
            Unk3 = stream.ReadUInt32(0x20);
            Unk4 = stream.ReadUInt32(0x24);
            albedoNamePointer = stream.ReadUInt32(0x28);
            normalNamePointer = stream.ReadUInt32(0x2C);
            expensiveNamePointer = stream.ReadUInt32(0x30);
            Unk5 = stream.ReadUInt32(0x34);
            Unk6 = stream.ReadUInt32(0x38);
            Unk7 = stream.ReadUInt32(0x3C);
        }

        public byte[] ToBytes(bool isOld, params object[]? additionalParams)
        {
            throw new NotImplementedException();
        }
    }
}
