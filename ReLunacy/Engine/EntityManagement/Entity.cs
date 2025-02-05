using LibLunacy.Objects.Instances;
using ReLunacy.Engine.Numerics;

namespace ReLunacy.Engine.EntityManagement;

public abstract class Entity
{
    public static ulong InstancesCount { get; protected set; } = 0;
    
    public abstract EntityType EntityType { get; init; }
    public ulong ID { get; init; }
    public string name = string.Empty;
    public bool AllowRender = true;

    public required Transform Transform { get; set; }
    public Vec4 boundingSphere;

    public static void Wipe() => InstancesCount = 0;

    public void SetPosition(Vec3 position)
    {
        Transform.Position = position;
    }
    public void SetRotation(Vec3 rotation)
    {
        Transform.SetRotation(rotation);
    }
    public void SetScale(Vec3 scale)
    {
        Transform.Scale = scale;
    }
    public void SetTransform(Mat4 mat)
    {
        Transform.Matrix = mat;
    }
    public void Draw()
    {
        if (!AllowRender) return;
        if (drawable is DrawableListList dll) dll.Draw(Transform);
        else if (drawable is DrawableList dl) dl.Draw(Transform);
        else if (drawable is Drawable d) d.Draw(Transform);
    }

    // It's shaky, I must consolidate that but it works !
    public void AddWireframeDrawCall()
    {
        if (!AllowRender) return;
        LunaLog.LogDebug("Wireframe Drawcall added");
        if (drawable is DrawableListList dll) dll.AddDrawCallWireframe(Transform, ID);
        else if (drawable is DrawableList dl) dl.AddDrawCallWireframe(Transform, ID);
        else if (drawable is Drawable d) d.AddDrawCallWireframe(Transform, ID);
    }
    public void RemoveWireframeDrawCall()
    {
        if (drawable is DrawableListList dll) dll.RemoveDrawCallWireframe(ID);
        else if (drawable is DrawableList dl) dl.RemoveDrawCallWireframe(ID);
        else if (drawable is Drawable d) d.RemoveDrawCallWireframe(ID);
    }
    // /////////////////////////////////////////////// //
 
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
}
