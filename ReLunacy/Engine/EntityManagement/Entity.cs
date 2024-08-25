using Vec3 = OpenTK.Mathematics.Vector3;
using Vector3 = System.Numerics.Vector3;
using Vec4 = OpenTK.Mathematics.Vector4;
using Vector4 = System.Numerics.Vector4;

namespace ReLunacy.Engine.EntityManagement;

public class Entity
{
    public static ulong InstancesCount { get; private set; } = 0;
    public object instance;                 //Is either a Region.CMobyInstance or a TieInstance depending on if it's a moby or tie repsectively
    public object drawable;                 //Is either a DrawableListList or a DrawableList depending on if it's a moby or tie respectively
    public ulong id;
    public string name = string.Empty;
    public bool AllowRender = true;

    public Transform transform;

    //xyz is pos, w is radius
    public Vector4 boundingSphere;

    public Entity(Region.CMobyInstance mobyInstance)
    {
        instance = mobyInstance;
        drawable = AssetManager.Singleton.Mobys[mobyInstance.moby.id];
        id = InstancesCount;
        InstancesCount++;
        transform = new Transform(
            mobyInstance.position,
            mobyInstance.rotation,
            Vector3.One * mobyInstance.scale
        );
        name = mobyInstance.name;
        ((DrawableListList)drawable).AddDrawCall(transform, id);
        boundingSphere = new(mobyInstance.moby.boundingSpherePosition + transform.position, mobyInstance.moby.boundingSphereRadius * mobyInstance.scale);
    }
    public Entity(Region.CVolumeInstance volumeInstance)
    {
        instance = volumeInstance;
        drawable = AssetManager.Singleton.Cube;
        id = InstancesCount;
        InstancesCount++;
        transform = new Transform(
            volumeInstance.position,
            new(0),
            volumeInstance.scale
        );
        transform.SetRotation(volumeInstance.rotation.ToOpenTK());
        name = volumeInstance.name;
        ((Drawable)drawable).AddDrawCall(transform, id);
        boundingSphere = new(volumeInstance.position, volumeInstance.scale.Length());
    }
    public Entity(CZone.CTieInstance tieInstance)
    {
        instance = tieInstance;
        drawable = AssetManager.Singleton.Ties[tieInstance.tie.id];
        id = InstancesCount;
        InstancesCount++;
        transform = new Transform(tieInstance.transformation.ToOpenTK());
        name = tieInstance.name;
        ((DrawableList)drawable).AddDrawCall(transform, id);
        boundingSphere = new(tieInstance.boundingPosition, tieInstance.boundingRadius);
    }
    public Entity(CZone.UFrag ufrag)
    {
        instance = ufrag;
        id = InstancesCount;
        InstancesCount++;
        drawable = AssetManager.Singleton.UFrags[ufrag.GetTuid()];
        name = $"UFrag_{ufrag.GetTuid():X08}";
        if(ufrag is CZone.OldUFrag)
        {
            transform = new Transform(ufrag.GetPosition() / 0x100, Vector3.Zero, Vector3.One / 0x100);
        }
        else
        {
            transform = new Transform(ufrag.GetPosition(), Vector3.Zero, Vector3.One / 0x100);
        }
        boundingSphere = ufrag.GetBoundingSphere();
        ((Drawable)drawable).AddDrawCall(transform, id);
        ((Drawable)drawable).ConsolidateDrawCalls();
    }

    public static void Wipe() => InstancesCount = 0;

    public void SetPosition(Vector3 position)
    {
        transform.position = position;
        if (drawable is DrawableListList dll) dll.UpdateTransform(transform, id);
        else if (drawable is DrawableList dl) dl.UpdateTransform(transform, id);
        else if (drawable is Drawable d) d.UpdateTransform(transform, id);
    }
    public void SetRotation(Vector3 rotation)
    {
        transform.SetRotation(rotation);
        if (drawable is DrawableListList dll) dll.UpdateTransform(transform, id);
        else if (drawable is DrawableList dl) dl.UpdateTransform(transform, id);
        else if (drawable is Drawable d) d.UpdateTransform(transform, id);
    }
    public void SetScale(Vector3 scale)
    {
        transform.scale = scale;
        if (drawable is DrawableListList dll) dll.UpdateTransform(transform, id);
        else if (drawable is DrawableList dl) dl.UpdateTransform(transform, id);
        else if (drawable is Drawable d) d.UpdateTransform(transform, id);
    }
    public void UpdateTransform()
    {
        if (drawable is DrawableListList dll) dll.UpdateTransform(transform, id);
        else if (drawable is DrawableList dl) dl.UpdateTransform(transform, id);
        else if (drawable is Drawable d) d.UpdateTransform(transform, id);
    }
    public void Draw()
    {
        if (!AllowRender) return;
        if (drawable is DrawableListList dll) dll.Draw();
        else if (drawable is DrawableList dl) dl.Draw();
        else if (drawable is Drawable d) d.Draw(transform);
    }

    // It's shaky, I must consolidate that but it works !
    public void AddWireframeDrawCall()
    {
        if (!AllowRender) return;
        LunaLog.LogDebug("Wireframe Drawcall added");
        if (drawable is DrawableListList dll) dll.AddDrawCallWireframe(transform, id);
        else if (drawable is DrawableList dl) dl.AddDrawCallWireframe(transform, id);
        else if (drawable is Drawable d) d.AddDrawCallWireframe(transform, id);
    }
    public void RemoveWireframeDrawCall()
    {
        if (drawable is DrawableListList dll) dll.RemoveDrawCallWireframe(id);
        else if (drawable is DrawableList dl) dl.RemoveDrawCallWireframe(id);
        else if (drawable is Drawable d) d.RemoveDrawCallWireframe(id);
    }
    // /////////////////////////////////////////////// //
 
    public bool IntersectsRay(Vec3 dir, Vec3 position, out float distance)
    {
        Vec3 localPos = position - boundingSphere.ToOpenTK().Xyz;
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
