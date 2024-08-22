using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vec2 = OpenTK.Mathematics.Vector2;
using Vec3 = OpenTK.Mathematics.Vector3;
using Quaternion = System.Numerics.Quaternion;
using Quat = OpenTK.Mathematics.Quaternion;

namespace ReLunacy.Engine;

public class Camera
{
    public static Camera Main { get; set; }
    public Transform transform = new();

    private float _fov;
    private float _aspect;
    private float _nearClip;
    private float _farClip;

    public float FOV
    {
        get => _fov;
        set
        {
            _fov = value;
            UpdatePerspective();
        }
    }
    public float Aspect
    {
        get => _aspect;
        set
        {
            _aspect = value;
            UpdatePerspective();
        }
    }
    public float NearClipDistance
    {
        get => _nearClip;
        set
        {
            _nearClip = value;
            UpdatePerspective();
        }
    }
    public float RenderDistance
    {
        get => _farClip;
        set
        {
            _farClip = value;
            UpdatePerspective();
        }
    }

    public Matrix4 WorldToView
    {
        get
        {
            return Matrix4.CreateTranslation(transform.position) * Matrix4.CreateFromQuaternion(transform.rotation);
        }
    }
    public Matrix4 ViewToClip;

    public void SetPerspective(float fov, float aspect, float depthNear, float depthFar)
    {
        _fov = fov;
        _aspect = aspect;
        _nearClip = depthNear;
        _farClip = depthFar;
        UpdatePerspective();
    }

    public void SetRotation(Vector2 angle)
    {
        angle.Y = Math.Clamp(angle.Y, -MathHelper.PiOver2 + 0.0001f, MathHelper.PiOver2 - 0.0001f);
        transform.SetRotation(Quat.FromAxisAngle(Vec3.UnitY, angle.X) * Quat.FromAxisAngle(Vec3.UnitX, angle.Y));
    }

    public void Rotate(Vector2 angle)
    {
        var rot = transform.eulerRotation;
        
        rot.Z -= angle.X;
        rot.X -= angle.Y;
        rot.X = Math.Clamp(rot.X, MathHelper.Pi / 180f * -89.9f, MathHelper.Pi / 180f * 89.9f);

        transform.SetRotation(rot);
    }

    public void UpdatePerspective()
    {
        ViewToClip = Matrix4.CreatePerspectiveFieldOfView(FOV, Aspect, NearClipDistance, RenderDistance);
    }

    public void ViewToWorldRay(Matrix4 projection, Matrix4 view) { }
}
