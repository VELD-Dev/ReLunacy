namespace ReLunacy.Engine;

public class Transform
{
    public Vec3 position = Vec3.Zero;
    public Quat rotation
    {
        get
        {
            return Quat.FromEulerAngles(eulerRotation.ToOpenTK());
        }
        set
        {
            eulerRotation = value.ToEulerAngles();
        }
    }
    public Vec3 eulerRotation;
    public Vec3 scale = Vec3.One;

    private Matrix4 modelMatrix;
    public bool useMatrix = false;

    public bool updated = false;

    public Vec3 Forward
    {
        get
        {
            return Quat.Invert(rotation) * Vec3.UnitZ;
        }
    }

    public Vec3 Up
    {
        get
        {
            return Quat.Invert(rotation) * Vec3.UnitY;
        }
    }

    public Vec3 Right
    {
        get
        {
            return Quat.Invert(rotation) * Vec3.UnitX;
        }
    }

    public Transform()
    {
        position = Vec3.Zero;
        SetRotation(Vec3.Zero);
        scale = Vec3.One;
    }

    public Transform(Vec3 position, Vec3 rotation, Vec3 scale)
    {
        this.position = position;
        SetRotation(rotation);
        this.scale = scale;
    }

    public Transform(Vec3 position, Quat rotation, Vec3 scale)
    {
        this.position = position;
        SetRotation(rotation);
        this.scale = scale;
    }

    public Transform(Mat4 mat)
    {
        //useMatrix = true;
        modelMatrix = mat;
        position = mat.ExtractTranslation().ToNumerics() * YardToMeter;
        scale = mat.ExtractScale().ToNumerics() * YardToMeter;
        SetRotation(mat.ExtractRotation().ToEulerAngles());
    }

    public void SetRotation(Quat quaternion)
    {
        rotation = quaternion;
    }

    public void SetRotation(Vec3 euler)
    {
        rotation = new Quat(euler);
    }

    public Matrix4 GetLocalToWorldMatrix()
    {
        //if (useMatrix) return modelMatrix;
        return Mat4.Identity * Mat4.CreateScale(scale) * Mat4.CreateFromQuaternion(rotation) * Mat4.CreateTranslation(position);
    }
}
