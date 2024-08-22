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

    public void UpdatePerspective()
    {
        ViewToClip = Matrix4.CreatePerspectiveFieldOfView(FOV, Aspect, NearClipDistance, RenderDistance);
    }
}
