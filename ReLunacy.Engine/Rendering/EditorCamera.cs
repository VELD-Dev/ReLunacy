using System.Numerics;

namespace ReLunacy.Engine.Rendering;

/// <summary>A look-at camera: position, target, up, and a perspective projection.</summary>
public sealed class EditorCamera
{
    public Vector3 Position;
    public Vector3 Target;
    public Vector3 Up;

    /// <summary>Vertical field of view, in degrees.</summary>
    public float Fov;
    public float NearPlane;
    public float FarPlane;

    public float AspectRatio { get; private set; } = 1f;

    private Matrix4x4 _view = Matrix4x4.Identity;
    private Matrix4x4 _projection = Matrix4x4.Identity;

    public EditorCamera(Vector3 position, Vector3 target, Vector3 up, float fov, float nearPlane, float farPlane)
    {
        Position = position;
        Target = target;
        Up = up;
        Fov = fov;
        NearPlane = nearPlane;
        FarPlane = farPlane;
        Update();
    }

    /// <summary>Rebuilds the view and projection matrices: right-handed look-at, right-handed
    /// perspective with +Y up.</summary>
    public void Update()
    {
        _projection = Matrix4x4.CreatePerspectiveFieldOfView(
            float.DegreesToRadians(Fov), AspectRatio, NearPlane, FarPlane);
        _view = Matrix4x4.CreateLookAt(Position, Target, Up);
    }

    public void Resize(uint width, uint height)
    {
        if (width == 0 || height == 0) return;
        AspectRatio = width / (float)height;
    }

    public Matrix4x4 GetView() => _view;
    public Matrix4x4 GetProjection() => _projection;

    public Vector3 GetForward() => Vector3.Normalize(Target - Position);

    /// <summary>Not normalized; callers that need a unit vector normalize it themselves.</summary>
    public Vector3 GetRight() => Vector3.Cross(GetForward(), Up);

    public float GetYaw() => float.RadiansToDegrees(MathF.Atan2(GetForward().X, GetForward().Z));

    public float GetPitch() => float.RadiansToDegrees(MathF.Asin(Math.Clamp(GetForward().Y, -1f, 1f)));

    /// <summary>Rotates about <see cref="Up"/> by (angle - current yaw). <paramref name="rotateAroundTarget"/>
    /// orbits the position about the target; otherwise the target swings about the position.</summary>
    public void SetYaw(float angle, bool rotateAroundTarget)
    {
        float delta = float.DegreesToRadians(angle - GetYaw());
        Vector3 rotated = RotateByAxisAngle(Target - Position, Up, delta);
        if (rotateAroundTarget) Position = Target - rotated;
        else Target = Position + rotated;
    }

    /// <summary>Rotates about <see cref="GetRight"/> by (angle - current pitch), clamped so the view
    /// direction can never reach or cross either pole.</summary>
    public void SetPitch(float angle, bool rotateAroundTarget)
    {
        float delta = float.DegreesToRadians(angle - GetPitch());
        Vector3 toTarget = Target - Position;

        float maxUp = AngleBetween(Up, toTarget) - 0.001f;
        float maxDown = -AngleBetween(-Up, toTarget) + 0.001f;
        delta = Math.Clamp(delta, maxDown, maxUp);

        Vector3 rotated = RotateByAxisAngle(toTarget, GetRight(), delta);
        if (rotateAroundTarget) Position = Target - rotated;
        else Target = Position + rotated;
    }

    /// <summary>Dollies along the view direction. Positive <paramref name="delta"/> pulls away from the
    /// target; the distance is floored just above zero so the look-at never degenerates.</summary>
    public void MoveToTarget(float delta)
    {
        float distance = Vector3.Distance(Position, Target) + delta;
        if (!(distance > 0f)) distance = 0.001f;
        Position = Target + GetForward() * -distance;
    }

    private static Vector3 RotateByAxisAngle(Vector3 v, Vector3 axis, float angle) =>
        Vector3.Transform(v, Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angle));

    private static float AngleBetween(Vector3 a, Vector3 b)
    {
        float denominator = a.Length() * b.Length();
        if (denominator <= 0f) return 0f;
        return MathF.Acos(Math.Clamp(Vector3.Dot(a, b) / denominator, -1f, 1f));
    }
}
