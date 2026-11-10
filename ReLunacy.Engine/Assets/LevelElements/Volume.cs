using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Assets.LevelElements;

/// <summary>A trigger volume's shape: the top byte of the last word of its new-engine record (gp_prius
/// 0x2505C +0x3C, the matrix's M44 slot). Each is the unit shape the matrix scales, like the box.</summary>
public enum VolumeShape : byte
{
    Box = 0,
    /// <summary>Along local Y: the elevator shafts and towers, X and Z equal on nearly all of them.</summary>
    Cylinder = 1,
    Sphere = 2,
    /// <summary>8 in all four games. Taken along local Y like the cylinder; the axis is not confirmed.</summary>
    Capsule = 3,
}

public class Volume : IVolumeData
{
    public ulong Id { get; init; }
    public string? Name { get; set; }
    public Matrix4x4 transform;
    public bool IsLoaded { get; set; }
    public ushort group { get; set; }
    public VolumeShape Shape { get; init; }

    public Volume(ulong id, Matrix4x4 transform, string? name = null, ushort group = 0, VolumeShape shape = VolumeShape.Box)
    {
        Shape = shape;
        Id = id;
        Name = name;
        this.transform = transform;
        IsLoaded = true;
        this.group = group;
    }
}
