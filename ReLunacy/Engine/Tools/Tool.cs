using Vector3 = OpenTK.Mathematics.Vector3;
using Quaternion = OpenTK.Mathematics.Quaternion;
namespace ReLunacy.Engine.Tools;

// Credits to github.com/RatchetModding/Replanetizer

public abstract class Tool(Toolbox tb) : IDisposable
{
    public float transformMultiplier { get; set; } = 50;

    protected Toolbox Toolbox { get; set; } = tb;
    public abstract ToolType ToolType { get; }


    protected int vbo;
    protected int vao;
    protected float[] vb = [
        0, 0, 0,
        0, 0, 0,
        0, 0, 0,
        0, 0, 0,
        0, 0, 0,
        0, 0, 0
    ];
    private static float SCREEN_SPACE_SCALE => Program.Settings.ToolsGizmoSize;

    protected void BindVAO()
    {
        if(vao == 0)
        {
            GL.GenVertexArrays(1, out vao);
            GL.BindVertexArray(vao);

            if(vbo == 0)
            {
                GL.GenBuffers(1, out vbo);
                GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
                GL.BufferData(BufferTarget.ArrayBuffer, vb.Length * sizeof(float), vb, BufferUsageHint.StaticDraw);
            }
            else
            {
                GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            }

            GLUtil.ActivateNumberOfVertexAttribArrays(1);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 3, 0);
        }
        else
        {
            GL.BindVertexArray(vao);
        }
    }

    public abstract void Render(Matrix4 mat, Material material);

    public void Render(Vector3 pos, Camera camera, Material material)
    {
        var mat = GetModelMatrixByCamDist(pos, camera);
        Render(mat, material);
    }

    public void Render(Vector3 pos, Quaternion rot, Camera camera, Material material)
    {
        var mat = GetModelMatrixByCamDist(pos, rot, camera);
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
                Render(selection.Mean, selection.NewestObject.transform.rotation, camera, material);
            else
                Render(selection.Mean, camera, material);
        }
    }

    protected virtual Vector3 ProcessVec(Vector3 direction, Vector3 magnitude)
    {
        return direction * magnitude * transformMultiplier;
    }

    protected float getLineIntersectedDist(Vector3 x, Vector3 dx, Vector3 y, Vector3 dy)
    {
        Vector3 g = y - x;
        Vector3 h = Vector3.Cross(dy, g);
        Vector3 k = Vector3.Cross(dy, dx);

        float ha = h.Length;
        float ka = k.Length;

        if (ha == 0.0f || ka == 0.0f)
        {
            return 0.0f;
        }

        float sign = (Vector3.Dot(h, k) >= 0.0f) ? 1.0f : -1.0f;

        return (ha / ka) * sign;
    }

    public virtual void Reset() { }

    protected static Matrix4 GetModelMatrixByCamDist(Vector3 pos, Camera camera)
    {
        float camDist = (float)camera.transform.position.DistanceFrom(pos.ToNumerics());
        return Matrix4.CreateScale(camDist * SCREEN_SPACE_SCALE) * Matrix4.CreateTranslation(pos);
    }

    protected static Matrix4 GetModelMatrixByCamDist(Vector3 pos, Quaternion rot, Camera camera)
    {
        float camDist = (float)camera.transform.position.DistanceFrom(pos.ToNumerics());
        return Matrix4.CreateScale(camDist * SCREEN_SPACE_SCALE) * Matrix4.CreateFromQuaternion(rot) * Matrix4.CreateTranslation(pos);  
    }

    public void Dispose()
    {
        GL.DeleteBuffer(vbo);
        GL.DeleteVertexArray(vao);
    }
}
