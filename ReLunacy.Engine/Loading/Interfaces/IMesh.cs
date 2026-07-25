namespace ReLunacy.Engine.Loading.Interfaces;

public interface IMesh
{
    public float[] vpos { get; }
    public uint[] indices { get; }
    public float[] uvs { get; }
    public uint[] boneWeight { get; }
    public uint[] vertToBonemap { get; }
}
