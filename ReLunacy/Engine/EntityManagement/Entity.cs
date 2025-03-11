using ReLunacy.Engine.Rendering.Alister;

namespace ReLunacy.Engine.EntityManagement;

public abstract class Entity : IRenderable, IDisposable
{
    private static uint entityCount = 1;
    public Model Model { get; set; }
    public Transform Transform { get; set; }
    
    public abstract EntityType EntityType { get; init; }
    public ulong ID { get; init; }
    public uint InternalID { get; set; }
    public string name = string.Empty;
    public bool AllowRender = true;
    public bool Selected = false;

    public Entity()
    {
        InternalID = entityCount++;
    }

    public float[] Vertices { get => Model?.Vertices ?? []; set
        {
            if (Model == null) return;
            Model.Vertices = value;
        }
    }
    public uint[] Indices { get => Model?.Indices ?? []; set
        {
            if(Model == null) return;
            Model.Indices = value;
        }
    }

    public Vec4 boundingSphere;

    public void SetPosition(Vec3 position) => Transform.Position = position;
    public void SetRotation(Vec3 rotation) => Transform.SetRotation(rotation);
    public void SetScale(Vec3 scale) => Transform.Scale = scale;
    public void SetTransform(Mat4 mat) =>Transform.Matrix = mat;

    public void Draw()
    {
        if (!AllowRender) return;
        Model.Draw(Transform, Selected);
    }
 
    public bool IntersectsRay(Vec3 dir, Vec3 position, out float distance)
    {
        Vec3 localPos = position - boundingSphere.XYZ;
        float b = Vec3.Dot(localPos, dir);
        float c = Vec3.Dot(localPos, localPos) - boundingSphere.W * boundingSphere.W;
        distance = float.NaN;

        if (c > 0 && b > 0) return false;

        float discriminant = b * b - c;
        distance = (b * b - c);

        return discriminant >= 0;
    }
    public bool IntersectsRay(Vec3 dir, Vec3 position)
    {
        return IntersectsRay(dir, position, out _);
    }

    public void Dispose()
    {
        Model.Dispose();
    }

    public static void ResetIDCount()
    {
        entityCount = 1;
    }
}
