using LibLunacy.Numerics;

namespace LibLunacy.Interfaces
{
    public interface IMobyInstance
    {
        public Vec3 Position { get; set; }
        public Vec3 Rotation { get; set; }
        public float Scale { get; set; }
        public ushort MobyIndex { get; set; }
    }
}
