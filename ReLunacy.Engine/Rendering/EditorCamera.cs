using System.Numerics;

namespace ReLunacy.Engine.Rendering;

/// <summary>A look-at camera: position, target, up, and a perspective projection.
///
/// Replaces Bliss's Cam3D, whose behaviour this reproduces exactly for the way the editor drives it
/// (its own Custom mode - the editor moves the camera itself and never used Cam3D's built-in movement
/// modes). Two differences are deliberate:
///
///  - <see cref="Update"/> rebuilds both matrices. Cam3D only rebuilt them inside Begin(), which was a
///    command-list call, so a view with no command list would silently keep serving the matrices from
///    whenever it last drew. Nothing here touches the graphics API at all.
///  - <see cref="GetYaw"/>/<see cref="GetPitch"/> are read straight off the forward vector instead of
///    round-tripping the view matrix through a quaternion and Euler angles. The callers only ever use
///    them as Set(Get() - delta), where the absolute value cancels out and only the delta survives, so
///    this is observably identical while being far better conditioned.</summary>
public sealed class EditorCamera
{
    public Vector3 Position;
    public Vector3 Target;
    public Vector3 Up;

    /// <summary>Vertical field of view, in DEGREES (Cam3D's convention, and what the settings store).</summary>
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

    /// <summary>Right-handed look-at, and a right-handed perspective with +Y up - the same
    /// System.Numerics calls Cam3D made, so every downstream convention is unchanged (including the
    /// renderer's negative-height viewport, which is what maps +Y-up clip space onto Vulkan).</summary>
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

    /// <summary>Deliberately NOT normalized, matching Cam3D: callers that need a unit vector normalize
    /// it themselves, and the pitch rotation only uses it as an axis (which gets normalized anyway).</summary>
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
    /// direction can never reach or cross either pole - that clamp is what stops the camera flipping
    /// upside down, so it is reproduced exactly (including the 0.001 rad guard band).</summary>
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
