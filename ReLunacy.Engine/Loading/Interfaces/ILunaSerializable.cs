namespace ReLunacy.Engine.Loading.Interfaces;

public interface ILunaSerializable
{
    /// <summary>Converts back to file-format bytes; throws if the built array doesn't match the structure's fixed size.</summary>
    public byte[] ToBytes(bool isOld, params object[]? additionalParams);
}
