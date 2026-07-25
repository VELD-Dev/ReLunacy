using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Assets.LevelElements;

public class Volume : IVolumeData
{
    public ulong Id { get; init; }
    public string? Name { get; set; }
    public Matrix4x4 transform;
    public bool IsLoaded { get; set; }
    public ushort group { get; set; }

    public Volume(ulong id, Matrix4x4 transform, string? name = null, ushort group = 0)
    {
        Id = id;
        Name = name;
        this.transform = transform;
        IsLoaded = true;
        this.group = group;
    }
}
