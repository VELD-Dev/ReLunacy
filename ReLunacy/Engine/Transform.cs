namespace ReLunacy.Engine;

public class Transform
{
    public Vec3 Position
    {
        get;
        set
        {
            field = value;
            UpdateTransformMatrix();
        }
    } = Vec3.Zero;

    public Quat Rotation
    {
        get;
        set
        {
            field = value;
            UpdateTransformMatrix();
        }
    } = Quat.Identity;

    public Vec3 EulerRotation
    {
        get => Rotation.ToEulerAngles();
        set => Rotation = Quat.FromEulerAngles(value);
    }

    public Vec3 Scale
    {
        get;
        set
        {
            field = value;
            UpdateTransformMatrix();
        }
    } = Vec3.One;

    public Mat4 Reflection { get; set; } = Mat4.Identity;
    public Mat4 Matrix
    {
        get;
        set
        {
            field = value;
            Rotation = value.ExtractRotation();
            Position = value.ExtractTranslation() * YardToMeter;
            Scale = value.ExtractScale() * YardToMeter;
        }
    } = Mat4.Identity;

    public Vec3 Forward => Rotation.Inverted() * Vec3.UnitZ;

    public Vec3 Up => Rotation.Inverted() * Vec3.UnitY;

    public Vec3 Right => Rotation.Inverted() * Vec3.UnitX;

    public Transform()
    {
        Position = Vec3.Zero;
        SetRotation(Vec3.Zero);
        Scale = Vec3.One;
    }

    public Transform(Vec3 position, Vec3 rotation, Vec3 scale)
    {
        Position = position;
        EulerRotation = rotation;
        Scale = scale;
    }

    public Transform(Vec3 position, Quat rotation, Vec3 scale)
    {
        Position = position;
        Rotation = rotation;
        Scale = scale;
    }

    public Transform(Mat4 mat)
    {
        //useMatrix = true;
        Matrix = mat;
        Position = mat.ExtractTranslation();
        Scale = mat.ExtractScale();
        SetRotation(mat.ExtractRotation().ToEulerAngles());
    }

    public void UpdateTransformMatrix()
    {
        Matrix = Reflection * Mat4.CreateScale(Scale) * Mat4.CreateFromQuaternion(Rotation) * Mat4.CreateTranslation(Position);
    }

    public void SetPosition(Vec3 pos)
    {
        Position = pos;
    }

    public void Translate(Vec3 delta)
    {
        Position += delta;
    }

    public void SetRotation(Quat quaternion)
    {
        Rotation = quaternion;
    }
    public void SetRotation(Vec3 euler)
    {
        EulerRotation = euler;
    }

    public void Rotate(Vec3 delta)
    {
        Rotation *= Quat.FromEulerAngles(delta);
    }

    public void SetScale(Vec3 scale)
    {
        Scale = scale;
    }

    public void SetScale(float scale)
    {
        Scale = (scale, scale, scale);
    }

    public void Rescale(Vec3 delta)
    {
        Scale += delta;
    }

    public void Rescale(float delta)
    {
        Scale += (delta, delta, delta);
    }

    public void SetMatrix(Mat4 mat)
    {
        Matrix = mat;
    }

    public Mat4 GetLocalToWorldMatrix()
    {
        //if (useMatrix) return modelMatrix;
        return Mat4.Identity * Mat4.CreateScale(Scale) * Mat4.CreateFromQuaternion(Rotation) * Mat4.CreateTranslation(Position);
    }
}
