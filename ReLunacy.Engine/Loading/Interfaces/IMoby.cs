using ReLunacy.Engine.Loading.Objects;

namespace ReLunacy.Engine.Loading.Interfaces;

public interface IMoby : ILunaObject, ILunaSerializable
{
    public MobyBangle[] bangles { get; set; }
}
