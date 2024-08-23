using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;
using Vec2 = OpenTK.Mathematics.Vector2;
using Vec3 = OpenTK.Mathematics.Vector3;
using Vec4 = OpenTK.Mathematics.Vector4;
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
    private Vector2 CamLocal;

    #region Cam Settings

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

    #endregion

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
        CamLocal = angle;
        //CamLocal.Y = Math.Clamp(CamLocal.Y, -MathHelper.PiOver2 + 0.0001f, MathHelper.PiOver2 - 0.0001f);
        transform.SetRotation(Quat.FromAxisAngle(Vec3.UnitX, angle.Y) * Quat.FromAxisAngle(Vec3.UnitY, angle.X));
    }

    public void Rotate(Vector2 angle)
    {
        CamLocal.X += angle.X;
        CamLocal.Y += angle.Y;
        
        SetRotation(CamLocal);
    }

    public void UpdatePerspective()
    {
        ViewToClip = Matrix4.CreatePerspectiveFieldOfView(FOV, Aspect, NearClipDistance, RenderDistance);
    }

    public Vec3 CreateRay(Vector2 castPositionOnClip, Vector2 frameSize)
    {
        Vec3 viewport = new(
            (2 * castPositionOnClip.X) / frameSize.X - 1,
            1 - (2 * castPositionOnClip.Y) / frameSize.Y,
            1
        );
        Vec4 homogeneousClip = new(viewport.X, viewport.Y, -1, 1);
        Vec4 eye = Matrix4.Invert(Matrix4.Transpose(ViewToClip)) * homogeneousClip;
        eye.Z = -1;
        eye.W = 0;
        Vec3 worldRayAngleFromCam = (Matrix4.Invert(Matrix4.Transpose(WorldToView)) * eye).Xyz;
        worldRayAngleFromCam.Normalize();
        return worldRayAngleFromCam;
    }
}
