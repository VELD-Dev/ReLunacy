using Vector3 = OpenTK.Mathematics.Vector3;
using Quaternion = OpenTK.Mathematics.Quaternion;

namespace ReLunacy.Engine;

public class Transform
{
    public Vector3 position = Vector3.Zero;
    public Quaternion rotation { get; private set; }
    public Vector3 eulerRotation { get; private set; }
    public Vector3 scale = Vector3.One;

    private Matrix4 modelMatrix;
    public bool useMatrix = false;

    public bool updated = false;

    public Vector3 Forward
    {
        get
        {
            return Quaternion.Invert(rotation) * Vector3.UnitZ;
        }
    }

    public Vector3 Up
    {
        get
        {
            return Quaternion.Invert(rotation) * Vector3.UnitY;
        }
    }

    public Vector3 Right
    {
        get
        {
            return Quaternion.Invert(rotation) * Vector3.UnitX;
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
    public Transform(Matrix4 mat)
    {
        useMatrix = true;
        modelMatrix = mat;
        position = mat.ExtractTranslation();
        scale = mat.ExtractScale();
        Quaternion quatRotation = mat.ExtractRotation();
        quatRotation.ToEulerAngles(out Vector3 tempEulers);
        SetRotation(tempEulers);
    }

    public void SetRotation(Quaternion quaternion)
    {
        rotation = quaternion;
        rotation.ToEulerAngles(out Vector3 tempEulers);
        eulerRotation = tempEulers;
    }
    public void SetRotation(Vector3 eulers)
    {
        eulerRotation = eulers;
        rotation = Quaternion.FromAxisAngle(Vector3.UnitZ, eulerRotation.Z) * Quaternion.FromAxisAngle(Vector3.UnitY, eulerRotation.Y) * Quaternion.FromAxisAngle(Vector3.UnitX, eulerRotation.X);
    }

    public Matrix4 GetLocalToWorldMatrix()
    {
        if (useMatrix) return modelMatrix;
        return Matrix4.Identity * Matrix4.CreateScale(scale) * Matrix4.CreateFromQuaternion(rotation) * Matrix4.CreateTranslation(position);
    }
}
