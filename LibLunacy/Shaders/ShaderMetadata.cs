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
    /// Old engine: albedo, normal and expensive are offsets stored as uint
    /// </summary>
    [FileStructure(0x80)]
    public record struct ShaderMetadataOld : ILunaSerializable
    {
        public const uint ID = 0x5000;
        public const uint Size = 0x80;

        [FileOffset(0x00)] public uint albedo;
        [FileOffset(0x04)] public uint normal;
        [FileOffset(0x08)] public uint expensive;
        [FileOffset(0x0C)] [Reference(0x05)] public byte[] Unk1;
        [FileOffset(0x11)] public byte renderingMode;
        [FileOffset(0x12)] [Reference(0x0E)] public byte[] Unk2;  // includes 1 byte gap at 0x12
        [FileOffset(0x20)] public float alphaClip;
        [FileOffset(0x24)] [Reference(0x5C)] public byte[] Unk3;

        public byte renderingModeValue => renderingMode;

        public static ShaderMetadataOld Read(StreamHelper sh)
        {
            return FileUtils.ReadStructure<ShaderMetadataOld>(sh);
        }

        public byte[] ToBytes(bool isOld, params object[]? additionalParams)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// New engine: albedo, normal and expensive are indices stored as int
    /// </summary>
    [FileStructure(0x80)]
    public record struct ShaderMetadataNew : ILunaSerializable
    {
        public const uint ID = 0x5000;
        public const uint Size = 0x80;

        [FileOffset(0x00)] public int albedo;
        [FileOffset(0x04)] public int normal;
        [FileOffset(0x08)] public int expensive;
        [FileOffset(0x0C)] [Reference(0x15)] public byte[] Unk1;
        [FileOffset(0x21)] public byte renderingMode;
        [FileOffset(0x22)] [Reference(0x0E)] public byte[] Unk2;  // includes 1 byte gap at 0x22
        [FileOffset(0x30)] public float alphaClip;
        [FileOffset(0x34)] [Reference(0x4C)] public byte[] Unk3;

        public byte renderingModeValue => renderingMode;

        public static ShaderMetadataNew Read(StreamHelper sh)
        {
            return FileUtils.ReadStructure<ShaderMetadataNew>(sh);
        }

        public byte[] ToBytes(bool isOld, params object[]? additionalParams)
        {
            throw new NotImplementedException();
        }
    }
}
