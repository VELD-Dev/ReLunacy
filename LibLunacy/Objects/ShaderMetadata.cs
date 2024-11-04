using LibLunacy.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Objects
{
    /// <summary>
    /// On new engine, albedo, normal and expensive are indices stored as int but always positive.<br/>
    /// On old engine, albedo, normal and expensive are offsets stored as uint and obviously,<br/>
    /// absolute and always positive (it's unsigned)
    /// </summary>
    public record struct ShaderMetadata : ILunaSerializable
    {
        public const uint ID = 0x5000;
        public const uint Size = 0x80;
        public uint albedo;
        public uint normal;
        public uint expensive;
        public byte[] Unk1;
        public byte renderingMode;
        public byte[] Unk2;
        public float alphaClip;
        public byte[] Unk3;

        public readonly bool isOld;

        public ShaderMetadata(LunaStream stream, bool old)
        {
            isOld = old;

            if(isOld)
            {
                albedo = stream.ReadUInt32(0x00);
                normal = stream.ReadUInt32(0x04);
                expensive = stream.ReadUInt32(0x08);
                Unk1 = stream.Peek(0x0C, 0x11 - 0x0C);
                renderingMode = stream.Peek(0x11, 1)[0];
                Unk2 = stream.Peek(0x13, 0x20 - 0x13);
                alphaClip = stream.ReadSingle(0x20);
                Unk3 = stream.Peek(0x24, 0x80 - 0x24);
            }
            else
            {
                albedo = (uint)stream.ReadInt32(0x00);
                normal = (uint)stream.ReadInt32(0x04);
                expensive = (uint)stream.ReadInt32(0x08);
                Unk1 = stream.Peek(0x0C, 0x21 - 0x0C);
                renderingMode = stream.Peek(0x21, 1)[0];
                Unk2 = stream.Peek(0x23, 0x30 - 0x23);
                alphaClip = stream.ReadSingle(0x30);
                Unk3 = stream.Peek(0x34, 0x80 - 0x34);
            }
        }

        public byte[] ToBytes(bool isOld, params object[]? additionalParams)
        {
            throw new NotImplementedException();
        }
    }
}
