using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Meshes;

namespace ReLunacy.Engine.Loading.Objects;

[FileStructure(0x08)]
public record struct MobyBangle : ILunaSerializable
{
    public const uint Size = 0x08;

    [FileOffset(0x00), Reference(nameof(MeshesCount))] public MobyMesh[] meshes;
    [FileOffset(0x04)] public uint meshesCount;

    public readonly uint MeshesCount => meshesCount;

    public readonly byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
