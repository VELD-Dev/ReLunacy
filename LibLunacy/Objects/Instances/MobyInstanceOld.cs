using LibLunacy.Interfaces;
using LibLunacy.Numerics;

namespace LibLunacy.Objects.Instances
{
    public record struct MobyInstanceOld : ILunaSerializable, IMobyInstance
    {
        public const uint ID = 0x7340;
        public const uint Size = 0x48;

        public byte[] Unk1;
        public Vec3 position;
        public Vec3 rotation;
        public float scale;
        public ulong Unk2;
        public ushort mobyIndex;
        public ushort Unk3;
        public ulong Unk4;

        public Vec3 Position { get => position; set => position = value; }
        public Vec3 Rotation { get => rotation; set => rotation = value; }
        public float Scale { get => scale; set => scale = value; }
        public ushort MobyIndex { get => mobyIndex; set => mobyIndex = value; }


        public MobyInstanceOld(LunaStream stream)
        {
            Unk1 = stream.Peek(0x00, 0x18);
            position = stream.ReadVector3(0x18);
            rotation = stream.ReadVector3(0x24);
            scale = stream.ReadSingle(0x30);
            Unk2 = stream.ReadUInt64(0x34);
            mobyIndex = stream.ReadUInt16(0x3C);
            Unk3 = stream.ReadUInt16(0x3E);
            Unk4 = stream.ReadUInt64(0x40);
        }

        public byte[] ToBytes(bool isOld, params object[]? additionalParams)
        {
            throw new NotImplementedException();
        }
    }
}
