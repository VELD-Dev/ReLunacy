using LibLunacy.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Objects.Instances
{
    public record struct MobyInstanceNew : ILunaSerializable, IMobyInstance
    {
        public const uint ID = 0x25048;
        public const uint Size = 0x50;

        public ushort mobyIndex;
        public ushort groupIndex;
        public byte[] Unk1;
        public Vector3 position;
        public Vector3 rotation;
        public float scale;
        public byte[] Unk2;

        public Vector3 Position { readonly get => position; set => position = value; }
        public Vector3 Rotation { readonly get => rotation; set => rotation = value; }
        public float Scale { readonly get =>  scale; set => scale = value; }
        public ushort MobyIndex { readonly get => mobyIndex; set => mobyIndex = value; }


        public MobyInstanceNew(LunaStream stream)
        {
            mobyIndex = stream.ReadUInt16(0x00);
            groupIndex = stream.ReadUInt16(0x02);
            Unk1 = stream.Peek(0x04, 0x10);
            position = stream.ReadVector3(0x14);
            rotation = stream.ReadVector3(0x20);
            scale = stream.ReadSingle(0x2C);
            Unk2 = stream.Peek(0x30, 0x20);
        }

        public byte[] ToBytes(bool isOld, params object[]? additionalParams)
        {
            throw new NotImplementedException();
        }
    }
}
