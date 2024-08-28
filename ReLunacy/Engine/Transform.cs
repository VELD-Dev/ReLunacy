using Vector3 = System.Numerics.Vector3;
using Vec3 = OpenTK.Mathematics.Vector3;
using Quaternion = OpenTK.Mathematics.Quaternion;

namespace ReLunacy.Engine;

public class Transform
{
    public Vector3 position = Vector3.Zero;
    public Quaternion rotation
    {
        get
        {
            return Quaternion.FromEulerAngles(eulerRotation.ToOpenTK());
        }
        set
        {
            eulerRotation = value.ToEulerAngles().ToNumerics();
        }
    }
    public Vector3 eulerRotation;
    public Vector3 scale = Vector3.One;

    private Matrix4 modelMatrix;
    public bool useMatrix = false;

    public bool updated = false;

    public Vec3 Forward
    {
        get
        {
            return Quaternion.Invert(rotation) * Vec3.UnitZ;
        }
    }

    public Vec3 Up
    {
        get
        {
            return Quaternion.Invert(rotation) * Vec3.UnitY;
        }
    }

    public Vec3 Right
    {
        get
        {
            return Quaternion.Invert(rotation) * Vec3.UnitX;
        }
    }

    public Transform()
    {
        position = Vector3.Zero;
        SetRotation(Vector3.Zero);
        scale = Vector3.One;
    }

    public Transform(Vector3 position, Vector3 rotation, Vector3 scale)
    {
        this.position = position;
        SetRotation(rotation);
        this.scale = scale;
    }

    public Transform(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        this.position = position;
        SetRotation(rotation);
        this.scale = scale;
    }

    public Transform(Matrix4 mat)
    {
        //useMatrix = true;
        modelMatrix = mat;
        position = mat.ExtractTranslation().ToNumerics() * YardToMeter;
        scale = mat.ExtractScale().ToNumerics() * YardToMeter;
        SetRotation(mat.ExtractRotation().ToEulerAngles().ToNumerics());
    }

    public void SetRotation(Quaternion quaternion)
    {
        rotation = quaternion;
    }

    public void SetRotation(Vector3 euler)
    {
        rotation = new Quaternion(euler.ToOpenTK());
    }

    public Matrix4 GetLocalToWorldMatrix()
    {
        //if (useMatrix) return modelMatrix;
        return Matrix4.Identity * Matrix4.CreateScale(scale.ToOpenTK()) * Matrix4.CreateFromQuaternion(rotation) * Matrix4.CreateTranslation(position.ToOpenTK());
    }
}
