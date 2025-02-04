namespace ReLunacy.Engine;

public class Transform
{
    public Vec3 Position { get; set {
            field = value;
            UpdateTransformMatrix();
        } }
    public Quat Rotation
    {
        get
        {
            return Quat.FromEulerAngles(eulerRotation);
        }
        set
        {
            eulerRotation = value.ToEulerAngles();
        }
    }
    public Vec3 eulerRotation;
    public Vec3 scale = Vec3.One;

    public Mat4 Reflection { get; set; } = Mat4.Identity;
    private Mat4 modelMatrix;
    public Mat4 Matrix
    {
        get => modelMatrix;
        set
        {
            modelMatrix = value;
            Rotation = value.ExtractRotation();
            Position = value.ExtractTranslation() * YardToMeter;
            scale = value.ExtractScale() * YardToMeter;
        }
    }
    public bool useMatrix = false;

    public bool updated = false;

    public Vec3 Forward
    {
        get
        {
            return Rotation.Inverted() * Vec3.UnitZ;
        }
    }

    public Vec3 Up
    {
        get
        {
            return Rotation.Inverted() * Vec3.UnitY;
        }
    }

    public Vec3 Right
    {
        get
        {
            return Rotation.Inverted() * Vec3.UnitX;
        }
    }

    public Transform()
    {
        Position = Vec3.Zero;
        SetRotation(Vec3.Zero);
        scale = Vec3.One;
    }

    public Transform(Vec3 position, Vec3 rotation, Vec3 scale)
    {
        this.Position = position;
        SetRotation(rotation);
        this.scale = scale;
    }

    public Transform(Vec3 position, Quat rotation, Vec3 scale)
    {
        this.Position = position;
        SetRotation(rotation);
        this.scale = scale;
    }

    public Transform(Mat4 mat)
    {
        //useMatrix = true;
        modelMatrix = mat;
        Position = mat.ExtractTranslation();
        scale = mat.ExtractScale();
        SetRotation(mat.ExtractRotation().ToEulerAngles());
    }

    public void UpdateTransformMatrix()
    {
        modelMatrix = Reflection * Mat4.CreateScale(scale) * Mat4.CreateFromQuaternion(Rotation) * Mat4.CreateTranslation(Position);
    }

    public void SetPosition(Vec3 pos)
    {
        Position = pos;
        
    }

    public void SetRotation(Quat quaternion)
    {
        Rotation = quaternion;
    }

    public void SetRotation(Vec3 euler)
    {
        Rotation = new Quat(euler);
    }

    public Matrix4 GetLocalToWorldMatrix()
    {
        //if (useMatrix) return modelMatrix;
        return Mat4.Identity * Mat4.CreateScale(scale) * Mat4.CreateFromQuaternion(Rotation) * Mat4.CreateTranslation(Position);
    }
}
