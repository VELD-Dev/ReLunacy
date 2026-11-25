using System.Numerics;
using ReLunacy.Engine.Assets.Water;
using ReLunacy.Engine.Rendering.Resources;

namespace ReLunacy.Engine.Scene;

/// <summary>
/// One water record of a zone (0x6641), as a selectable entity. Read-only: no save path writes water.
///
/// It has no renderables of its own: the water is drawn by VulkanRenderer.Water.cs from the list the view hands it,
/// and picked there by its <see cref="Entity.ID"/>. The file names no body, so the entity is named by its zone, its
/// body (<see cref="WaterSurface.Bodies"/>) and, for a body of several rectangles, its part.
/// </summary>
public sealed class EntityWater : Entity
{
    public override Vector4 BoundingSphere { get; set; }
    public override string Name { get; protected set; }
    public override bool IsReadOnly => true;

    /// <summary>The record this entity stands for. A zone listed by several regions gives several entities over the
    /// same record; the view draws each record once (LevelWaterView).</summary>
    public WaterSurface Surface { get; }

    public string ZoneName { get; internal set; }

    /// <summary>The body this record is part of, numbered within its zone.</summary>
    public int Body { get; }

    /// <summary>This record's place in its body, and how many records the body has.</summary>
    public int Part { get; }
    public int PartCount { get; }

    public EntityWater(WaterSurface surface, string zoneName, int body, int part, int partCount)
    {
        Surface = surface;
        ZoneName = zoneName;
        Body = body;
        Part = part;
        PartCount = partCount;
        Name = partCount > 1 ? $"Water_{body}_{part}" : $"Water_{body}";

        // Placed at the rectangle's centre, so the inspector and "go to" have a position; the sphere is local to it.
        Transform = new Transform { Translation = surface.Centre };
        float radius = new Vector2(surface.Size.X, surface.Size.Y).Length() * 0.5f;
        BoundingSphere = new Vector4(Vector3.Zero, radius);
    }

    /// <summary>The rectangle's four corners at the water's height, in order round its edge.</summary>
    public Vector3[] Outline()
    {
        Vector3 c = Surface.Corner;
        var x = new Vector3(Surface.Size.X, 0f, 0f);
        var z = new Vector3(0f, 0f, Surface.Size.Y);
        return [c, c + x, c + x + z, c + z];
    }
}
