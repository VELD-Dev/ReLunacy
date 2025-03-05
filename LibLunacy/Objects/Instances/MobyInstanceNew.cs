using LibLunacy.Interfaces;
using LibLunacy.Numerics;

namespace LibLunacy.Objects.Instances
{
    public record struct MobyInstanceNew : ILunaSerializable, IMobyInstance
    {
        public const uint ID = 0x25048;
        public const uint Size = 0x50;

        public ushort mobyIndex; // Unknown
        public ushort groupIndex; // Unknown
        public byte[] Unk1;
        public float minRenderDistance;
        public float maxRenderDistance;
        public Vec3 position;
        public Vec3 rotation;
        public float scale;
        public byte[] Unk2;

        public Vec3 Position { readonly get => position; set => position = value; }
        public Vec3 Rotation { readonly get => rotation; set => rotation = value; }
        public float Scale { readonly get =>  scale; set => scale = value; }
        public ushort MobyIndex { readonly get => mobyIndex; set => mobyIndex = value; }


        public MobyInstanceNew(LunaStream stream)
        {
            mobyIndex = stream.ReadUInt16(0x00);
            groupIndex = stream.ReadUInt16(0x02);
            Unk1 = stream.Peek(0x04, 0x10);
            position = stream.ReadVec3(0x14);
            rotation = stream.ReadVec3(0x20);
            scale = stream.ReadSingle(0x2C);
            Unk2 = stream.Peek(0x30, 0x20);
        }

        public byte[] ToBytes(bool isOld, params object[]? additionalParams)
        {
            throw new NotImplementedException();
        }
    }
}
