using LibLunacy.Interfaces;
using LibLunacy.Legacy;
using LibLunacy.Numerics;

namespace LibLunacy.Objects.Instances
{
    [FileStructure(0x50)]
    public record struct MobyInstanceNew : ILunaSerializable, IMobyInstance
    {
        public const uint ID = 0x25048;
        public const uint Size = 0x50;

        [FileOffset(0x00)] public ushort mobyIndex;
        [FileOffset(0x02)] public ushort groupIndex;
        [FileOffset(0x04)] [Reference(0x10)] public byte[] Unk1;
        [FileOffset(0x14)] public Vec3 position;
        [FileOffset(0x20)] public Vec3 rotation;
        [FileOffset(0x2C)] public float scale;
        [FileOffset(0x30)] [Reference(0x20)] public byte[] Unk2;

        public Vec3 Position { readonly get => position; set => position = value; }
        public Vec3 Rotation { readonly get => rotation; set => rotation = value; }
        public float Scale { readonly get =>  scale; set => scale = value; }
        public ushort MobyIndex { readonly get => mobyIndex; set => mobyIndex = value; }

        public static MobyInstanceNew Read(StreamHelper sh)
        {
            return FileUtils.ReadStructure<MobyInstanceNew>(sh);
        }

        public byte[] ToBytes(bool isOld, params object[]? additionalParams)
        {
            throw new NotImplementedException();
        }
    }
}
