namespace ReLunacy.Engine.Assets.Interfaces;

public interface IMesh
{
    IGeometry Geometry { get; }
    IMaterial Material { get; }
    string? Name { get; }
}
