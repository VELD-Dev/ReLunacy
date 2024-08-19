using Vec3 = OpenTK.Mathematics.Vector3;
using Vec4 = OpenTK.Mathematics.Vector4; 

namespace ReLunacy.Engine;

public class Entity
{
    public object instance;                 //Is either a Region.CMobyInstance or a TieInstance depending on if it's a moby or tie repsectively
    public object drawable;                 //Is either a DrawableListList or a DrawableList depending on if it's a moby or tie respectively
    public int id;
    public string name = string.Empty;
    public bool draw = true;

    public Transform transform;

    //xyz is pos, w is radius
    public Vec4 boundingSphere;

    public static uint drawCalls;

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
        drawable = new Drawable(ufrag);
        name = $"UFrag_{ufrag.GetTuid():X08}";
        transform = new Transform(ufrag.GetPosition().ToOpenTK(), Vec3.Zero, Vec3.One / (float)255f);

        ((Drawable)drawable).AddDrawCall(transform);
        ((Drawable)drawable).ConsolidateDrawCalls();
    }

    public void SetPosition(Vec3 position)
    {
        transform.position = position;
        if (drawable is DrawableListList dll) dll.UpdateTransform(transform);
        else if (drawable is DrawableList dl) dl.UpdateTransform(transform);
    }
    public void SetRotation(Vec3 rotation)
    {
        transform.SetRotation(rotation);
        if (drawable is DrawableListList dll) dll.UpdateTransform(transform);
        else if (drawable is DrawableList dl) dl.UpdateTransform(transform);
    }
    public void SetScale(Vec3 scale)
    {
        transform.scale = scale;
        if (drawable is DrawableListList dll) dll.UpdateTransform(transform);
        else if (drawable is DrawableList dl) dl.UpdateTransform(transform);
    }
    public void Draw()
    {
        if (!draw) return;
        if (drawable is DrawableListList dll) dll.Draw();
        else if (drawable is DrawableList dl) dl.Draw();
        else if (drawable is Drawable d) d.Draw(transform);
    }
    public bool IntersectsRay(Vec3 dir, Vec3 position)
    {
        Vec3 m = position - boundingSphere.Xyz;
        float b = Vec3.Dot(m, dir);
        float c = Vec3.Dot(m, m) - boundingSphere.W * boundingSphere.W;

        if (c > 0 && b > 0) return false;

        float discriminant = b * b - c;

        return discriminant >= 0;
    }
}
