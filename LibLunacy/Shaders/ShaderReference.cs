using LibLunacy.Interfaces;
using LibLunacy.Legacy;
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
    [FileStructure(0x40)]
    public record struct ShaderReference : ILunaObject, ILunaSerializable
    {
        public const uint ID = 0x5D00;
        public const uint Size = 0x40;

        public ulong TUID { get => _tuid; init => _tuid = value; }
        [FileOffset(0x00)] private ulong _tuid;
        [FileOffset(0x08)] public uint namePointer;
        [FileOffset(0x0C)] public uint Unk1;
        [FileOffset(0x10)] public uint albedoID;
        [FileOffset(0x14)] public uint normalID;
        [FileOffset(0x18)] public uint expensiveID;
        [FileOffset(0x1C)] public uint Unk2;
        [FileOffset(0x20)] public uint Unk3;
        [FileOffset(0x24)] public uint Unk4;
        [FileOffset(0x28)] public uint albedoNamePointer;
        [FileOffset(0x2C)] public uint normalNamePointer;
        [FileOffset(0x30)] public uint expensiveNamePointer;
        [FileOffset(0x34)] public uint Unk5;
        [FileOffset(0x38)] public uint Unk6;
        [FileOffset(0x3C)] public uint Unk7;

        public readonly uint TextureCount => 3;

        public static ShaderReference Read(StreamHelper sh)
        {
            return FileUtils.ReadStructure<ShaderReference>(sh);
        }

        public byte[] ToBytes(bool isOld, params object[]? additionalParams)
        {
            throw new NotImplementedException();
        }
    }
}
