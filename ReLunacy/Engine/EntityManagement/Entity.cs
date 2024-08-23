using Vec3 = OpenTK.Mathematics.Vector3;
using Vec4 = OpenTK.Mathematics.Vector4;

namespace ReLunacy.Engine.EntityManagement;

public class Entity
{
    public object instance;                 //Is either a Region.CMobyInstance or a TieInstance depending on if it's a moby or tie repsectively
    public object drawable;                 //Is either a DrawableListList or a DrawableList depending on if it's a moby or tie respectively
    public int id;
    public string name = string.Empty;
    public bool AllowRender = true;

    public Transform transform;

    //xyz is pos, w is radius
    public Vec4 boundingSphere;

    public Entity(Region.CMobyInstance mobyInstance)
    {
        instance = mobyInstance;
        drawable = AssetManager.Singleton.Mobys[mobyInstance.moby.id];
        transform = new Transform(
            mobyInstance.position.ToOpenTK(),
            mobyInstance.rotation.ToOpenTK(),
            Vec3.One * mobyInstance.scale
        );
        name = mobyInstance.name;
        ((DrawableListList)drawable).AddDrawCall(transform);
        boundingSphere = new Vec4(mobyInstance.moby.boundingSpherePosition.ToOpenTK() + transform.position, mobyInstance.moby.boundingSphereRadius * mobyInstance.scale);
    }
    public Entity(Region.CVolumeInstance volumeInstance)
    {
        instance = volumeInstance;
        drawable = AssetManager.Singleton.Cube;
        transform = new Transform(
            volumeInstance.position.ToOpenTK(),
            volumeInstance.rotation.ToOpenTK().ToEulerAngles(),
            volumeInstance.scale.ToOpenTK()
        );
        name = volumeInstance.name;
        ((Drawable)drawable).AddDrawCall(transform);
        boundingSphere = new Vec4(volumeInstance.position.ToOpenTK(), volumeInstance.scale.Length());
    }
    public Entity(CZone.CTieInstance tieInstance)
    {
        instance = tieInstance;
        drawable = AssetManager.Singleton.Ties[tieInstance.tie.id];
        transform = new Transform(tieInstance.transformation.ToOpenTK());
        name = tieInstance.name;
        ((DrawableList)drawable).AddDrawCall(transform);
        boundingSphere = new Vec4(tieInstance.boundingPosition.ToOpenTK(), tieInstance.boundingRadius);
    }
    public Entity(CZone.UFrag ufrag)
    {
        instance = ufrag;
        drawable = AssetManager.Singleton.UFrags[ufrag.GetTuid()];
        name = $"UFrag_{ufrag.GetTuid():X08}";
        transform = new Transform(ufrag.GetPosition().ToOpenTK(), Vec3.Zero, Vec3.One / (float)255f);

        ((Drawable)drawable).AddDrawCall(transform);
        ((Drawable)drawable).ConsolidateDrawCalls();
    }

    public void SetPosition(Vec3 position)
    {
        transform.position = position;
        if (drawable is DrawableListList dll) dll.UpdateTransform(transform, id);
        else if (drawable is DrawableList dl) dl.UpdateTransform(transform, id);
    }
    public void SetRotation(Vec3 rotation)
    {
        transform.SetRotation(rotation);
        if (drawable is DrawableListList dll) dll.UpdateTransform(transform, id);
        else if (drawable is DrawableList dl) dl.UpdateTransform(transform, id);
    }
    public void SetScale(Vec3 scale)
    {
        transform.scale = scale;
        if (drawable is DrawableListList dll) dll.UpdateTransform(transform, id);
        else if (drawable is DrawableList dl) dl.UpdateTransform(transform, id);
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
        Vec3 localPos = position - boundingSphere.Xyz;
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
