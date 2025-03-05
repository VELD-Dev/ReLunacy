namespace ReLunacy.Engine.Tools;

// Credits to github.com/RatchetModding/Replanetizer

public abstract class Tool(Toolbox tb) : IDisposable
{
    public float transformMultiplier { get; set; } = 50;

    protected Toolbox Toolbox { get; set; } = tb;
    public abstract ToolType ToolType { get; }
    private static float SCREEN_SPACE_SCALE => Program.Settings.ToolsGizmoSize;

    protected void Update()
    {
        //ImGuizmo.SetDrawlist();
        //ImGuizmo.SetRect()
    }

    public abstract void Render(Mat4 mat, Material material);

    public void Render(Vec3 pos, Camera camera, Material material)
    {
        var mat = GetModelMatrixByCamDist(pos, camera);
        Render(mat, material);
    }

    public void Render(Vec3 pos, Vec3 rot, Camera camera, Material material)
    {
        var mat = GetModelMatrixByCamDist(pos, Quat.FromEulerAngles(rot), camera);  // Maybe..?
        Render(mat, material);
    }

    public void Render(Selection selection, Camera camera, Material material)
    {
        if(Toolbox.TransformSpace == TransformSpace.Global)
        {
            Render(selection.Mean, camera, material);
        }
        else if(Toolbox.TransformSpace == TransformSpace.Local)
        {
            if (selection.NewestObject != null)
                Render(selection.Mean, selection.NewestObject.Transform.EulerRotation, camera, material);
            else
                Render(selection.Mean, camera, material);
        }
    }

    protected virtual Vec3 ProcessVec(Vec3 direction, Vec3 magnitude)
    {
        return direction * magnitude * transformMultiplier;
    }

    protected float GetLineIntersectedDist(Vec3 x, Vec3 dx, Vec3 y, Vec3 dy)
    {
        Vec3 g = y - x;
        Vec3 h = Vec3.Cross(dy, g);
        Vec3 k = Vec3.Cross(dy, dx);

        float ha = h.Length;
        float ka = k.Length;

        if (ha == 0.0f || ka == 0.0f)
        {
            return 0.0f;
        }

        float sign = (Vec3.Dot(h, k) >= 0.0f) ? 1.0f : -1.0f;

        return (ha / ka) * sign;
    }

    public virtual void Reset() { }

    protected static Matrix4 GetModelMatrixByCamDist(Vec3 pos, Camera camera)
    {
        float camDist = (float)camera.transform.Position.DistanceFrom(pos);
        return Mat4.CreateScale(camDist * SCREEN_SPACE_SCALE) * Mat4.CreateTranslation(pos);
    }

    protected static Matrix4 GetModelMatrixByCamDist(Vec3 pos, Quat rot, Camera camera)
    {
        float camDist = (float)camera.transform.Position.DistanceFrom(pos);
        return Mat4.CreateScale(camDist * SCREEN_SPACE_SCALE) * Mat4.CreateFromQuaternion(rot) * Mat4.CreateTranslation(pos);  
    }

    public void Dispose()
    {
        //ImGuizmo.Destroy(ImGuizmo.GetStyle());
    }
}
